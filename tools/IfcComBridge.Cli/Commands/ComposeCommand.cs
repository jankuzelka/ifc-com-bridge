using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using IfcComBridge.Composition;
using IfcComBridge.IO;

namespace IfcComBridge.Cli.Commands
{
    internal static class ComposeCommand
    {
        public static int Run(CommandArguments args)
        {
            string building = OutputGuard.InputFile(args.Required("building"), "building");
            string products = OutputGuard.InputFile(args.Required("products"), "products");
            string productsMap = OutputGuard.InputFile(args.Required("products-map"), "products-map");
            string layout = OutputGuard.InputFile(args.Required("layout"), "layout");
            string outPath = args.Required("out");
            string api = (args.Optional("api") ?? "runtime").ToLowerInvariant();
            string missingLogPath = args.Optional("missing-log");
            bool withWexbim = args.Flag("with-wexbim");
            bool force = args.Flag("force");
            args.EnsureAllConsumed();

            if (api != "runtime" && api != "utils")
                throw new UsageException("--api must be 'runtime' or 'utils'.");
            if (api == "runtime" && missingLogPath != null)
                throw new UsageException("--missing-log needs --api utils: the COM method LoadIfcJson has no such parameter.");

            var inputs = new[] { building, products, productsMap, layout };
            string outDir = OutputGuard.OutputDirectory(outPath, force, inputs);
            string missingLog = missingLogPath == null ? null : OutputGuard.OutputFile(missingLogPath, force, inputs);

            IReadOnlyList<ComposedModelFile> written = api == "runtime"
                ? ComposeRunner.ViaRuntime(building, products, productsMap, layout, outDir, withWexbim)
                : ComposeRunner.ViaUtils(building, products, productsMap, layout, outDir, withWexbim, missingLog);

            var manifest = new JObject
            {
                ["api"] = api,
                ["modelCount"] = written.Count,
                ["models"] = new JArray(written.Select(m => m.ToJson())),
            };
            File.WriteAllText(Path.Combine(outDir, "compose.json"), manifest.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));

            Console.WriteLine($"compose ({api}): {written.Count} model(s) written to {outDir}");
            foreach (ComposedModelFile m in written)
                Console.WriteLine($"  [{m.Index}] {m.IfcFile}{(m.WexbimFile != null ? " + " + m.WexbimFile : string.Empty)}");
            return Program.ExitOk;
        }
    }

    public sealed class ComposedModelFile
    {
        public ComposedModelFile(int index, string groupId, bool withWexbim)
        {
            Index = index;
            GroupId = groupId;
            Stem = ComposeRunner.ModelFileStem(index, groupId);
            IfcFile = Stem + ".ifc";
            WexbimFile = withWexbim ? Stem + ".wexbim" : null;
        }

        public int Index { get; }
        public string GroupId { get; }
        public string Stem { get; }
        public string IfcFile { get; }
        public string WexbimFile { get; }

        public JObject ToJson() => new JObject
        {
            ["index"] = Index,
            ["groupId"] = GroupId,
            ["ifc"] = IfcFile,
            ["wexbim"] = WexbimFile,
        };
    }

    /// <summary>The two ways the current code base composes models from a JSON layout.</summary>
    public static class ComposeRunner
    {
        /// <summary>
        /// The COM client path: LoadIfcJson on the runtime class, with the products map passed as JSON
        /// text and everything else as file paths.
        /// </summary>
        public static IReadOnlyList<ComposedModelFile> ViaRuntime(string building, string products, string productsMap,
            string layout, string outDir, bool withWexbim)
        {
            var written = new List<ComposedModelFile>();
            using (var runtime = new ComRuntime())
            {
                int count = runtime.LoadIfcJson(building, products, File.ReadAllText(productsMap), layout);
                for (int i = 0; i < count; i++)
                {
                    var file = new ComposedModelFile(i, runtime.GetModelGroupId(i), withWexbim);
                    runtime.SaveIfc(Path.Combine(outDir, file.IfcFile), i);
                    if (withWexbim)
                        runtime.SaveWexbim(Path.Combine(outDir, file.WexbimFile), i);
                    written.Add(file);
                }
            }
            return written;
        }

        /// <summary>
        /// The internal path: the static helper, the products map parsed by the library's
        /// LoadJson, and the optional missing-items log that COM clients cannot reach.
        /// </summary>
        public static IReadOnlyList<ComposedModelFile> ViaUtils(string building, string products, string productsMap,
            string layout, string outDir, bool withWexbim, string missingLog)
        {
            // The library appends to the log; always start from an empty file.
            if (missingLog != null && File.Exists(missingLog))
                File.Delete(missingLog);

            var map = (JObject)ModelFiles.LoadJson(productsMap, "JSON Data");
            var models = new List<ComposedModel>();
            var written = new List<ComposedModelFile>();
            try
            {
                models.AddRange(ProductLayoutComposer.LoadIfcJson(building, products, map, layout, missingLog));
                for (int i = 0; i < models.Count; i++)
                {
                    var file = new ComposedModelFile(i, models[i].GroupId, withWexbim);
                    ModelFiles.SaveIfcModel(Path.Combine(outDir, file.IfcFile), models[i].IfcStore);
                    if (withWexbim)
                        ModelFiles.SaveIfcWexbim(Path.Combine(outDir, file.WexbimFile), models[i].IfcStore);
                    written.Add(file);
                }
            }
            finally
            {
                foreach (ComposedModel model in models)
                    model.Dispose();
            }
            return written;
        }

        /// <summary>
        /// One file per model: "NNN_group". The index prefix keeps every group's file separate, even
        /// when two group ids map to the same name.
        /// </summary>
        public static string ModelFileStem(int index, string groupId)
        {
            var safe = new StringBuilder();
            foreach (char c in groupId ?? string.Empty)
                safe.Append(Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) ? '_' : c);
            string text = safe.Length == 0 ? "group" : safe.ToString();
            if (text.Length > 60)
                text = text.Substring(0, 60);
            return $"{index:D3}_{text}";
        }
    }
}
