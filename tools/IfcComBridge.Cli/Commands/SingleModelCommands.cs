using System;
using IfcComBridge;

namespace IfcComBridge.Cli.Commands
{
    // These commands go through the runtime class, i.e. the exact members a COM client calls.

    internal static class LoadSaveCommand
    {
        public static int Run(CommandArguments args)
        {
            string input = OutputGuard.InputFile(args.Required("ifc"), "ifc");
            string outPath = args.Required("out");
            bool force = args.Flag("force");
            args.EnsureAllConsumed();
            string output = OutputGuard.OutputFile(outPath, force, new[] { input });

            using (var runtime = new ComRuntime())
            {
                runtime.LoadIfc(input);
                runtime.SaveIfc(output, 0);
            }

            Console.WriteLine($"load-save: wrote {output}");
            return Program.ExitOk;
        }
    }

    internal static class WexbimCommand
    {
        public static int Run(CommandArguments args)
        {
            string input = OutputGuard.InputFile(args.Required("ifc"), "ifc");
            string outPath = args.Required("out");
            bool force = args.Flag("force");
            args.EnsureAllConsumed();
            string output = OutputGuard.OutputFile(outPath, force, new[] { input });

            using (var runtime = new ComRuntime())
            {
                runtime.LoadIfc(input);
                runtime.SaveWexbim(output, 0);
            }

            Console.WriteLine($"wexbim: wrote {output}");
            return Program.ExitOk;
        }
    }

    internal static class UpdateCommand
    {
        public static int Run(CommandArguments args)
        {
            string input = OutputGuard.InputFile(args.Required("ifc"), "ifc");
            string transforms = OutputGuard.InputFile(args.Required("transforms"), "transforms");
            string outPath = args.Required("out");
            string wexbimOutPath = args.Optional("wexbim-out");
            bool force = args.Flag("force");
            args.EnsureAllConsumed();

            var inputs = new[] { input, transforms };
            string output = OutputGuard.OutputFile(outPath, force, inputs);
            string wexbimOutput = wexbimOutPath == null ? null : OutputGuard.OutputFile(wexbimOutPath, force, inputs);
            if (wexbimOutput != null && string.Equals(wexbimOutput, output, StringComparison.OrdinalIgnoreCase))
                throw new UsageException("--out and --wexbim-out must differ.");

            bool changed;
            using (var runtime = new ComRuntime())
            {
                runtime.LoadIfc(input);
                changed = runtime.UpdateModel(transforms, 0);
                runtime.SaveIfc(output, 0);
                if (wexbimOutput != null)
                    runtime.SaveWexbim(wexbimOutput, 0);
            }

            Console.WriteLine($"update: UpdateModel returned {changed}");
            Console.WriteLine($"update: wrote {output}");
            if (wexbimOutput != null)
                Console.WriteLine($"update: wrote {wexbimOutput}");
            return Program.ExitOk;
        }
    }
}
