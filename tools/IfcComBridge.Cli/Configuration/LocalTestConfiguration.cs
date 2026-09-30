using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IfcComBridge.Cli.Baseline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Cli.Configuration
{
    /// <summary>Every problem found in a configuration, reported together.</summary>
    public sealed class ConfigurationException : Exception
    {
        public ConfigurationException(string configPath, IReadOnlyList<string> problems)
            : base($"Invalid test configuration '{configPath}':" + string.Concat(problems.Select(p => Environment.NewLine + "  - " + p)))
        {
            ConfigPath = configPath;
            Problems = problems;
        }

        public string ConfigPath { get; }
        public IReadOnlyList<string> Problems { get; }
    }

    /// <summary>A single IFC4 model, optionally with transforms for UpdateModel.</summary>
    public sealed class ModelScenario
    {
        public string Ifc { get; internal set; }
        /// <summary>Optional; null when not configured.</summary>
        public string Transforms { get; internal set; }
    }

    /// <summary>The four inputs of LoadIfcJson.</summary>
    public sealed class CompositionScenario
    {
        public string Building { get; internal set; }
        public string Products { get; internal set; }
        public string ProductsMap { get; internal set; }
        public string Layout { get; internal set; }
    }

    /// <summary>
    /// The private local test configuration (tests.local.json). This is the ONE parser and validator:
    /// the CLI uses it directly, the scripts only call the CLI, and the integration tests reference it.
    ///
    /// <code>
    /// { "version": 1,
    ///   "scenarios": {
    ///     "model":       { "ifc": "...", "transforms": "..." },            // optional; transforms optional
    ///     "composition": { "building": "...", "products": "...",
    ///                      "productsMap": "...", "layout": "..." } } }     // optional; all four required
    /// </code>
    ///
    /// Relative paths are resolved against the folder of the configuration file. The configuration and
    /// every input must be outside the repository. Unknown properties are errors, so typos never silently
    /// disable a scenario. Input files get the checks the library itself hard-requires (existence, IFC4
    /// STEP header, JSON root shapes); deeper content rules are library behaviour and stay untouched.
    /// </summary>
    public sealed class LocalTestConfiguration
    {
        public const int SupportedVersion = 1;
        public const string ModelScenarioName = "model";
        public const string CompositionScenarioName = "composition";

        private static readonly string[] TopLevelProperties = { "version", "scenarios", "$schema" };
        private static readonly string[] ScenarioNames = { ModelScenarioName, CompositionScenarioName };

        private LocalTestConfiguration(string configPath)
        {
            ConfigPath = configPath;
        }

        public string ConfigPath { get; }
        public ModelScenario Model { get; private set; }
        public CompositionScenario Composition { get; private set; }
        public bool HasAnyScenario => Model != null || Composition != null;

        public static LocalTestConfiguration Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("No configuration path given.", nameof(path));

            string full = Path.GetFullPath(path.Trim().Trim('"'));
            if (Directory.Exists(full))
                throw new ConfigurationException(full, new[] { "the path is a directory; expected the tests.local.json file" });
            if (!File.Exists(full))
                throw new ConfigurationException(full, new[] { "configuration file not found" });

            var problems = new List<string>();
            string repository = RepositoryGuard.FindRepositoryRoot(Path.GetDirectoryName(full));
            if (repository != null)
                problems.Add($"the configuration file is inside the repository ({repository}); keep tests.local.json outside it");

            JToken root;
            try
            {
                root = JToken.Parse(File.ReadAllText(full));
            }
            catch (JsonException ex)
            {
                problems.Add("not valid JSON: " + ex.Message);
                throw new ConfigurationException(full, problems);
            }

            if (!(root is JObject document))
            {
                problems.Add("the root must be a JSON object");
                throw new ConfigurationException(full, problems);
            }

            foreach (JProperty unknown in document.Properties().Where(p => !TopLevelProperties.Contains(p.Name)))
                problems.Add($"unknown property '{unknown.Name}' (expected: version, scenarios)");

            JToken version = document["version"];
            if (version == null)
                problems.Add($"missing 'version' (expected {SupportedVersion})");
            else if (version.Type != JTokenType.Integer)
                problems.Add("'version' must be an integer");
            else if ((long)version != SupportedVersion)
                problems.Add($"unsupported version {version} (this harness reads version {SupportedVersion})");

            var config = new LocalTestConfiguration(full);
            string baseDirectory = Path.GetDirectoryName(full);
            JToken scenarios = document["scenarios"];
            if (scenarios == null)
            {
                problems.Add("missing 'scenarios' (an object; use {} when nothing is configured)");
            }
            else if (!(scenarios is JObject scenarioObject))
            {
                problems.Add("'scenarios' must be an object");
            }
            else
            {
                foreach (JProperty unknown in scenarioObject.Properties().Where(p => !ScenarioNames.Contains(p.Name)))
                    problems.Add($"unknown scenario '{unknown.Name}' (known: {string.Join(", ", ScenarioNames)})");

                JObject model = Section(scenarioObject, ModelScenarioName, problems);
                if (model != null)
                {
                    var reader = new SectionReader(model, "scenarios." + ModelScenarioName, baseDirectory, problems, "ifc", "transforms");
                    config.Model = new ModelScenario
                    {
                        Ifc = reader.Path("ifc", required: true, InputKind.Ifc),
                        Transforms = reader.Path("transforms", required: false, InputKind.Transforms),
                    };
                }

                JObject composition = Section(scenarioObject, CompositionScenarioName, problems);
                if (composition != null)
                {
                    var reader = new SectionReader(composition, "scenarios." + CompositionScenarioName, baseDirectory, problems,
                        "building", "products", "productsMap", "layout");
                    config.Composition = new CompositionScenario
                    {
                        Building = reader.Path("building", required: true, InputKind.Ifc),
                        Products = reader.Path("products", required: true, InputKind.Ifc),
                        ProductsMap = reader.Path("productsMap", required: true, InputKind.ProductsMap),
                        Layout = reader.Path("layout", required: true, InputKind.Layout),
                    };
                }
            }

            if (problems.Count > 0)
                throw new ConfigurationException(full, problems);
            return config;
        }

        /// <summary>The configured inputs in the shape the baseline runner uses.</summary>
        public BaselineInputs ToBaselineInputs() => new BaselineInputs
        {
            Ifc = Model?.Ifc,
            Transforms = Model?.Transforms,
            Building = Composition?.Building,
            Products = Composition?.Products,
            ProductsMap = Composition?.ProductsMap,
            Layout = Composition?.Layout,
        };

        /// <summary>The resolved input paths as JSON (null for a scenario that is not configured), for scripts.</summary>
        public JObject ToJson() => new JObject
        {
            ["config"] = ConfigPath,
            ["version"] = SupportedVersion,
            [ModelScenarioName] = Model == null ? null : new JObject
            {
                ["ifc"] = Model.Ifc,
                ["transforms"] = Model.Transforms,
            },
            [CompositionScenarioName] = Composition == null ? null : new JObject
            {
                ["building"] = Composition.Building,
                ["products"] = Composition.Products,
                ["productsMap"] = Composition.ProductsMap,
                ["layout"] = Composition.Layout,
            },
        };

        public IEnumerable<string> Describe()
        {
            yield return $"configuration: {ConfigPath} (version {SupportedVersion}) - valid";
            if (Model == null)
            {
                yield return "  model:       not configured";
            }
            else
            {
                yield return "  model:       enabled";
                yield return $"    ifc:          {Model.Ifc}";
                yield return $"    transforms:   {Model.Transforms ?? "(not configured)"}";
            }

            if (Composition == null)
            {
                yield return "  composition: not configured";
            }
            else
            {
                yield return "  composition: enabled";
                yield return $"    building:     {Composition.Building}";
                yield return $"    products:     {Composition.Products}";
                yield return $"    productsMap:  {Composition.ProductsMap}";
                yield return $"    layout:       {Composition.Layout}";
            }
        }

        /// <summary>An absent or null scenario is simply not configured; any other non-object is an error.</summary>
        private static JObject Section(JObject scenarios, string name, List<string> problems)
        {
            JToken token = scenarios[name];
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (token is JObject section)
                return section;
            problems.Add($"scenarios.{name} must be an object (or be left out)");
            return null;
        }

        private sealed class SectionReader
        {
            private readonly JObject _section;
            private readonly string _prefix;
            private readonly string _baseDirectory;
            private readonly List<string> _problems;

            public SectionReader(JObject section, string prefix, string baseDirectory, List<string> problems, params string[] fields)
            {
                _section = section;
                _prefix = prefix;
                _baseDirectory = baseDirectory;
                _problems = problems;
                foreach (JProperty unknown in section.Properties().Where(p => !fields.Contains(p.Name)))
                    problems.Add($"{prefix}: unknown field '{unknown.Name}' (expected: {string.Join(", ", fields)})");
            }

            public string Path(string field, bool required, InputKind kind)
            {
                string name = $"{_prefix}.{field}";
                JToken token = _section[field];
                if (token == null || token.Type == JTokenType.Null)
                {
                    if (required)
                        _problems.Add($"{name}: missing (required when the scenario is present)");
                    return null;
                }

                if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                {
                    _problems.Add($"{name}: must be a non-empty path string");
                    return null;
                }

                string raw = ((string)token).Trim();
                string full;
                try
                {
                    full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(raw) ? raw : System.IO.Path.Combine(_baseDirectory, raw));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    _problems.Add($"{name}: invalid path '{raw}' ({ex.Message})");
                    return null;
                }

                string problem = InputValidator.Check(full, kind);
                if (problem != null)
                {
                    _problems.Add($"{name}: {problem}");
                    return null;
                }
                return full;
            }
        }
    }

    internal enum InputKind
    {
        Ifc,
        ProductsMap,
        Layout,
        Transforms,
    }

    /// <summary>
    /// Checks the library hard-requires before any real work: without them it fails immediately.
    /// Returns a problem description or null.
    /// </summary>
    internal static class InputValidator
    {
        private const int HeaderChars = 64 * 1024;

        public static string Check(string path, InputKind kind)
        {
            if (Directory.Exists(path))
                return $"is a directory, expected a file: {path}";
            if (!File.Exists(path))
                return $"file not found: {path}";

            string repository = RepositoryGuard.FindRepositoryRoot(Path.GetDirectoryName(path));
            if (repository != null)
                return $"is inside the repository ({repository}); private inputs must stay outside it: {path}";

            switch (kind)
            {
                case InputKind.Ifc:
                    return CheckIfc(path);
                case InputKind.ProductsMap:
                    // COM LoadIfcJson: JObject.Parse(productsMapJson); values are read as objects or arrays.
                    return CheckJson(path, root =>
                    {
                        if (!(root is JObject map))
                            return "the root must be a JSON object (products map)";
                        int bad = FirstIndex(map.Properties(), p => !(p.Value is JObject || p.Value is JArray));
                        return bad < 0 ? null : $"entry #{bad + 1} is neither an object nor an array";
                    });
                case InputKind.Layout:
                    // LoadIfcJson enumerates the root as groups and reads group["sets"].
                    return CheckJson(path, root =>
                    {
                        if (!(root is JArray groups))
                            return "the root must be a JSON array of groups (layout)";
                        int bad = FirstIndex(groups, g => !(g is JObject));
                        return bad < 0 ? null : $"group #{bad + 1} is not an object";
                    });
                case InputKind.Transforms:
                    // UpdateIfcPosition iterates root["modified_products"].
                    return CheckJson(path, root =>
                        root is JObject transforms && transforms["modified_products"] is JArray
                            ? null
                            : "the root must be an object with a 'modified_products' array (transforms)");
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static string CheckIfc(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".ifczip" || extension == ".ifcxml")
                return null; // container formats: the library reports problems itself
            if (extension != ".ifc")
                return $"unsupported extension '{extension}' (expected .ifc, .ifczip or .ifcxml): {path}";

            string head = ReadHead(path);
            if (!head.TrimStart('\uFEFF', ' ', '\t', '\r', '\n').StartsWith("ISO-10303-21", StringComparison.Ordinal))
                return $"not an IFC STEP file (no ISO-10303-21 header): {path}";

            Match schema = Regex.Match(head, @"FILE_SCHEMA\s*\(\s*\(\s*'([^']*)'", RegexOptions.IgnoreCase);
            if (!schema.Success)
                return $"no FILE_SCHEMA found in the header: {path}";

            string name = schema.Groups[1].Value.Trim().ToUpperInvariant();
            bool ifc4 = name.StartsWith("IFC4", StringComparison.Ordinal) && !name.StartsWith("IFC4X", StringComparison.Ordinal);
            return ifc4 ? null : $"schema '{schema.Groups[1].Value}' is not supported: the library accepts IFC4 only: {path}";
        }

        private static string ReadHead(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var buffer = new char[HeaderChars];
                int read = reader.ReadBlock(buffer, 0, buffer.Length);
                return new string(buffer, 0, read);
            }
        }

        private static int FirstIndex<T>(IEnumerable<T> items, Func<T, bool> predicate)
        {
            int index = 0;
            foreach (T item in items)
            {
                if (predicate(item))
                    return index;
                index++;
            }
            return -1;
        }

        private static string CheckJson(string path, Func<JToken, string> shape)
        {
            JToken root;
            try
            {
                // Same parser settings as the library (JToken/JObject.Parse).
                root = JToken.Parse(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                return $"not valid JSON ({ex.Message}): {path}";
            }

            string problem = shape(root);
            return problem == null ? null : $"{problem}: {path}";
        }
    }
}
