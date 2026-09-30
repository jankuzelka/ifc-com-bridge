using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xbim.Ifc4.Interfaces;
using IfcComBridge.Composition;
using IfcComBridge.IO;

namespace IfcComBridge.Tests.Characterization
{
    /// <summary>
    /// Composes the synthetic inputs once (static helper path, with missing-items log) and keeps the
    /// resulting in-memory models open for inspection; each model is also saved for summary checks.
    /// </summary>
    public sealed class ComposeFixture : IDisposable
    {
        private readonly TempDirectory _temp = new TempDirectory();

        public ComposeFixture()
        {
            Inputs = SyntheticModels.WriteComposeInputs(_temp.Directory("inputs"));
            MissingLog = _temp.File("missing-items.log");
            OutputDirectory = _temp.Directory("out");

            var map = (JObject)ModelFiles.LoadJson(Inputs.ProductsMap, "JSON Data");
            Models = ProductLayoutComposer.LoadIfcJson(Inputs.Building, Inputs.Products, map, Inputs.Layout, MissingLog).ToList();
            foreach (ComposedModel model in Models)
            {
                string file = Path.Combine(OutputDirectory, model.GroupId + ".ifc");
                ModelFiles.SaveIfcModel(file, model.IfcStore);
                SavedFiles[model.GroupId] = file;
            }
        }

        public SyntheticModels.ComposeInputs Inputs { get; }
        public string MissingLog { get; }
        public string OutputDirectory { get; }
        internal List<ComposedModel> Models { get; }
        public Dictionary<string, string> SavedFiles { get; } = new Dictionary<string, string>();

        internal ComposedModel Group(string groupId) => Models.Single(m => m.GroupId == groupId);

        /// <summary>Products inserted from the layout (the library marks them via Description), in insertion order.</summary>
        public List<IIfcProduct> Copies(string groupId) => Group(groupId).IfcStore.Instances.OfType<IIfcProduct>()
            .Where(p => Text(p.Description)?.StartsWith("JSON [", StringComparison.Ordinal) == true)
            .OrderBy(p => p.EntityLabel)
            .ToList();

        public static string Text<T>(T? value) where T : struct => value.HasValue ? value.Value.ToString() : null;

        public void Dispose()
        {
            foreach (ComposedModel model in Models)
                model.Dispose();
            _temp.Dispose();
        }
    }
}
