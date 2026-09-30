using System;
using System.IO;
using System.Linq;
using IfcComBridge.Cli.Configuration;
using IfcComBridge.Cli.Summary;

namespace IfcComBridge.Cli.Baseline
{
    internal static class BaselineCommand
    {
        public static int Run(CommandArguments args)
        {
            string configPath = args.Optional("config");
            var explicitInputs = new BaselineInputs
            {
                Ifc = OptionalInput(args, "ifc"),
                Building = OptionalInput(args, "building"),
                Products = OptionalInput(args, "products"),
                ProductsMap = OptionalInput(args, "products-map"),
                Layout = OptionalInput(args, "layout"),
                Transforms = OptionalInput(args, "transforms"),
            };
            string outPath = args.Required("out");
            bool force = args.Flag("force");
            args.EnsureAllConsumed();

            BaselineInputs inputs = configPath != null
                ? InputsFromConfig(configPath, explicitInputs)
                : CheckExplicit(explicitInputs);

            // Check before OutputDirectory, which creates the folder.
            RepositoryGuard.AssertOutsideRepository(outPath);
            string outDir = OutputGuard.OutputDirectory(outPath, force, inputs.All().Select(i => i.Value));

            BaselineRunner.Run(inputs, outDir, Console.WriteLine);
            Console.WriteLine($"baseline: written to {outDir}");
            return Program.ExitOk;
        }

        private static BaselineInputs InputsFromConfig(string configPath, BaselineInputs explicitInputs)
        {
            if (explicitInputs.All().Any())
                throw new UsageException("Use either --config or explicit input options (--ifc, --building, ...), not both.");
            LocalTestConfiguration config = LocalTestConfiguration.Load(configPath);
            if (!config.HasAnyScenario)
                throw new UsageException($"{config.ConfigPath} configures no scenario; nothing to capture.");
            return config.ToBaselineInputs();
        }

        private static BaselineInputs CheckExplicit(BaselineInputs inputs)
        {
            int composeInputs = new[] { inputs.Building, inputs.Products, inputs.ProductsMap, inputs.Layout }.Count(p => p != null);
            if (composeInputs != 0 && composeInputs != 4)
                throw new UsageException("Compose needs all of --building, --products, --products-map and --layout.");
            if (inputs.Transforms != null && inputs.Ifc == null)
                throw new UsageException("--transforms needs --ifc (the model the transforms apply to).");
            return inputs;
        }

        private static string OptionalInput(CommandArguments args, string name)
        {
            string value = args.Optional(name);
            return value == null ? null : OutputGuard.InputFile(value, name);
        }
    }

    internal static class BaselineCompareCommand
    {
        public static int Run(CommandArguments args)
        {
            string configPath = args.Required("config");
            string baselinePath = args.Required("baseline");
            string ignore = args.Optional("ignore");
            int maxDiffs = args.OptionalInt("max-diffs", 200);
            bool keep = args.Flag("keep");
            args.EnsureAllConsumed();

            LocalTestConfiguration config = LocalTestConfiguration.Load(configPath);
            if (!config.HasAnyScenario)
                throw new UsageException($"{config.ConfigPath} configures no scenario; nothing to compare.");

            string baseline = Path.GetFullPath(baselinePath);
            if (!Directory.Exists(baseline))
                throw new UsageException($"Baseline folder not found: {baseline}");
            RepositoryGuard.AssertOutsideRepository(baseline);

            string current = Path.Combine(Path.GetTempPath(), "IfcComBridge.Compare", Guid.NewGuid().ToString("N"));
            RepositoryGuard.AssertOutsideRepository(current);
            Directory.CreateDirectory(current);

            BaselineComparisonResult result = BaselineComparison.Run(
                config.ToBaselineInputs(), baseline, current, SummaryComparer.ParseIgnoreList(ignore), Console.WriteLine);

            if (result.InputProblems.Count > 0)
            {
                Console.WriteLine("baseline-compare: the configured inputs cannot be compared with this baseline:");
                foreach (string problem in result.InputProblems)
                    Console.WriteLine("  - " + problem);
                Directory.Delete(current, recursive: true);
                return Program.ExitConfigError;
            }

            if (result.Differences.Count == 0)
            {
                Console.WriteLine("baseline-compare: identical");
                if (keep)
                    Console.WriteLine($"baseline-compare: current run kept in {current}");
                else
                    Directory.Delete(current, recursive: true);
                return Program.ExitOk;
            }

            Console.WriteLine($"baseline-compare: {result.Differences.Count} difference(s)");
            foreach (string line in result.Differences.Take(maxDiffs))
                Console.WriteLine("  " + line);
            if (result.Differences.Count > maxDiffs)
                Console.WriteLine($"  ... {result.Differences.Count - maxDiffs} more (use --max-diffs)");
            Console.WriteLine($"baseline-compare: current run kept for inspection in {current}");
            return Program.ExitCheckFailed;
        }
    }
}
