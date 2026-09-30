using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IfcComBridge.Cli;
using IfcComBridge.Cli.Baseline;
using IfcComBridge.Cli.Commands;
using IfcComBridge.Cli.Summary;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xunit;

namespace IfcComBridge.Tests.Harness
{
    /// <summary>Self-tests of the characterization tooling (not of the library).</summary>
    public class OutputSafetyTests
    {
        [Fact]
        public void OutputFile_RefusesToOverwriteAnInput()
        {
            using (var temp = new TempDirectory())
            {
                string input = temp.File("in.ifc");
                File.WriteAllText(input, "x");
                Assert.Throws<UsageException>(() => OutputGuard.OutputFile(input, force: true, new[] { input }));
            }
        }

        [Fact]
        public void OutputFile_RefusesExistingFileWithoutForce()
        {
            using (var temp = new TempDirectory())
            {
                string output = temp.File("out.ifc");
                File.WriteAllText(output, "x");
                Assert.Throws<UsageException>(() => OutputGuard.OutputFile(output, force: false, new string[0]));
                Assert.Equal(output, OutputGuard.OutputFile(output, force: true, new string[0]));
            }
        }

        [Fact]
        public void OutputDirectory_RefusesFoldersHoldingInputs_AndNonEmptyFoldersWithoutForce()
        {
            using (var temp = new TempDirectory())
            {
                string input = temp.File("in.ifc");
                File.WriteAllText(input, "x");
                Assert.Throws<UsageException>(() => OutputGuard.OutputDirectory(temp.Path, force: true, new[] { input }));

                string other = temp.Directory("other");
                File.WriteAllText(Path.Combine(other, "existing.txt"), "x");
                Assert.Throws<UsageException>(() => OutputGuard.OutputDirectory(other, force: false, new[] { input }));
            }
        }

        [Fact]
        public void BaselineGuard_RefusesFoldersInsideTheRepository()
        {
            using (var temp = new TempDirectory())
            {
                File.WriteAllText(temp.File(RepositoryGuard.RepositoryMarker), string.Empty);
                string inside = temp.Directory(Path.Combine("a", "b"));
                Assert.Throws<UsageException>(() => RepositoryGuard.AssertOutsideRepository(inside));
            }

            using (var outside = new TempDirectory())
                RepositoryGuard.AssertOutsideRepository(outside.Path);
        }

        [Theory]
        [InlineData(0, "G1", "000_G1")]
        [InlineData(7, "a b/c:d", "007_a_b_c_d")]
        [InlineData(12, "", "012_group")]
        public void ModelFileStem_IsIndexedAndFileSystemSafe(int index, string groupId, string expected)
        {
            Assert.Equal(expected, ComposeRunner.ModelFileStem(index, groupId));
        }
    }

    public class SummaryComparerTests
    {
        [Fact]
        public void ReportsChangedAddedRemovedAndArrayLengthDifferences()
        {
            var baseline = JObject.Parse("{\"a\":1,\"b\":{\"c\":[1,2]},\"gone\":true,\"sha256\":\"x\"}");
            var current = JObject.Parse("{\"a\":2,\"b\":{\"c\":[1]},\"new\":true,\"sha256\":\"y\"}");
            IReadOnlyList<string> diff = SummaryComparer.Compare(baseline, current, new HashSet<string> { "sha256" });
            Assert.Equal(new[]
            {
                "$.a: 1 -> 2",
                "$.b.c: array length 2 -> 1",
                "$.gone: true -> <absent>",
                "$.new: <absent> -> true",
            }, diff);
        }

        [Fact]
        public void NullStringAssignedInCode_EqualsParsedJsonNull()
        {
            var built = new JObject { ["x"] = (string)null };
            Assert.Empty(SummaryComparer.Compare(built, JObject.Parse("{\"x\":null}")));
            Assert.Single(SummaryComparer.Compare(built, JObject.Parse("{\"x\":\"\"}")));
        }

        [Fact]
        public void IdenticalDocuments_HaveNoDifferences()
        {
            var json = JObject.Parse("{\"a\":[{\"b\":1.5}]}");
            Assert.Empty(SummaryComparer.Compare(json, json.DeepClone()));
        }
    }

