using System;
using IfcComBridge.Cli.Configuration;
using IfcComBridge.Cli.Synthetic;
using Newtonsoft.Json;

namespace IfcComBridge.Cli.Commands
{
    /// <summary>
    /// Validates a tests.local.json and shows what it enables (exit 0 valid, 3 invalid). With --json it
    /// prints the resolved input paths as JSON instead, so scripts never parse the configuration themselves.
    /// </summary>
    internal static class ConfigCommand
    {
        public static int Run(CommandArguments args)
        {
            string path = args.Required("config");
            bool json = args.Flag("json");
            args.EnsureAllConsumed();

            LocalTestConfiguration config = LocalTestConfiguration.Load(path);
            if (json)
            {
                // ASCII only (other characters as \uXXXX), so that console code pages cannot alter paths.
                Console.WriteLine(JsonConvert.SerializeObject(config.ToJson(), new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    StringEscapeHandling = StringEscapeHandling.EscapeNonAscii,
                }));
                return Program.ExitOk;
            }

            foreach (string line in config.Describe())
                Console.WriteLine(line);
            if (!config.HasAnyScenario)
                Console.WriteLine("  note: no scenario configured; every private-data test will be skipped.");
            return Program.ExitOk;
        }
    }

    /// <summary>Writes the synthetic stand-in input set (both IFC models, JSON fixtures, tests.local.json).</summary>
    internal static class SyntheticCommand
    {
        public static int Run(CommandArguments args)
        {
            string outPath = args.Required("out");
            bool force = args.Flag("force");
            args.EnsureAllConsumed();

            // Check before OutputDirectory, which creates the folder.
            RepositoryGuard.AssertOutsideRepository(outPath);
            string outDir = OutputGuard.OutputDirectory(outPath, force, new string[0]);
            string config = SyntheticInputSet.Write(outDir);

            Console.WriteLine($"synthetic: input set written to {outDir}");
            Console.WriteLine($"synthetic: use it with -Config \"{config}\"");
            return Program.ExitOk;
        }
    }
}
