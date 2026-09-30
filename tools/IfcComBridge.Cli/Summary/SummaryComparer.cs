using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Cli.Summary
{
    /// <summary>
    /// Structural diff of two summary JSON documents. Output lines are "path: baseline -> current" and
    /// are ordered deterministically so that two runs report differences identically.
    /// </summary>
    public static class SummaryComparer
    {
        public const string SummarySuffix = ".summary.json";

        /// <summary>"a,b" -> {"a","b"}; null or empty -> empty set.</summary>
        public static ISet<string> ParseIgnoreList(string commaSeparated) => new HashSet<string>(
            (commaSeparated ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
            StringComparer.Ordinal);

        /// <param name="ignoredProperties">Property names ignored at any depth (e.g. "sha256").</param>
        public static IReadOnlyList<string> Compare(JToken baseline, JToken current, ISet<string> ignoredProperties = null)
        {
            var differences = new List<string>();
            Walk("$", baseline, current, ignoredProperties ?? new HashSet<string>(), differences);
            return differences;
        }

        /// <summary>Compares every *.summary.json below both roots, matched by relative path.</summary>
        public static IReadOnlyList<string> CompareDirectories(string baselineRoot, string currentRoot, ISet<string> ignoredProperties = null)
        {
            Dictionary<string, string> baseline = SummaryFiles(baselineRoot);
            Dictionary<string, string> current = SummaryFiles(currentRoot);
            var differences = new List<string>();

            foreach (string relative in baseline.Keys.Union(current.Keys).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (!current.ContainsKey(relative))
                {
                    differences.Add($"{relative}: missing in current");
                    continue;
                }
                if (!baseline.ContainsKey(relative))
                {
                    differences.Add($"{relative}: not in baseline");
                    continue;
                }

                IReadOnlyList<string> fileDifferences = Compare(
                    JToken.Parse(File.ReadAllText(baseline[relative])),
                    JToken.Parse(File.ReadAllText(current[relative])),
                    ignoredProperties);
                differences.AddRange(fileDifferences.Select(d => $"{relative}: {d}"));
            }

            return differences;
        }

        private static Dictionary<string, string> SummaryFiles(string root)
        {
            string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(root, "*" + SummarySuffix, SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetFullPath(f).Substring(prefix.Length), f => f, StringComparer.OrdinalIgnoreCase);
        }

        private static void Walk(string path, JToken baseline, JToken current, ISet<string> ignored, List<string> differences)
        {
            if (baseline is JObject baseObject && current is JObject currentObject)
            {
                var names = baseObject.Properties().Select(p => p.Name)
                    .Union(currentObject.Properties().Select(p => p.Name))
                    .Where(n => !ignored.Contains(n))
                    .OrderBy(n => n, StringComparer.Ordinal);
                foreach (string name in names)
                {
                    JToken b = baseObject[name];
                    JToken c = currentObject[name];
                    if (b == null)
                        differences.Add($"{path}.{name}: <absent> -> {Short(c)}");
                    else if (c == null)
                        differences.Add($"{path}.{name}: {Short(b)} -> <absent>");
                    else
                        Walk($"{path}.{name}", b, c, ignored, differences);
                }
                return;
            }

            if (baseline is JArray baseArray && current is JArray currentArray)
            {
                if (baseArray.Count != currentArray.Count)
                    differences.Add($"{path}: array length {baseArray.Count} -> {currentArray.Count}");
                int common = Math.Min(baseArray.Count, currentArray.Count);
                for (int i = 0; i < common; i++)
                    Walk($"{path}[{i}]", baseArray[i], currentArray[i], ignored, differences);
                return;
            }

            if (IsJsonNull(baseline) && IsJsonNull(current))
                return;
            if (!JToken.DeepEquals(baseline, current))
                differences.Add($"{path}: {Short(baseline)} -> {Short(current)}");
        }

        // A null string assigned in code is a String-typed JValue; a parsed null is a Null-typed one.
        // Both serialize to JSON null, which is what the comparison is about.
        private static bool IsJsonNull(JToken token) =>
            token == null || token.Type == JTokenType.Null || (token is JValue value && value.Value == null);

        private static string Short(JToken token)
        {
            string text = token?.ToString(Formatting.None) ?? "<null>";
            return text.Length > 120 ? text.Substring(0, 117) + "..." : text;
        }
    }
}
