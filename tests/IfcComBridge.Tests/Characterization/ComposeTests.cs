using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IfcComBridge.Cli.Commands;
using IfcComBridge.Cli.Summary;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xbim.Ifc4.Interfaces;
using Xunit;
using static IfcComBridge.Tests.Characterization.ComposeFixture;
using IfcComBridge.Composition;
using IfcComBridge.IO;
using IfcComBridge.Infrastructure;

namespace IfcComBridge.Tests.Characterization
{
    /// <summary>
    /// LoadIfcJson: one model per layout group = full copy of the building + placed catalogue products
    /// + recreated relations. See SyntheticModels.Layout() for what each set exercises.
    /// </summary>
    public class ComposeTests : IClassFixture<ComposeFixture>
    {
        private readonly ComposeFixture _fx;

        public ComposeTests(ComposeFixture fixture)
        {
            _fx = fixture;
        }

        [Fact]
        public void Q6_GroupsWithoutAnyPlacedProduct_AreSkipped()
        {
            Assert.Equal(new[] { "G1", "G3" }, _fx.Models.Select(m => m.GroupId));
        }

        [Fact]
        public void MissingItemsLog_ListsEveryUnresolvedSet()
        {
            Assert.Equal(new[]
            {
                "Set_id \"S4\" - id \"8\" - name \"GHOST\": Requested IFC item not found, ifc_guid: \"no-such-guid\", ifc_name: \"NO_SUCH_PRODUCT\"",
                "Set_id \"S4\" - id \"9\" - name \"UNKNOWN\": Requested JSON item not found",
                "Set_id \"S4\" - id \"10\" - name \"SHORT-1500\": IFC object with suitable length not found",
                "Set_id \"S9\" - id \"11\" - name \"UNKNOWN-2\": Requested JSON item not found",
            }, File.ReadAllLines(_fx.MissingLog));
        }

        [Fact]
        public void Copies_AreNamedAndDescribedFromTheLayout_WithHtmlDecodedNames()
        {
            List<IIfcProduct> copies = _fx.Copies("G1");
            Assert.Equal(new[]
            {
                "BEAM_A [BEAM&A]", "BEAM_A [BEAM&A]", "CAT_1200 [CAT-1100]", "CAT_1500 [CAT-1600]",
                "CAT_1000 [CAT-1000-clip]", "EXTRUDED_B [EXT]", "WALL_W [WALL]",
            }, copies.Select(p => Text(p.Name)));
            Assert.Equal(new[]
            {
                "JSON [ID: 1, SET_ID: S1]", "JSON [ID: 2, SET_ID: S1]", "JSON [ID: 3, SET_ID: S2]", "JSON [ID: 4, SET_ID: S2]",
                "JSON [ID: 5, SET_ID: S2]", "JSON [ID: 6, SET_ID: S3]", "JSON [ID: 7, SET_ID: S3]",
            }, copies.Select(p => Text(p.Description)));
        }

        [Fact]
        public void Q8_PrefixFallback_TakesTheFirstMatchingKey()
        {
            // "CAT-1100" starts with both "CAT" (catalogue) and the later "CAT-11" (BEAM_A); the first key wins.
            Assert.Equal("CAT_1200 [CAT-1100]", Text(_fx.Copies("G1")[2].Name));
        }

        public static IEnumerable<object[]> ExpectedPlacements() => new[]
        {
            // set, location x/y/z, refDirection x/y  (Axis is always +Z)
            new object[] { 0, 501.0, 0.0, 0.0, 1.0, 0.0 },                 // Q1: non-catalogue products get a +1 X shift
            new object[] { 1, 501.0, 300.0, 0.0, 1.0, 0.0 },
            new object[] { 2, 0.0, 2000.0, 0.0, 1.0, 0.0 },                // catalogue: no +1 shift
            new object[] { 3, 1000.0, 2000.0, 0.0, 0.0, -1.0 },            // angle 90 -> placement uses -angle
            new object[] { 4, 1950.0, 2000.0, 0.0, 1.0, 0.0 },             // clip offset -50 along X
            new object[] { 5, 3156.198729810778, 3370.544136109991, -25.0, -0.5, -0.866025403784 }, // 30+90 deg, centre offsets, +1
            new object[] { 6, 1.0, 5000.0, 0.0, 1.0, 0.0 },
        };

