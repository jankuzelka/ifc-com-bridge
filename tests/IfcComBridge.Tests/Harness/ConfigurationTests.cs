using System;
using System.IO;
using System.Linq;
using System.Text;
using IfcComBridge.Cli;
using IfcComBridge.Cli.Baseline;
using IfcComBridge.Cli.Configuration;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IfcComBridge.Tests.Harness
{
    /// <summary>A synthetic input set written once; configurations under test point at its files.</summary>
    public sealed class SyntheticSetFixture : IDisposable
    {
        private readonly TempDirectory _temp = new TempDirectory();

        public SyntheticSetFixture()
        {
            Directory = _temp.Directory("set");
            ConfigPath = SyntheticInputSet.Write(Directory);
            Ifc2x3 = Path.Combine(Directory, "ifc2x3.ifc");
            SyntheticModels.WriteIfc2x3Model(Ifc2x3);
            NotIfc = Path.Combine(Directory, "not-an-ifc.ifc");
            System.IO.File.WriteAllText(NotIfc, "hello");
        }

        public string Directory { get; }
        public string ConfigPath { get; }
        public string Ifc2x3 { get; }
        public string NotIfc { get; }

        public string File(string name) => Path.Combine(Directory, name);

        /// <summary>Writes a configuration file into the set folder (so relative paths refer to the set).</summary>
        public string WriteConfig(string json, string name = null)
        {
            string path = Path.Combine(Directory, name ?? $"config-{Guid.NewGuid():N}.json");
            System.IO.File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }

        public void Dispose() => _temp.Dispose();
    }

    public class ConfigurationTests : IClassFixture<SyntheticSetFixture>
    {
        private const string Composition =
            "\"composition\": { \"building\": \"building.ifc\", \"products\": \"products.ifc\", \"productsMap\": \"products-map.json\", \"layout\": \"layout.json\" }";

        private readonly SyntheticSetFixture _set;

        public ConfigurationTests(SyntheticSetFixture set)
        {
            _set = set;
        }

        private static ConfigurationException Invalid(string configPath) =>
            Assert.Throws<ConfigurationException>(() => LocalTestConfiguration.Load(configPath));

        [Fact]
        public void SyntheticSetConfiguration_IsValid_AndResolvesPathsAgainstTheConfigFolder()
        {
            string previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Path.GetTempPath(); // must not matter
                LocalTestConfiguration config = LocalTestConfiguration.Load(_set.ConfigPath);

                Assert.Equal(_set.File("products.ifc"), config.Model.Ifc);
                Assert.Equal(_set.File("transforms.json"), config.Model.Transforms);
                Assert.Equal(_set.File("building.ifc"), config.Composition.Building);
                Assert.Equal(_set.File("products.ifc"), config.Composition.Products);
                Assert.Equal(_set.File("products-map.json"), config.Composition.ProductsMap);
                Assert.Equal(_set.File("layout.json"), config.Composition.Layout);
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }

        [Fact]
        public void RelativePathsInASubfolder_AndAbsolutePathsWithForwardSlashes_AreResolved()
        {
            string sub = Path.Combine(_set.Directory, "private-config");
            Directory.CreateDirectory(sub);
            string configPath = Path.Combine(sub, "tests.local.json");
            string absoluteProducts = _set.File("products.ifc").Replace('\\', '/');
            File.WriteAllText(configPath,
                "{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"" + absoluteProducts + "\", \"transforms\": \"../transforms.json\" } } }");

            LocalTestConfiguration config = LocalTestConfiguration.Load(configPath);
            Assert.Equal(_set.File("products.ifc"), config.Model.Ifc);
            Assert.Equal(_set.File("transforms.json"), config.Model.Transforms);
        }

        [Fact]
        public void ToJson_ListsTheResolvedPaths_AndNullForAScenarioThatIsNotConfigured()
        {
            JObject json = LocalTestConfiguration.Load(_set.WriteConfig("{ \"version\": 1, \"scenarios\": { " + Composition + " } }")).ToJson();

            Assert.Equal(1, (int)json["version"]);
            Assert.Equal(JTokenType.Null, json["model"].Type);
            Assert.Equal(_set.File("building.ifc"), (string)json["composition"]["building"]);
            Assert.Equal(_set.File("products.ifc"), (string)json["composition"]["products"]);
            Assert.Equal(_set.File("products-map.json"), (string)json["composition"]["productsMap"]);
            Assert.Equal(_set.File("layout.json"), (string)json["composition"]["layout"]);
        }

        [Fact]
        public void ModelOnly_WithoutTransforms_IsValid()
        {
            var config = LocalTestConfiguration.Load(_set.WriteConfig("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"building.ifc\" } } }"));
            Assert.Equal(_set.File("building.ifc"), config.Model.Ifc);
            Assert.Null(config.Model.Transforms);
            Assert.Null(config.Composition);
        }

        [Fact]
        public void CompositionOnly_IsValid()
        {
            var config = LocalTestConfiguration.Load(_set.WriteConfig("{ \"version\": 1, \"scenarios\": { " + Composition + " } }"));
            Assert.Null(config.Model);
            Assert.Equal(_set.File("layout.json"), config.Composition.Layout);
        }

        [Theory]
        [InlineData("{ \"version\": 1, \"scenarios\": {} }")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": null, \"composition\": null } }")]
        public void NoScenario_IsValid_AndEnablesNothing(string json)
        {
            var config = LocalTestConfiguration.Load(_set.WriteConfig(json));
            Assert.False(config.HasAnyScenario);
            BaselineInputs inputs = config.ToBaselineInputs();
            Assert.Empty(inputs.All());
        }

        [Theory]
        [InlineData("{", "not valid JSON")]
        [InlineData("[1]", "the root must be a JSON object")]
        [InlineData("{ \"scenarios\": {} }", "missing 'version'")]
        [InlineData("{ \"version\": \"1\", \"scenarios\": {} }", "'version' must be an integer")]
        [InlineData("{ \"version\": 2, \"scenarios\": {} }", "unsupported version 2")]
        [InlineData("{ \"version\": 1 }", "missing 'scenarios'")]
        [InlineData("{ \"version\": 1, \"scenarios\": [] }", "'scenarios' must be an object")]
        [InlineData("{ \"version\": 1, \"scenarios\": {}, \"extra\": 1 }", "unknown property 'extra'")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"modle\": {} } }", "unknown scenario 'modle'")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": \"building.ifc\" } }", "scenarios.model must be an object")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": {} } }", "scenarios.model.ifc: missing")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": 5 } } }", "scenarios.model.ifc: must be a non-empty path string")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"building.ifc\", \"transform\": \"transforms.json\" } } }", "unknown field 'transform'")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"composition\": { \"building\": \"building.ifc\", \"products\": \"products.ifc\", \"productsMap\": \"products-map.json\" } } }", "scenarios.composition.layout: missing")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"missing.ifc\" } } }", "scenarios.model.ifc: file not found")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"building.ifc\", \"transforms\": \"missing.json\" } } }", "scenarios.model.transforms: file not found")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"edge-cases\" } } }", "is a directory")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"products-map.json\" } } }", "unsupported extension '.json'")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"not-an-ifc.ifc\" } } }", "not an IFC STEP file")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"ifc2x3.ifc\" } } }", "the library accepts IFC4 only")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"building.ifc\", \"transforms\": \"layout.json\" } } }", "'modified_products' array")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"composition\": { \"building\": \"building.ifc\", \"products\": \"products.ifc\", \"productsMap\": \"layout.json\", \"layout\": \"layout.json\" } } }", "the root must be a JSON object (products map)")]
        [InlineData("{ \"version\": 1, \"scenarios\": { \"composition\": { \"building\": \"building.ifc\", \"products\": \"products.ifc\", \"productsMap\": \"products-map.json\", \"layout\": \"products-map.json\" } } }", "the root must be a JSON array of groups")]
        public void InvalidConfigurations_AreRejectedWithAClearProblem(string json, string expectedProblem)
        {
            ConfigurationException ex = Invalid(_set.WriteConfig(json));
            Assert.Contains(ex.Problems, p => p.Contains(expectedProblem));
            Assert.Contains(expectedProblem, ex.Message);
        }

        [Fact]
        public void EveryProblemIsReportedTogether()
        {
            ConfigurationException ex = Invalid(_set.WriteConfig(
                "{ \"version\": 3, \"scenarios\": { \"model\": { \"ifc\": \"missing.ifc\" }, \"composition\": { \"building\": \"building.ifc\" } }, \"x\": 1 }"));
            Assert.Contains(ex.Problems, p => p.Contains("unsupported version 3"));
            Assert.Contains(ex.Problems, p => p.Contains("unknown property 'x'"));
            Assert.Contains(ex.Problems, p => p.Contains("scenarios.model.ifc: file not found"));
            Assert.Contains(ex.Problems, p => p.Contains("scenarios.composition.products: missing"));
            Assert.Contains(ex.Problems, p => p.Contains("scenarios.composition.productsMap: missing"));
            Assert.Contains(ex.Problems, p => p.Contains("scenarios.composition.layout: missing"));
        }

        [Fact]
        public void MissingConfigurationFile_IsRejected()
        {
            ConfigurationException ex = Invalid(_set.File("no-such-config.json"));
            Assert.Contains("configuration file not found", ex.Message);
        }

        [Fact]
        public void ConfigurationOrInputsInsideTheRepository_AreRejected()
        {
            using (var fakeRepository = new TempDirectory())
            {
                File.WriteAllText(fakeRepository.File(RepositoryGuard.RepositoryMarker), string.Empty);
                string inside = fakeRepository.Directory("tests");
                string privateInput = Path.Combine(inside, "model.ifc");
                File.Copy(_set.File("building.ifc"), privateInput);

                string configInside = Path.Combine(inside, "tests.local.json");
                File.WriteAllText(configInside, "{ \"version\": 1, \"scenarios\": {} }");
                Assert.Contains(Invalid(configInside).Problems, p => p.Contains("the configuration file is inside the repository"));

                string configOutside = _set.WriteConfig(
                    "{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"" + privateInput.Replace('\\', '/') + "\" } } }");
                Assert.Contains(Invalid(configOutside).Problems, p => p.Contains("private inputs must stay outside it"));
            }
        }

        [Fact]
        public void BaselineRoles_FollowTheScenarios()
        {
            BaselineInputs inputs = LocalTestConfiguration.Load(_set.ConfigPath).ToBaselineInputs();
            Assert.Equal(new[] { "building", "ifc", "layout", "products", "productsMap", "transforms" }, inputs.All().Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal));
            Assert.True(inputs.HasSingleModel && inputs.HasCompose && inputs.HasUpdate);
        }
    }

    public class LocalTestGateTests : IClassFixture<SyntheticSetFixture>
    {
        private readonly SyntheticSetFixture _set;

        public LocalTestGateTests(SyntheticSetFixture set)
        {
            _set = set;
        }

        [Fact]
        public void WithoutConfiguration_EveryScenarioIsSkipped()
        {
            foreach (LocalScenario scenario in Enum.GetValues(typeof(LocalScenario)))
                Assert.Contains("No private test configuration", LocalTestGate.SkipReason(null, scenario));
            Assert.Contains("No private test configuration", LocalTestGate.BaselineSkipReason(null, null));
        }

        [Fact]
        public void AbsentScenarios_AreSkipped_ConfiguredOnesRun()
        {
            string modelOnly = _set.WriteConfig("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"building.ifc\" } } }");
            Assert.Null(LocalTestGate.SkipReason(modelOnly, LocalScenario.Model));
            Assert.Equal("'scenarios.model.transforms' is not configured.", LocalTestGate.SkipReason(modelOnly, LocalScenario.ModelWithTransforms));
            Assert.Equal("Scenario 'composition' is not configured.", LocalTestGate.SkipReason(modelOnly, LocalScenario.Composition));

            foreach (LocalScenario scenario in Enum.GetValues(typeof(LocalScenario)))
                Assert.Null(LocalTestGate.SkipReason(_set.ConfigPath, scenario));
        }

        [Fact]
        public void AnInvalidConfiguration_IsNotSkipped_ItFailsLoudly()
        {
            string broken = _set.WriteConfig("{ \"version\": 1, \"scenarios\": { \"model\": { \"ifc\": \"missing.ifc\" } } }");
            Assert.Null(LocalTestGate.SkipReason(broken, LocalScenario.Model));
            Assert.Throws<ConfigurationException>(() => LocalTestGate.Require(broken));
        }

        [Fact]
        public void BaselineComparison_NeedsBothConfigurationAndBaseline()
        {
            Assert.Contains("No baseline given", LocalTestGate.BaselineSkipReason(_set.ConfigPath, null));
            Assert.Null(LocalTestGate.BaselineSkipReason(_set.ConfigPath, _set.Directory));
        }
    }

    public class BaselineInputCheckTests : IClassFixture<SyntheticSetFixture>
    {
        private readonly SyntheticSetFixture _set;

        public BaselineInputCheckTests(SyntheticSetFixture set)
        {
            _set = set;
        }

        private string Manifest(TempDirectory temp, params (string Role, string Sha)[] inputs)
        {
            var manifest = new JObject
            {
                ["summaryFormat"] = IfcComBridge.Cli.Summary.ModelSummarizer.FormatVersion,
                ["inputs"] = new JArray(inputs.Select(i => new JObject { ["role"] = i.Role, ["sha256"] = i.Sha })),
            };
            File.WriteAllText(temp.File(BaselineRunner.ManifestFile), manifest.ToString());
            return temp.Path;
        }

        [Fact]
        public void MatchingRolesAndHashes_HaveNoProblems()
        {
            using (var temp = new TempDirectory())
            {
                var inputs = new BaselineInputs { Ifc = _set.File("building.ifc") };
                string baseline = Manifest(temp, ("ifc", BaselineRunner.Sha256(inputs.Ifc)));
                Assert.Empty(BaselineComparison.CheckInputs(baseline, inputs));
            }
        }

        [Fact]
        public void DifferentFilesOrRoles_AreReported()
        {
            using (var temp = new TempDirectory())
            {
                var inputs = new BaselineInputs { Ifc = _set.File("building.ifc"), Transforms = _set.File("transforms.json") };
                string baseline = Manifest(temp, ("ifc", "0000"), ("layout", "1111"));
                var problems = BaselineComparison.CheckInputs(baseline, inputs);
                Assert.Contains(problems, p => p.Contains("input 'ifc' is not the file the baseline was captured from"));
                Assert.Contains(problems, p => p.Contains("captured with input 'layout', which is not configured now"));
                Assert.Contains(problems, p => p.Contains("input 'transforms' is configured but was not part of the baseline"));
            }
        }
    }
}
