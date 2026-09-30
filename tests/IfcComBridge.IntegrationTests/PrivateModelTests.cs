using System.Collections.Generic;
using System.IO;
using System.Linq;
using IfcComBridge.Cli.Baseline;
using IfcComBridge.Cli.Commands;
using IfcComBridge.Cli.Configuration;
using IfcComBridge.Cli.Summary;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xunit;
using Xunit.Abstractions;

namespace IfcComBridge.IntegrationTests
{
    /// <summary>
    /// Characterization against PRIVATE local models configured in tests.local.json. Outputs go to a
    /// temporary folder under %TEMP% that is deleted afterwards; nothing is written next to the inputs
    /// or into the repository.
    /// </summary>
    public class PrivateModelTests
    {
        private readonly ITestOutputHelper _output;

        public PrivateModelTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [LocalScenarioFact(LocalScenario.Model)]
        public void LoadSaveAndWexbim_Succeed_AndSavingPreservesTheModel()
        {
            string input = LocalData.Config.Model.Ifc;
            using (var temp = new TempFolder())
            using (var runtime = new ComRuntime())
            {
                Assert.True(runtime.DoXBimLibTest());
                runtime.LoadIfc(input);
                string ifc = temp.File("roundtrip.ifc");
                string wexbim = temp.File("model.wexbim");
                runtime.SaveIfc(ifc, 0);
                runtime.SaveWexbim(wexbim, 0);

                JObject before = ModelSummarizer.Summarize(input);
                JObject after = ModelSummarizer.Summarize(ifc, wexbim);
                Assert.True((bool)after["wexbim"]["valid"]);

                IReadOnlyList<string> differences = SummaryComparer.Compare(before, after, new HashSet<string> { "wexbim" });
                foreach (string line in differences.Take(50))
                    _output.WriteLine(line);
                Assert.Empty(differences);
            }
        }

        [LocalScenarioFact(LocalScenario.Model)]
        public void Summary_IsDeterministic()
        {
            string input = LocalData.Config.Model.Ifc;
            Assert.Empty(SummaryComparer.Compare(ModelSummarizer.Summarize(input), ModelSummarizer.Summarize(input)));
        }

        [LocalScenarioFact(LocalScenario.ModelWithTransforms)]
        public void UpdateModel_Runs_AndTheResultCanBeSaved()
        {
            ModelScenario model = LocalData.Config.Model;
            using (var temp = new TempFolder())
            using (var runtime = new ComRuntime())
            {
                runtime.LoadIfc(model.Ifc);
                bool changed = runtime.UpdateModel(model.Transforms, 0);
                _output.WriteLine($"UpdateModel returned {changed}");
                string output = temp.File("updated.ifc");
                runtime.SaveIfc(output, 0);
                Assert.True(File.Exists(output));
            }
        }

        [LocalScenarioFact(LocalScenario.Composition)]
        public void Compose_RuntimeAndStaticHelper_ProduceIdenticalModels_AndAreDeterministic()
        {
            CompositionScenario c = LocalData.Config.Composition;
            using (var temp = new TempFolder())
            {
                string runtimeDir = temp.Directory("runtime");
                string runtimeAgainDir = temp.Directory("runtime-again");
                string utilsDir = temp.Directory("utils");
                IReadOnlyList<ComposedModelFile> viaRuntime = ComposeRunner.ViaRuntime(c.Building, c.Products, c.ProductsMap, c.Layout, runtimeDir, withWexbim: false);
                IReadOnlyList<ComposedModelFile> viaRuntimeAgain = ComposeRunner.ViaRuntime(c.Building, c.Products, c.ProductsMap, c.Layout, runtimeAgainDir, withWexbim: false);
                IReadOnlyList<ComposedModelFile> viaUtils = ComposeRunner.ViaUtils(c.Building, c.Products, c.ProductsMap, c.Layout, utilsDir, withWexbim: false, temp.File("missing.log"));

                _output.WriteLine($"{viaRuntime.Count} model(s) composed");
                Assert.Equal(viaRuntime.Select(m => m.GroupId), viaUtils.Select(m => m.GroupId));
                Assert.Equal(viaRuntime.Select(m => m.GroupId), viaRuntimeAgain.Select(m => m.GroupId));

                foreach (ComposedModelFile model in viaRuntime)
                {
                    JObject reference = ModelSummarizer.Summarize(Path.Combine(runtimeDir, model.IfcFile));
                    Assert.Empty(SummaryComparer.Compare(reference, ModelSummarizer.Summarize(Path.Combine(runtimeAgainDir, model.IfcFile))));
                    Assert.Empty(SummaryComparer.Compare(reference, ModelSummarizer.Summarize(Path.Combine(utilsDir, model.IfcFile))));
                }
            }
        }
    }

    /// <summary>
    /// Compares the current library with a baseline captured earlier (scripts\capture-baseline.ps1 -Config)
    /// from the unmodified code. Same implementation as 'IfcComBridge.Cli baseline-compare'.
    /// </summary>
    public class BaselineComparisonTests
    {
        private readonly ITestOutputHelper _output;

        public BaselineComparisonTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [BaselineFact]
        public void CurrentLibrary_MatchesTheCapturedBaseline()
        {
            LocalTestConfiguration config = LocalData.Config;
            using (var temp = new TempFolder())
            {
                BaselineComparisonResult result = BaselineComparison.Run(
                    config.ToBaselineInputs(), LocalData.BaselineDirectory, temp.Path, new HashSet<string>(), _output.WriteLine);

                Assert.True(result.InputProblems.Count == 0,
                    "The configured inputs cannot be compared with this baseline:\n  " + string.Join("\n  ", result.InputProblems));
                foreach (string line in result.Differences.Take(200))
                    _output.WriteLine(line);
                Assert.Empty(result.Differences);
            }
        }
    }

    internal sealed class TempFolder : System.IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IfcComBridge.IntegrationTests", System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string Directory(string name)
        {
            string dir = System.IO.Path.Combine(Path, name);
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (System.UnauthorizedAccessException) { }
        }
    }
}
