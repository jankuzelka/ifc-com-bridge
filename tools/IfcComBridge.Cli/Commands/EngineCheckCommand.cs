using System;
using System.Linq;
using IfcComBridge;

namespace IfcComBridge.Cli.Commands
{
    internal static class EngineCheckCommand
    {
        public static int Run(CommandArguments args)
        {
            string library = args.Optional("library");
            string runtimeType = args.Optional("runtime-type") ?? IsolatedEngineProbe.DefaultRuntimeTypeName;
            args.EnsureAllConsumed();

            if (library != null)
                return IsolatedEngineProbe.RunInIsolatedDomain(OutputGuard.InputFile(library, "library"), runtimeType);

            try
            {
                using (var runtime = new ComRuntime())
                {
                    bool ok = runtime.DoXBimLibTest();
                    Console.WriteLine($"engine-check: DoXBimLibTest() returned {ok}");
                    PrintGeometryAssemblies();
                    return ok ? Program.ExitOk : Program.ExitCheckFailed;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("engine-check: FAILED");
                Program.ReportException(ex, verbose: false);
                PrintGeometryAssemblies();
                return Program.ExitCheckFailed;
            }
        }

        private static void PrintGeometryAssemblies()
        {
            foreach (string line in IsolatedEngineProbe.DescribeGeometryAssemblies(AppDomain.CurrentDomain).OrderBy(s => s))
                Console.WriteLine("  loaded: " + line);
        }
    }
}