        [Theory]
        [MemberData(nameof(ExpectedPlacements))]
        public void Copies_ArePlacedFromTranslateRotationAndCatalogueOffsets(int index, double x, double y, double z, double refX, double refY)
        {
            var local = (IIfcLocalPlacement)_fx.Copies("G1")[index].ObjectPlacement;
            Assert.Null(local.PlacementRelTo);
            var placement = (IIfcAxis2Placement3D)local.RelativePlacement;
            Assert.Equal(x, placement.Location.X, 6);
            Assert.Equal(y, placement.Location.Y, 6);
            Assert.Equal(z, placement.Location.Z, 6);
            Assert.Equal(refX, placement.RefDirection.X, 9);
            Assert.Equal(refY, placement.RefDirection.Y, 9);
            Assert.Equal(0, placement.RefDirection.Z, 9);
            Assert.Equal(1, placement.Axis.Z, 9);
        }

        [Theory]
        [InlineData(0, 1000)]   // BEAM_A, unscaled
        [InlineData(2, 1100)]   // CAT_1200 scaled 1100/1200
        [InlineData(3, 1600)]   // CAT_1500 upscaled 1600/1500
        [InlineData(4, 900)]    // CAT_1000 clipped to 900
        [InlineData(5, 800)]    // extrusion, scale 1 -> untouched
        public void Geometry_IsStretchedAlongLocalXOnly(int index, double expectedMaxX)
        {
            IIfcRepresentationItem item = _fx.Copies("G1")[index].Representation.Representations.Single().Items.Single();
            List<IIfcCartesianPoint> points = PointsOf(item);
            Assert.Equal(expectedMaxX, points.Max(p => p.X), 6);
            Assert.Equal(0, points.Min(p => p.X), 6);
            Assert.Equal(100, points.Max(p => p.Y), 6);
        }

        private static List<IIfcCartesianPoint> PointsOf(IIfcRepresentationItem item)
        {
            switch (item)
            {
                case IIfcFacetedBrep brep:
                    return brep.Outer.CfsFaces.SelectMany(f => f.Bounds).Select(b => b.Bound).OfType<IIfcPolyLoop>().SelectMany(l => l.Polygon).ToList();
                case IIfcExtrudedAreaSolid solid:
                    return ((IIfcPolyline)((IIfcArbitraryClosedProfileDef)solid.SweptArea).OuterCurve).Points.ToList();
                default:
                    throw new InvalidOperationException(item.GetType().Name);
            }
        }

        [Fact]
        public void SameProductAtSameScale_SharesGeometryItems_ButNotRepresentations()
        {
            List<IIfcProduct> copies = _fx.Copies("G1");
            IIfcRepresentation first = copies[0].Representation.Representations.Single();
            IIfcRepresentation second = copies[1].Representation.Representations.Single();
            Assert.NotEqual(first.EntityLabel, second.EntityLabel);
            Assert.Equal(first.Items.Single().EntityLabel, second.Items.Single().EntityLabel);
        }

        [Fact]
        public void Q13_RepeatedCopiesOfOneProduct_ShareItsGlobalId()
        {
            List<IIfcProduct> copies = _fx.Copies("G1");
            Assert.Equal(copies[0].GlobalId.ToString(), copies[1].GlobalId.ToString());
            Assert.Equal(SyntheticModels.Gid(SyntheticModels.BeamA), copies[0].GlobalId.ToString());
        }