    public class SummaryTests
    {
        [Fact]
        public void Summary_IsDeterministic_AndHasNoIdentityOrPathsByDefault()
        {
            using (var temp = new TempDirectory())
            {
                string path = temp.File("building.ifc");
                SyntheticModels.WriteBuildingModel(path);
                JObject first = ModelSummarizer.Summarize(path);
                JObject second = ModelSummarizer.Summarize(path);

                Assert.Empty(SummaryComparer.Compare(first, second));
                Assert.Null(first["identity"]);
                Assert.DoesNotContain(temp.Path, ModelSummarizer.ToJsonText(first), StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void IdentityIsOptIn()
        {
            using (var temp = new TempDirectory())
            {
                string path = temp.File("building.ifc");
                SyntheticModels.WriteBuildingModel(path);
                JObject summary = ModelSummarizer.Summarize(path, includeIdentity: true);
                Assert.Contains(SyntheticModels.Editor.EditorsOrganisationName, summary["identity"]["organizations"].Values<string>());
            }
        }

        [Fact]
        public void GoldenSummary_OfSyntheticBuilding()
        {
            using (var temp = new TempDirectory())
            {
                string path = temp.File("building.ifc");
                SyntheticModels.WriteBuildingModel(path);
                Golden.AssertMatches("building.summary.json", ModelSummarizer.Summarize(path));
            }
        }
    }

    /// <summary>
    /// Deployment checks without COM registration: the library is loaded the way /codebase activation
    /// does, in an isolated AppDomain whose base folder and configuration are unrelated to it.
    /// </summary>
    public class IsolatedEngineProbeTests
    {
        private static string Deploy(TempDirectory temp, string name, bool withEngine)
        {
            string dir = temp.Directory(name);
            string library = typeof(ComRuntime).Assembly.Location;
            File.Copy(library, Path.Combine(dir, Path.GetFileName(library)));
            if (withEngine)
            {
                string engine = Path.Combine(Path.GetDirectoryName(library), "Xbim.Geometry.Engine64.dll");
                File.Copy(engine, Path.Combine(dir, Path.GetFileName(engine)));
            }
            return Path.Combine(dir, Path.GetFileName(library));
        }

        [Fact]
        public void LibraryAlone_CannotLoadTheGeometryEngine()
        {
            using (var temp = new TempDirectory())
            {
                EngineProbeResult result = IsolatedEngineProbe.ProbeInIsolatedDomain(Deploy(temp, "dll-only", withEngine: false), IsolatedEngineProbe.DefaultRuntimeTypeName);
                Assert.False(result.Success);
                Assert.Contains("Xbim.Geometry.Engine64", result.Error);
            }
        }

        [Fact]
        public void LibraryWithEngineSideBySide_LoadsTheEngineFromThatFolder()
        {
            using (var temp = new TempDirectory())
            {
                string library = Deploy(temp, "dll-and-engine", withEngine: true);
                EngineProbeResult result = IsolatedEngineProbe.ProbeInIsolatedDomain(library, IsolatedEngineProbe.DefaultRuntimeTypeName);
                Assert.True(result.Success, result.Error);
                Assert.Contains(result.GeometryAssemblies, a => a.StartsWith("Xbim.Geometry.Engine64 ") && a.Contains(Path.GetDirectoryName(library)));
            }
        }
    }

    public class BaselineRunnerTests
    {
        [Fact]
        public void TwoRunsOnTheSameInputs_AreIdentical_AndContainNoLocalPaths()
        {
            using (var temp = new TempDirectory())
            {
                var compose = SyntheticModels.WriteComposeInputs(temp.Directory("inputs"));
                int slabLabel = (int)ModelSummarizer.Summarize(compose.Building)["products"].Single()["label"];
                string transforms = Path.Combine(temp.Path, "inputs", "transforms.json");
                SyntheticModels.WriteJson(transforms, SyntheticModels.Transforms(
                    SyntheticModels.TransformEntry(slabLabel, SyntheticModels.Matrix(10, 20, 0, rotationZDegrees: 45))));

                var inputs = new BaselineInputs
                {
                    Ifc = compose.Building,
                    Building = compose.Building,
                    Products = compose.Products,
                    ProductsMap = compose.ProductsMap,
                    Layout = compose.Layout,
                    Transforms = transforms,
                };
                string first = temp.Directory("run1");
                string second = temp.Directory("run2");
                JObject manifest = BaselineRunner.Run(inputs, first, null);
                BaselineRunner.Run(inputs, second, null);

                Assert.All(manifest["steps"], step => Assert.True((bool)step["ok"], (string)step["step"]));
                Assert.Empty(SummaryComparer.CompareDirectories(first, second));

                string inputsFolder = Path.Combine(temp.Path, "inputs");
                foreach (string file in Directory.EnumerateFiles(first, "*.json", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    Assert.DoesNotContain(inputsFolder, text, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(first, text, StringComparison.OrdinalIgnoreCase);
                }

                JObject update = JObject.Parse(File.ReadAllText(Path.Combine(first, "update", "result.summary.json")));
                Assert.True((bool)update["updateModelReturned"]);
            }
        }
    }
}
