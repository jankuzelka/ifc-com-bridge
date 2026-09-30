using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IfcComBridge.Cli.Baseline;
using IfcComBridge.Cli.Configuration;
using IfcComBridge.Cli.Summary;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace IfcComBridge.Tests.Characterization
{
    /// <summary>
    /// The committed JSON fixtures (tests/fixtures/synthetic) must only reference what exists in the
    /// synthetic IFC models, and must behave as documented when fed to the unmodified library.
    /// </summary>
    public class SyntheticFixtureTests
    {
        private const int BeamALabel = 77;
        private const int Cat1200Label = 151;
        private const int ProjectLabel = 9;

        [Fact]
        public void EveryFixtureIsEmbedded_AndHasTheShapeTheLibraryRequires()
        {
            Assert.IsType<JObject>(SyntheticFixtures.Read(SyntheticFixtures.ProductsMapFile));
            Assert.IsType<JArray>(SyntheticFixtures.Read(SyntheticFixtures.LayoutFile));
            Assert.IsType<JArray>(SyntheticFixtures.Read(SyntheticFixtures.TransformsFile)["modified_products"]);
            foreach (string file in SyntheticFixtures.EdgeCaseFiles)
                Assert.NotNull(SyntheticFixtures.Read(file));
        }

        [Fact]
        public void WriteComposeInputs_CopiesTheCommittedFixturesVerbatim()
        {
            using (var temp = new TempDirectory())
            {
                var inputs = SyntheticModels.WriteComposeInputs(temp.Path);
                Assert.Equal(SyntheticFixtures.ReadText(SyntheticFixtures.ProductsMapFile), File.ReadAllText(inputs.ProductsMap));
                Assert.Equal(SyntheticFixtures.ReadText(SyntheticFixtures.LayoutFile), File.ReadAllText(inputs.Layout));
            }
        }

        [Fact]
        public void ProductsMapReferences_ExistInTheSyntheticProductsModel_ExceptTheDeliberateMisses()
        {
            using (var temp = new TempDirectory())
            {
                string products = temp.File("products.ifc");
                SyntheticModels.WriteProductsModel(products);

                // The library's candidates: every IfcBeam and IfcWallStandardCase of the products model.
                var guids = new HashSet<string>();
                var names = new HashSet<string>();
                using (IfcStore model = IfcStore.Open(products, null, -1))
                {
                    foreach (IIfcProduct p in model.Instances.OfType<IIfcBeam>().Cast<IIfcProduct>().Concat(model.Instances.OfType<IIfcWallStandardCase>()))
                    {
                        guids.Add(p.GlobalId.ToString());
                        names.Add(p.Name.ToString());
                    }
                }

                var resolution = new Dictionary<string, string>();
                foreach (JProperty entry in ((JObject)SyntheticFixtures.Read(SyntheticFixtures.ProductsMapFile)).Properties())
                {
                    IEnumerable<JObject> parameters = entry.Value is JArray catalogue ? catalogue.Cast<JObject>() : new[] { (JObject)entry.Value };
                    foreach (JObject p in parameters)
                    {
                        string how = guids.Contains((string)p["ifc_guid"]) ? "guid"
                            : names.Contains((string)p["ifc_name"]) ? "name"
                            : "none";
                        resolution[$"{entry.Name}/{p["ifc_name"]}"] = how;
                    }
                }

                Assert.Equal(new Dictionary<string, string>
                {
                    ["BEAM&A/BEAM_A"] = "guid",
                    ["CAT/CAT_1000"] = "guid",
                    ["CAT/CAT_1200"] = "guid",
                    ["CAT/CAT_1500"] = "guid",
                    ["CAT-11/BEAM_A"] = "guid",
                    ["EXT/EXTRUDED_B"] = "name",            // unknown GlobalId: resolved through the ifc_name fallback
                    ["WALL/WALL_W"] = "guid",
                    ["GHOST/NO_SUCH_PRODUCT"] = "none",     // deliberately unresolvable
                    ["SHORT/CAT_1000"] = "guid",
                }, resolution);
            }
        }

        [Fact]
        public void TransformIds_AreEntityLabelsOfTheSyntheticProductsModel()
        {
            using (var temp = new TempDirectory())
            {
                string products = temp.File("products.ifc");
                SyntheticModels.WriteProductsModel(products);
                using (IfcStore model = IfcStore.Open(products, null, -1))
                {
                    Assert.Equal("BEAM_A", ((IIfcProduct)model.Instances[BeamALabel]).Name.ToString());
                    Assert.Equal("CAT_1200", ((IIfcProduct)model.Instances[Cat1200Label]).Name.ToString());
                    Assert.IsAssignableFrom<IIfcProject>(model.Instances[ProjectLabel]);
                }

                int[] ids = SyntheticFixtures.Read(SyntheticFixtures.TransformsFile)["modified_products"].Select(e => (int)e["id"]).ToArray();
                Assert.Equal(new[] { BeamALabel, Cat1200Label }, ids);
            }
        }

        private static JObject Update(string transformsFixture, out bool changed)
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                string products = temp.File("products.ifc");
                string transforms = temp.File("transforms.json");
                string output = temp.File("updated.ifc");
                SyntheticModels.WriteProductsModel(products);
                SyntheticFixtures.CopyTo(transformsFixture, transforms);

                runtime.LoadIfc(products);
                changed = runtime.UpdateModel(transforms, 0);
                runtime.SaveIfc(output, 0);
                return ModelSummarizer.Summarize(output);
            }
        }

        private static JToken Relative(JObject summary, int label) =>
            summary["products"].Single(p => (int)p["label"] == label)["placement"]["relative"];

        [Fact]
        public void Transforms_MoveTheNamedProducts_ThroughTheRuntime()
        {
            JObject summary = Update(SyntheticFixtures.TransformsFile, out bool changed);
            Assert.True(changed);

            JToken beam = Relative(summary, BeamALabel);                    // pure translation
            Assert.Equal(new[] { 100.0, 200.0, 0.0 }, beam["location"].Values<double>());
            Assert.Equal(new[] { 0.0, 0.0, 1.0 }, beam["axis"].Values<double>());
            Assert.Equal(new[] { 1.0, 0.0, 0.0 }, beam["refDirection"].Values<double>());

            JToken cat = Relative(summary, Cat1200Label);                   // +90 degrees about Z, then +1000 in Y
            Assert.Equal(new[] { 0.0, 1000.0, 0.0 }, cat["location"].Values<double>());
            Assert.Equal(new[] { 0.0, 0.0, 1.0 }, cat["axis"].Values<double>());
            Assert.Equal(new[] { 0.0, 1.0, 0.0 }, cat["refDirection"].Values<double>());
        }

        [Fact]
        public void EdgeCase_Q17_PartialMatrix_CollapsesTheDirections()
        {
            JObject summary = Update(SyntheticFixtures.PartialMatrixTransformsFile, out bool changed);
            Assert.True(changed);
            JToken beam = Relative(summary, BeamALabel);
            Assert.Equal(new[] { 10.0, 20.0, 30.0 }, beam["location"].Values<double>());
            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, beam["axis"].Values<double>());
            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, beam["refDirection"].Values<double>());
        }

        [Fact]
        public void EdgeCase_SkippedEntries_ChangeNothing()
        {
            JObject summary = Update(SyntheticFixtures.SkippedEntriesTransformsFile, out bool changed);
            Assert.False(changed);
            using (var temp = new TempDirectory())
            {
                string products = temp.File("products.ifc");
                SyntheticModels.WriteProductsModel(products);
                Assert.Empty(SummaryComparer.Compare(ModelSummarizer.Summarize(products), summary));
            }
        }

        [Fact]
        public void EdgeCase_Q5_MissingModifiedProducts_ThrowsNullReference()
        {
            Assert.Throws<NullReferenceException>(() => Update(SyntheticFixtures.MissingModifiedProductsTransformsFile, out _));
        }

        [Fact]
        public void SyntheticInputSet_IsAValidConfiguration_AndEveryBaselineStepSucceeds()
        {
            using (var temp = new TempDirectory())
            {
                string configPath = SyntheticInputSet.Write(temp.Directory("set"));
                LocalTestConfiguration config = LocalTestConfiguration.Load(configPath);
                Assert.NotNull(config.Model.Transforms);
                Assert.NotNull(config.Composition);

                JObject manifest = BaselineRunner.Run(config.ToBaselineInputs(), temp.Directory("baseline"), null);
                Assert.All(manifest["steps"], step => Assert.True((bool)step["ok"], (string)step["step"]));

                JObject update = JObject.Parse(File.ReadAllText(Path.Combine(temp.Path, "baseline", "update", "result.summary.json")));
                Assert.True((bool)update["updateModelReturned"]);
                JObject compose = JObject.Parse(File.ReadAllText(Path.Combine(temp.Path, "baseline", "compose", "runtime", "result.summary.json")));
                Assert.Equal(new[] { "G1", "G3" }, compose["groupIds"].Values<string>());
            }
        }
    }
}
