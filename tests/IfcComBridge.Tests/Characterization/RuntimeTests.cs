using System;
using System.IO;
using System.Linq;
using IfcComBridge.Cli.Summary;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xunit;

namespace IfcComBridge.Tests.Characterization
{
    /// <summary>The runtime class exercised through exactly the members a COM client calls.</summary>
    public class RuntimeTests
    {
        [Fact]
        public void DoXBimLibTest_ReturnsTrue_WhenTheEngineIsNextToTheLibrary()
        {
            using (var runtime = new ComRuntime())
                Assert.True(runtime.DoXBimLibTest());
        }

        [Fact]
        public void StopwatchStop_WithoutStart_ReturnsZero()
        {
            using (var runtime = new ComRuntime())
            {
                Assert.Equal(0.0, runtime.StopwatchStop());
                runtime.StopwatchStart();
                Assert.True(runtime.StopwatchStop() >= 0);
            }
        }

        [Fact]
        public void GetModelGroupId_WithoutModels_ThrowsArgumentOutOfRange()
        {
            using (var runtime = new ComRuntime())
                Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetModelGroupId(0));
        }

        [Fact]
        public void LoadIfc_ReplacesModels_AndUsesTheFilePathAsGroupId()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                string path = temp.File("building.ifc");
                SyntheticModels.WriteBuildingModel(path);
                runtime.LoadIfc(path);
                runtime.LoadIfc(path);
                Assert.Equal(path, runtime.GetModelGroupId(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetModelGroupId(1));
            }
        }

        [Fact]
        public void Q11_LoadIfc_Ifc2x3_ThrowsFileNotFound()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                string path = temp.File("ifc2x3.ifc");
                SyntheticModels.WriteIfc2x3Model(path);
                Assert.Throws<FileNotFoundException>(() => runtime.LoadIfc(path));
            }
        }

        [Fact]
        public void SaveIfc_RoundTrip_PreservesTheModelSummary()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                string input = temp.File("building.ifc");
                string output = temp.File("roundtrip.ifc");
                SyntheticModels.WriteBuildingModel(input);
                runtime.LoadIfc(input);
                runtime.SaveIfc(output, 0);
                Assert.Empty(SummaryComparer.Compare(ModelSummarizer.Summarize(input), ModelSummarizer.Summarize(output)));
            }
        }

        [Fact]
        public void Q16_SaveIfc_ReplacesAnExistingFile()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                string input = temp.File("building.ifc");
                string output = temp.File("out.ifc");
                SyntheticModels.WriteBuildingModel(input);
                File.WriteAllText(output, "not an IFC file");
                runtime.LoadIfc(input);
                runtime.SaveIfc(output, 0);
                Assert.StartsWith("ISO-10303-21;", File.ReadAllText(output));
            }
        }

        [Fact]
        public void SaveWexbim_WritesAValidWexbimFile()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                string input = temp.File("building.ifc");
                string output = temp.File("building.wexbim");
                SyntheticModels.WriteBuildingModel(input);
                runtime.LoadIfc(input);
                runtime.SaveWexbim(output, 0);

                JObject header = WexbimHeaderReader.Read(output);
                Assert.True((bool)header["valid"]);
                Assert.Equal(1, (int)header["products"]);   // the slab
                Assert.True((int)header["triangles"] > 0);
            }
        }

        [Fact]
        public void UpdateModel_MovesAComposedProductByEntityLabel()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                var inputs = SyntheticModels.WriteComposeInputs(temp.Directory("inputs"));
                int count = runtime.LoadIfcJson(inputs.Building, inputs.Products, File.ReadAllText(inputs.ProductsMap), inputs.Layout);
                Assert.Equal(2, count);

                // Labels in a saved file are the in-memory labels a client sees (e.g. via the WexBIM viewer).
                string before = temp.File("before.ifc");
                runtime.SaveIfc(before, 0);
                int label = ProductLabel(ModelSummarizer.Summarize(before), "JSON [ID: 1, SET_ID: S1]");

                string transforms = temp.File("transforms.json");
                SyntheticModels.WriteJson(transforms, SyntheticModels.Transforms(
                    SyntheticModels.TransformEntry(label, SyntheticModels.Matrix(100, 200, 300))));
                Assert.True(runtime.UpdateModel(transforms, 0));

                string after = temp.File("after.ifc");
                runtime.SaveIfc(after, 0);
                JToken location = Product(ModelSummarizer.Summarize(after), label)["placement"]["relative"]["location"];
                Assert.Equal(new[] { 601.0, 200.0, 300.0 }, location.Values<double>());
            }
        }

        [Fact]
        public void UpdateModel_ReturnsFalse_WhenNothingApplies()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                string input = temp.File("building.ifc");
                SyntheticModels.WriteBuildingModel(input);
                runtime.LoadIfc(input);
                string transforms = temp.File("transforms.json");
                SyntheticModels.WriteJson(transforms, SyntheticModels.Transforms(SyntheticModels.TransformEntry(null, SyntheticModels.Matrix(1, 1, 1))));
                Assert.False(runtime.UpdateModel(transforms, 0));
            }
        }

        [Fact]
        public void Dispose_ReleasesAllModels()
        {
            using (var temp = new TempDirectory())
            {
                string input = temp.File("building.ifc");
                SyntheticModels.WriteBuildingModel(input);
                var runtime = new ComRuntime();
                runtime.LoadIfc(input);
                runtime.Dispose();
                Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetModelGroupId(0));
            }
        }

        private static JToken Product(JObject summary, int label) =>
            summary["products"].Single(p => (int)p["label"] == label);

        private static int ProductLabel(JObject summary, string description) =>
            (int)summary["products"].Single(p => (string)p["description"] == description)["label"];
    }
}
