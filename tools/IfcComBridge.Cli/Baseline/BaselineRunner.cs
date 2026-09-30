using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using IfcComBridge.Cli.Commands;
using IfcComBridge.Cli.Summary;
using Newtonsoft.Json.Linq;
using IfcComBridge;

namespace IfcComBridge.Cli.Baseline
{
    public sealed class BaselineInputs
    {
        public string Ifc { get; set; }
        public string Building { get; set; }
        public string Products { get; set; }
        public string ProductsMap { get; set; }
        public string Layout { get; set; }
        public string Transforms { get; set; }

        public bool HasSingleModel => Ifc != null;
        public bool HasCompose => Building != null && Products != null && ProductsMap != null && Layout != null;
        public bool HasUpdate => Ifc != null && Transforms != null;

        public IEnumerable<KeyValuePair<string, string>> All()
        {
            var roles = new Dictionary<string, string>
            {
                ["ifc"] = Ifc,
                ["building"] = Building,
                ["products"] = Products,
                ["productsMap"] = ProductsMap,
                ["layout"] = Layout,
                ["transforms"] = Transforms,
            };
            return roles.Where(r => r.Value != null);
        }
    }

    /// <summary>
    /// Runs every characterization step against the current library and writes the results as
    /// comparable *.summary.json files (plus the raw IFC/WexBIM outputs for manual inspection).
    ///
    /// The same code produces a baseline (before refactoring) and the "current" run it is compared
    /// with, so any difference comes from the library, not from the harness. A step that throws is
    /// recorded as an error summary: an exception is characterized behaviour too.
    /// </summary>
    public static class BaselineRunner
    {
        public const string ManifestFile = "manifest.json";

        public static JObject Run(BaselineInputs inputs, string outDir, Action<string> log)
        {
            log = log ?? (_ => { });
            if (!inputs.HasSingleModel && !inputs.HasCompose)
                throw new UsageException("Nothing to capture: give --ifc and/or the four compose inputs.");

            var steps = new JArray();
            Step(steps, outDir, "engine", log, dir =>
            {
                using (var runtime = new ComRuntime())
                    WriteSummary(dir, "engine", new JObject { ["doXBimLibTest"] = runtime.DoXBimLibTest() });
            });

            if (inputs.HasSingleModel)
            {
                Step(steps, outDir, "single", log, dir =>
                {
                    WriteSummary(dir, "input", ModelSummarizer.Summarize(inputs.Ifc));
                    string ifc = Path.Combine(dir, "roundtrip.ifc");
                    string wexbim = Path.Combine(dir, "roundtrip.wexbim");
                    using (var runtime = new ComRuntime())
                    {
                        runtime.LoadIfc(inputs.Ifc);
                        runtime.SaveIfc(ifc, 0);
                        runtime.SaveWexbim(wexbim, 0);
                    }
                    WriteSummary(dir, "roundtrip", ModelSummarizer.Summarize(ifc, wexbim));
                });
            }

            if (inputs.HasCompose)
            {
                Step(steps, outDir, Path.Combine("compose", "runtime"), log, dir =>
                {
                    IReadOnlyList<ComposedModelFile> models = ComposeRunner.ViaRuntime(
                        inputs.Building, inputs.Products, inputs.ProductsMap, inputs.Layout, dir, withWexbim: true);
                    WriteComposeSummaries(dir, models);
                });

                Step(steps, outDir, Path.Combine("compose", "utils"), log, dir =>
                {
                    string missingLog = Path.Combine(dir, "missing-items.log");
                    IReadOnlyList<ComposedModelFile> models = ComposeRunner.ViaUtils(
                        inputs.Building, inputs.Products, inputs.ProductsMap, inputs.Layout, dir, withWexbim: true, missingLog);
                    WriteComposeSummaries(dir, models);
                    string[] lines = File.Exists(missingLog) ? File.ReadAllLines(missingLog) : new string[0];
                    WriteSummary(dir, "missing-items", new JObject { ["lines"] = new JArray(lines) });
                });
            }

            if (inputs.HasUpdate)
            {
                Step(steps, outDir, "update", log, dir =>
                {
                    string ifc = Path.Combine(dir, "updated.ifc");
                    string wexbim = Path.Combine(dir, "updated.wexbim");
                    bool changed;
                    using (var runtime = new ComRuntime())
                    {
                        runtime.LoadIfc(inputs.Ifc);
                        changed = runtime.UpdateModel(inputs.Transforms, 0);
                        runtime.SaveIfc(ifc, 0);
                        runtime.SaveWexbim(wexbim, 0);
                    }
                    WriteSummary(dir, "result", new JObject { ["updateModelReturned"] = changed });
                    WriteSummary(dir, "updated", ModelSummarizer.Summarize(ifc, wexbim));
                });
            }

            var manifest = new JObject
            {
                ["summaryFormat"] = ModelSummarizer.FormatVersion,
                ["createdUtc"] = DateTime.UtcNow.ToString("o"),
                ["libraryAssembly"] = typeof(ComRuntime).Assembly.GetName().Name,
                ["librarySha256"] = Sha256(typeof(ComRuntime).Assembly.Location),
                // File names and hashes only: the manifest must not carry private folder paths.
                ["inputs"] = new JArray(inputs.All().Select(i => new JObject
                {
                    ["role"] = i.Key,
                    ["fileName"] = Path.GetFileName(i.Value),
                    ["bytes"] = new FileInfo(i.Value).Length,
                    ["sha256"] = Sha256(i.Value),
                })),
                ["steps"] = steps,
            };
            File.WriteAllText(Path.Combine(outDir, ManifestFile), ModelSummarizer.ToJsonText(manifest), new UTF8Encoding(false));
            return manifest;
        }

        private static void Step(JArray steps, string outDir, string name, Action<string> log, Action<string> body)
        {
            string dir = Path.Combine(outDir, name);
            Directory.CreateDirectory(dir);
            log($"baseline: {name} ...");
            try
            {
                body(dir);
                steps.Add(new JObject { ["step"] = name.Replace('\\', '/'), ["ok"] = true });
            }
            catch (Exception ex)
            {
                Exception inner = Program.Unwrap(ex);
                // Output paths differ between runs; keep messages comparable.
                string message = inner.Message.Replace(outDir, "<out>");
                WriteSummary(dir, "error", new JObject { ["exception"] = inner.GetType().FullName, ["message"] = message });
                steps.Add(new JObject { ["step"] = name.Replace('\\', '/'), ["ok"] = false, ["exception"] = inner.GetType().FullName });
                log($"baseline: {name} threw {inner.GetType().Name}: {inner.Message}");
            }
        }

        private static void WriteComposeSummaries(string dir, IReadOnlyList<ComposedModelFile> models)
        {
            WriteSummary(dir, "result", new JObject
            {
                ["modelCount"] = models.Count,
                ["groupIds"] = new JArray(models.Select(m => m.GroupId)),
            });
            foreach (ComposedModelFile model in models)
            {
                WriteSummary(dir, model.Stem, ModelSummarizer.Summarize(
                    Path.Combine(dir, model.IfcFile), Path.Combine(dir, model.WexbimFile)));
            }
        }

        private static void WriteSummary(string dir, string name, JToken content) =>
            File.WriteAllText(Path.Combine(dir, name + SummaryComparer.SummarySuffix), ModelSummarizer.ToJsonText(content), new UTF8Encoding(false));

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
