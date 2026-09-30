using System;
using System.Collections.Generic;
using System.Reflection;
using IfcComBridge.Cli.Baseline;
using IfcComBridge.Cli.Commands;
using IfcComBridge.Cli.Configuration;

namespace IfcComBridge.Cli
{
    /// <summary>
    /// Characterization / debug CLI. Every input path comes from the command line; the tool has no
    /// built-in paths, never writes over its inputs and refuses to overwrite outputs without --force.
    /// </summary>
    internal static class Program
    {
        internal const int ExitOk = 0;
        internal const int ExitCheckFailed = 1;
        internal const int ExitError = 2;
        internal const int ExitConfigError = 3;
        internal const int ExitUsage = 64;

        // Options that take no value. Every other --option expects exactly one value.
        private static readonly ISet<string> FlagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "force", "with-wexbim", "include-identity", "verbose", "keep", "json"
        };

        private static int Main(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return args.Length == 0 ? ExitUsage : ExitOk;
            }

            CommandArguments arguments;
            try
            {
                arguments = CommandArguments.Parse(args, FlagNames);
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine("Usage error: " + ex.Message);
                Console.Error.WriteLine("Run 'IfcComBridge.Cli help' for the list of commands.");
                return ExitUsage;
            }

            bool verbose = arguments.Flag("verbose");
            try
            {
                switch (arguments.Command)
                {
                    case "engine-check": return EngineCheckCommand.Run(arguments);
                    case "load-save": return LoadSaveCommand.Run(arguments);
                    case "wexbim": return WexbimCommand.Run(arguments);
                    case "compose": return ComposeCommand.Run(arguments);
                    case "update": return UpdateCommand.Run(arguments);
                    case "summary": return SummaryCommand.Run(arguments);
                    case "compare": return CompareCommand.Run(arguments);
                    case "baseline": return BaselineCommand.Run(arguments);
                    case "baseline-compare": return BaselineCompareCommand.Run(arguments);
                    case "config": return ConfigCommand.Run(arguments);
                    case "synthetic": return SyntheticCommand.Run(arguments);
                    default: throw new UsageException($"Unknown command '{arguments.Command}'.");
                }
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine("Usage error: " + ex.Message);
                return ExitUsage;
            }
            catch (ConfigurationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitConfigError;
            }
            catch (Exception ex)
            {
                ReportException(ex, verbose);
                return ExitError;
            }
        }

        internal static void ReportException(Exception ex, bool verbose)
        {
            Exception inner = Unwrap(ex);
            Console.Error.WriteLine($"ERROR: {inner.GetType().FullName}: {inner.Message}");
            if (verbose)
                Console.Error.WriteLine(ex);
        }

        internal static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }

        private static bool IsHelp(string arg) =>
            arg is "help" or "--help" or "-h" or "/?";

        private static void PrintUsage()
        {
            Console.WriteLine(@"IfcComBridge.Cli - characterization harness for the IfcComBridge COM library (net472, x64)

Commands (all paths are explicit arguments; nothing is read from or written to built-in locations):

  engine-check [--library <dir\IfcComBridge.dll>] [--runtime-type <type>]
      Construct the geometry engine via DoXBimLibTest(). With --library the given DLL is loaded
      into an isolated AppDomain (LoadFrom, no config file), which mimics an in-process COM host
      without registering anything.

  load-save --ifc <in.ifc> --out <out.ifc|ifczip|ifcxml> [--force]
      LoadIfc + SaveIfc through the runtime class (the COM API surface).

  wexbim --ifc <in.ifc> --out <out.wexbim> [--force]
      LoadIfc + SaveWexbim through the runtime class.

  compose --building <ifc> --products <ifc> --products-map <json> --layout <json> --out <dir>
          [--api runtime|utils] [--missing-log <file>] [--with-wexbim] [--force]
      Compose one model per layout group and save each as <dir>\NNN_<group>.ifc (+ .wexbim).
      --api runtime (default) uses LoadIfcJson of the runtime class exactly as a COM client does.
      --api utils calls the library's internal static helper directly; only this path supports
      --missing-log.

  update --ifc <in.ifc> --transforms <json> --out <out.ifc> [--wexbim-out <out.wexbim>] [--force]
      LoadIfc + UpdateModel + SaveIfc (+ SaveWexbim).

  summary --ifc <file> [--wexbim <file>] --out <summary.json|-> [--include-identity] [--force]
      Deterministic JSON summary for regression comparison (no timestamps, no generated GUIDs,
      no file paths). --include-identity adds IFC header/application/organization names, which
      can contain private values; leave it off for anything that may be shared.

  compare --baseline <file|dir> --current <file|dir> [--ignore <prop1,prop2>] [--max-diffs <n>]
      Compare summary files (directories: all *.summary.json by relative path).
      Exit code 0 = identical, 1 = differences.

  baseline --out <dir> (--config <tests.local.json> | [--ifc <ifc>] [--transforms <json>]
           [--building <ifc> --products <ifc> --products-map <json> --layout <json>]) [--force]
      Run every characterization step (engine, load/save + WexBIM, compose via both APIs, update)
      and write *.summary.json files plus manifest.json (input file names and SHA-256 only).
      Refuses any folder inside the repository.

  baseline-compare --config <tests.local.json> --baseline <dir> [--ignore <a,b>] [--keep]
      Check that the configured inputs are the baseline's (roles + SHA-256), re-run every step
      into a temporary folder and compare. Exit 0 identical, 1 differences, 3 input mismatch.

  config --config <tests.local.json> [--json]
      Validate a local test configuration and show which scenarios it enables; with --json, print
      the resolved input paths as JSON (used by scripts\com-smoke-test.ps1).

  synthetic --out <dir> [--force]
      Write the synthetic stand-in input set: building.ifc, products.ifc, the JSON fixtures,
      edge cases and a tests.local.json (relative paths) usable with -Config.

Global flags: --verbose (full exception details)
Exit codes: 0 ok, 1 check failed / differences, 2 error, 3 configuration error, 64 usage error");
        }
    }
}
