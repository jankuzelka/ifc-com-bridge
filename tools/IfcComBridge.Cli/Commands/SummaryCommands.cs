using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IfcComBridge.Cli.Summary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Cli.Commands
{
    internal static class SummaryCommand
    {
        public static int Run(CommandArguments args)
        {
            string ifc = OutputGuard.InputFile(args.Required("ifc"), "ifc");
            string wexbimArg = args.Optional("wexbim");
            string outPath = args.Required("out");
            bool includeIdentity = args.Flag("include-identity");
            bool force = args.Flag("force");
            args.EnsureAllConsumed();

            string wexbim = wexbimArg == null ? null : OutputGuard.InputFile(wexbimArg, "wexbim");
            JObject summary = ModelSummarizer.Summarize(ifc, wexbim, includeIdentity);
            string text = ModelSummarizer.ToJsonText(summary);

            if (outPath == "-")
            {
                Console.Write(text);
                return Program.ExitOk;
            }

            var inputs = wexbim == null ? new[] { ifc } : new[] { ifc, wexbim };
            string output = OutputGuard.OutputFile(outPath, force, inputs);
            File.WriteAllText(output, text, new UTF8Encoding(false));
            Console.WriteLine($"summary: wrote {output}");
            return Program.ExitOk;
        }
    }

    internal static class CompareCommand
    {
        public static int Run(CommandArguments args)
        {
            string baseline = Path.GetFullPath(args.Required("baseline"));
            string current = Path.GetFullPath(args.Required("current"));
            string ignore = args.Optional("ignore");
            int maxDiffs = args.OptionalInt("max-diffs", 200);
            args.EnsureAllConsumed();

            ISet<string> ignored = SummaryComparer.ParseIgnoreList(ignore);

            IReadOnlyList<string> differences;
            if (Directory.Exists(baseline) && Directory.Exists(current))
                differences = SummaryComparer.CompareDirectories(baseline, current, ignored);
            else if (File.Exists(baseline) && File.Exists(current))
                differences = SummaryComparer.Compare(
                    JToken.Parse(File.ReadAllText(baseline)), JToken.Parse(File.ReadAllText(current)), ignored);
            else
                throw new UsageException("--baseline and --current must both be existing files or both be existing directories.");

            if (differences.Count == 0)
            {
                Console.WriteLine("compare: identical");
                return Program.ExitOk;
            }

            Console.WriteLine($"compare: {differences.Count} difference(s)");
            foreach (string line in differences.Take(maxDiffs))
                Console.WriteLine("  " + line);
            if (differences.Count > maxDiffs)
                Console.WriteLine($"  ... {differences.Count - maxDiffs} more (use --max-diffs)");
            return Program.ExitCheckFailed;
        }
    }
}