        [Fact]
        public void Relations_AreRecreatedForCopies()
        {
            foreach (IIfcProduct copy in _fx.Copies("G1"))
            {
                var element = (IIfcElement)copy;
                Assert.Equal(new[] { SyntheticModels.BuildingStorey }, element.ContainedInStructure.Select(r => Text(r.RelatingStructure.Name)));
                Assert.Equal(new[] { "Pset_Synthetic" }, copy.IsDefinedBy.Select(r => Text(((IIfcPropertySet)r.RelatingPropertyDefinition).Name)));
                Assert.Equal(new[] { "SyntheticTimber" }, copy.HasAssociations.OfType<IIfcRelAssociatesMaterial>().Select(r => ((IIfcMaterial)r.RelatingMaterial).Name.ToString()));
                Assert.Equal(new[] { "SyntheticLayer" }, copy.Representation.Representations.Single().LayerAssignments.Select(l => l.Name.ToString()));

                bool isBeam = copy is IIfcBeam;
                Assert.Equal(isBeam ? new[] { "SyntheticBeamType" } : new string[0], copy.IsTypedBy.Select(r => Text(r.RelatingType.Name)));
                Assert.Equal(isBeam ? new[] { "SC-01" } : new string[0],
                    copy.HasAssociations.OfType<IIfcRelAssociatesClassification>().Select(r => Text(((IIfcClassificationReference)r.RelatingClassification).Identification)));
            }
        }

        [Fact]
        public void SurfaceStyles_FollowTheCopiedBreps()
        {
            foreach (IIfcProduct copy in _fx.Copies("G1").Where(p => Text(p.Name).StartsWith("BEAM_A") || Text(p.Name).StartsWith("CAT_")))
            {
                IIfcRepresentationItem item = copy.Representation.Representations.Single().Items.Single();
                IIfcStyledItem styled = Assert.Single(item.StyledByItem);
                Assert.Equal("SyntheticRed", Text(((IIfcSurfaceStyle)styled.Styles.Single()).Name));
            }
        }

        [Fact]
        public void Q3_OutputUsesTheProductModelsUnits()
        {
            JObject summary = ModelSummarizer.Summarize(_fx.SavedFiles["G1"]);
            Assert.Equal(new[] { "LENGTHUNIT: MILLIMETRE", "VOLUMEUNIT: CUBIC_METRE" }, summary["units"].Values<string>());
        }

        [Fact]
        public void TheWholeBuildingIsCopiedIntoEveryGroup()
        {
            foreach (string group in new[] { "G1", "G3" })
                Assert.Single(_fx.Group(group).IfcStore.Instances.OfType<IIfcSlab>());
        }

        [Fact]
        public void Output_HasNoOwnerHistory_AndCarriesTheBuildingInputsIdentity_NotTheLibrarys()
        {
            // Owner histories are stripped while copying and the leftovers deleted, so the library's own
            // editor credentials never reach the file. The person/organization/application entities of
            // the BUILDING input are copied along with everything else and stay behind, unreferenced.
            JObject summary = ModelSummarizer.Summarize(_fx.SavedFiles["G1"], includeIdentity: true);
            Assert.Null(summary["entityCounts"]["IfcOwnerHistory"]);
            Assert.Equal(new[] { SyntheticModels.Editor.EditorsOrganisationName }, summary["identity"]["organizations"].Values<string>());
            Assert.DoesNotContain(EditorIdentity.Create().ApplicationDevelopersName,
                summary["identity"]["organizations"].Values<string>());
        }

        [Fact]
        public void GoldenSummaries_OfSyntheticComposition()
        {
            Golden.AssertMatches("compose-G1.summary.json", ModelSummarizer.Summarize(_fx.SavedFiles["G1"]));
            Golden.AssertMatches("compose-G3.summary.json", ModelSummarizer.Summarize(_fx.SavedFiles["G3"]));
        }

