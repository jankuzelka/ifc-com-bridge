using System.Collections.Generic;
using System.IO;
using System.Linq;
using IfcComBridge.Cli.Summary;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Cli.Baseline
{
    public sealed class BaselineComparisonResult
    {
        /// <summary>Why the configured inputs cannot be compared with this baseline (nothing was run).</summary>
        public IReadOnlyList<string> InputProblems { get; set; } = new string[0];
        public IReadOnlyList<string> Differences { get; set; } = new string[0];
        public bool Identical => InputProblems.Count == 0 && Differences.Count == 0;
    }

    /// <summary>
    /// Re-runs every characterization step and compares the summaries with a captured baseline. Used by
    /// the 'baseline-compare' command and by the integration test, so both behave identically.
    /// </summary>
    public static class BaselineComparison
    {
        /// <summary>
        /// A comparison is only meaningful for the very same inputs: the configured roles must equal the
        /// baseline's and every file must be byte-identical (SHA-256 recorded in manifest.json).
        /// </summary>
        public static IReadOnlyList<string> CheckInputs(string baselineDirectory, BaselineInputs inputs)
        {
            var problems = new List<string>();
            string manifestPath = Path.Combine(baselineDirectory, BaselineRunner.ManifestFile);
            if (!File.Exists(manifestPath))
            {
                problems.Add($"not a baseline folder (no {BaselineRunner.ManifestFile}): {baselineDirectory}");
                return problems;
            }

            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
            if ((int?)manifest["summaryFormat"] != ModelSummarizer.FormatVersion)
                problems.Add($"the baseline uses summary format {manifest["summaryFormat"]}, this harness writes {ModelSummarizer.FormatVersion}; capture a new baseline");

            Dictionary<string, string> recorded = (manifest["inputs"] as JArray ?? new JArray())
                .OfType<JObject>()
                .ToDictionary(i => (string)i["role"], i => (string)i["sha256"]);
            Dictionary<string, string> configured = inputs.All().ToDictionary(i => i.Key, i => i.Value);

            foreach (string role in recorded.Keys.Except(configured.Keys).OrderBy(r => r))
                problems.Add($"the baseline was captured with input '{role}', which is not configured now");
            foreach (string role in configured.Keys.Except(recorded.Keys).OrderBy(r => r))
                problems.Add($"input '{role}' is configured but was not part of the baseline");
            foreach (string role in configured.Keys.Intersect(recorded.Keys).OrderBy(r => r))
            {
                if (BaselineRunner.Sha256(configured[role]) != recorded[role])
                    problems.Add($"input '{role}' is not the file the baseline was captured from (SHA-256 differs)");
            }

            return problems;
        }

        public static BaselineComparisonResult Run(BaselineInputs inputs, string baselineDirectory, string currentDirectory,
            ISet<string> ignoredProperties, System.Action<string> log)
        {
            IReadOnlyList<string> inputProblems = CheckInputs(baselineDirectory, inputs);
            if (inputProblems.Count > 0)
                return new BaselineComparisonResult { InputProblems = inputProblems };

            BaselineRunner.Run(inputs, currentDirectory, log);
            return new BaselineComparisonResult
            {
                Differences = SummaryComparer.CompareDirectories(baselineDirectory, currentDirectory, ignoredProperties),
            };
        }
    }
}