        [Fact]
        public void RuntimeApi_ProducesTheSameModelsAsTheStaticHelper()
        {
            using (var temp = new TempDirectory())
            {
                IReadOnlyList<ComposedModelFile> files = ComposeRunner.ViaRuntime(
                    _fx.Inputs.Building, _fx.Inputs.Products, _fx.Inputs.ProductsMap, _fx.Inputs.Layout, temp.Path, withWexbim: false);
                Assert.Equal(new[] { "G1", "G3" }, files.Select(f => f.GroupId));
                foreach (ComposedModelFile file in files)
                {
                    var differences = SummaryComparer.Compare(
                        ModelSummarizer.Summarize(_fx.SavedFiles[file.GroupId]),
                        ModelSummarizer.Summarize(Path.Combine(temp.Path, file.IfcFile)));
                    Assert.Empty(differences);
                }
            }
        }

        [Fact]
        public void Q15_DefaultOptimizationIsSize_WhichMergesDuplicatePoints()
        {
            Assert.Equal(ProductLayoutComposer.OptimizationTarget.Size, ProductLayoutComposer.CurrentOptimization);

            int sizePoints = _fx.Group("G1").IfcStore.Instances.OfType<IIfcCartesianPoint>().Count();
            ProductLayoutComposer.CurrentOptimization = ProductLayoutComposer.OptimizationTarget.Speed;
            try
            {
                var map = (JObject)ModelFiles.LoadJson(_fx.Inputs.ProductsMap, "JSON Data");
                List<ComposedModel> models = ProductLayoutComposer.LoadIfcJson(_fx.Inputs.Building, _fx.Inputs.Products, map, _fx.Inputs.Layout).ToList();
                try
                {
                    int speedPoints = models.Single(m => m.GroupId == "G1").IfcStore.Instances.OfType<IIfcCartesianPoint>().Count();
                    Assert.True(speedPoints > sizePoints, $"Speed: {speedPoints} points, Size: {sizePoints} points");
                }
                finally
                {
                    models.ForEach(m => m.Dispose());
                }
            }
            finally
            {
                ProductLayoutComposer.CurrentOptimization = ProductLayoutComposer.OptimizationTarget.Size;
            }
        }
    }

    /// <summary>Compose failure modes (separate inputs, no shared fixture).</summary>
    public class ComposeFailureTests
    {
        [Fact]
        public void Q12_DuplicateGlobalIdInProducts_ThrowsArgumentException()
        {
            using (var temp = new TempDirectory())
            {
                var inputs = SyntheticModels.WriteComposeInputs(temp.Path, duplicateProductGlobalId: true);
                var map = (JObject)ModelFiles.LoadJson(inputs.ProductsMap, "JSON Data");
                Assert.Throws<ArgumentException>(() => ProductLayoutComposer.LoadIfcJson(inputs.Building, inputs.Products, map, inputs.Layout).ToList());
            }
        }

        [Fact]
        public void Q10_Q2_RuntimeKeepsModelsComposedBeforeAFailure_AndTheStackTraceIsReset()
        {
            // Group "OK" is valid; the set in group "BROKEN" has no "rotation" object (quirk Q5).
            var layout = (JArray)SyntheticFixtures.Read(SyntheticFixtures.MissingRotationLayoutFile);

            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            {
                var inputs = SyntheticModels.WriteComposeInputs(temp.Path, layout);
                var ex = Assert.Throws<NullReferenceException>(() =>
                    runtime.LoadIfcJson(inputs.Building, inputs.Products, File.ReadAllText(inputs.ProductsMap), inputs.Layout));

                // Q10: the model of the first group stays loaded although the call failed.
                Assert.Equal("OK", runtime.GetModelGroupId(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetModelGroupId(1));

                // Q2: "throw ex" in LoadIfcJson discards the frames below it (the NRE originates in CopyProduct).
                Assert.DoesNotContain("CopyProduct", ex.StackTrace);
                Assert.Contains("LoadIfcJson", ex.StackTrace);
            }
        }
    }
}
