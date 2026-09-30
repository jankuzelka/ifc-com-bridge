using System;
using System.IO;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.IO;
using Xunit;
using IfcComBridge.Placement;

namespace IfcComBridge.Tests.Characterization
{
    /// <summary>
    /// SetIfcPosition / UpdateIfcPosition (UpdateModel). The transform is 16 values keyed "0".."15" in
    /// column-major order with the translation in 12..14; it is applied to the placement's own values,
    /// i.e. in the frame of PlacementRelTo, not in world coordinates.
    /// </summary>
    public class PlacementTests
    {
        private static IfcStore NewModel() =>
            IfcStore.Create(SyntheticModels.Editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);

        private static void AssertXyz(double x, double y, double z, double ax, double ay, double az)
        {
            Assert.Equal(x, ax, 9);
            Assert.Equal(y, ay, 9);
            Assert.Equal(z, az, 9);
        }

        private static void AssertPoint(IIfcCartesianPoint p, double x, double y, double z) => AssertXyz(x, y, z, p.X, p.Y, p.Z);

        private static void AssertDirection(IIfcDirection d, double x, double y, double z) => AssertXyz(x, y, z, d.X, d.Y, d.Z);

        [Fact]
        public void Translation_MovesLocation_KeepsDirections()
        {
            using (IfcStore model = NewModel())
            using (ITransaction txn = model.BeginTransaction("test"))
            {
                IfcAxis2Placement3D placement = new SyntheticModels.Builder(model, 1).Axis3D(1, 2, 3);
                PlacementUpdater.SetIfcPosition(SyntheticModels.Matrix(10, 20, 30), placement);
                AssertPoint(placement.Location, 11, 22, 33);
                AssertDirection(placement.Axis, 0, 0, 1);
                AssertDirection(placement.RefDirection, 1, 0, 0);
            }
        }

        [Fact]
        public void RotationAboutZ_RotatesLocationAndRefDirectionCounterClockwise()
        {
            using (IfcStore model = NewModel())
            using (ITransaction txn = model.BeginTransaction("test"))
            {
                IfcAxis2Placement3D placement = new SyntheticModels.Builder(model, 1).Axis3D(1, 2, 3);
                PlacementUpdater.SetIfcPosition(SyntheticModels.Matrix(0, 0, 0, rotationZDegrees: 90), placement);
                AssertPoint(placement.Location, -2, 1, 3);
                AssertDirection(placement.Axis, 0, 0, 1);
                AssertDirection(placement.RefDirection, 0, 1, 0);
            }
        }

        [Fact]
        public void Q17_PartialMatrix_DefaultsDifferForLocationAndDirections()
        {
            // Missing keys default to the identity for the location matrix ("0","5","10","15" -> 1) but to
            // ZERO for the direction matrix, so a translation-only JSON collapses Axis and RefDirection.
            var translationOnly = new JObject { ["12"] = 10, ["13"] = 20, ["14"] = 30 };
            using (IfcStore model = NewModel())
            using (ITransaction txn = model.BeginTransaction("test"))
            {
                IfcAxis2Placement3D placement = new SyntheticModels.Builder(model, 1).Axis3D(1, 2, 3);
                PlacementUpdater.SetIfcPosition(translationOnly, placement);
                AssertPoint(placement.Location, 11, 22, 33);
                AssertDirection(placement.Axis, 0, 0, 0);
                AssertDirection(placement.RefDirection, 0, 0, 0);
            }
        }

        [Fact]
        public void Q7_MissingAxis_ThrowsNullReference()
        {
            using (IfcStore model = NewModel())
            using (ITransaction txn = model.BeginTransaction("test"))
            {
                IfcAxis2Placement3D placement = new SyntheticModels.Builder(model, 1).Axis3D(1, 2, 3);
                placement.Axis = null;
                Assert.Throws<NullReferenceException>(() => PlacementUpdater.SetIfcPosition(SyntheticModels.Matrix(1, 1, 1), placement));
            }
        }

        [Fact]
        public void Q7_SharedPointEntity_MovesEveryPlacementUsingIt()
        {
            using (IfcStore model = NewModel())
            using (ITransaction txn = model.BeginTransaction("test"))
            {
                var c = new SyntheticModels.Builder(model, 1);
                IfcCartesianPoint shared = c.Point(0, 0, 0);
                IfcAxis2Placement3D Placement() => c.New<IfcAxis2Placement3D>(p =>
                {
                    p.Location = shared;
                    p.Axis = c.Direction(0, 0, 1);
                    p.RefDirection = c.Direction(1, 0, 0);
                });
                IfcAxis2Placement3D moved = Placement();
                IfcAxis2Placement3D bystander = Placement();

                PlacementUpdater.SetIfcPosition(SyntheticModels.Matrix(100, 0, 0), moved);

                AssertPoint(bystander.Location, 100, 0, 0);
            }
        }

        // ---------------------------------------------------------------- UpdateIfcPosition(json, model)

        private sealed class BeamModel : IDisposable
        {
            public BeamModel()
            {
                Model = NewModel();
                using (ITransaction txn = Model.BeginTransaction("setup"))
                {
                    var c = new SyntheticModels.Builder(Model, 1);
                    IfcLocalPlacement placement = c.Placement(null, 1, 2, 3);
                    Beam = c.New<IfcBeam>(b => { b.GlobalId = c.NextGlobalId(); b.Name = "B"; b.ObjectPlacement = placement; });
                    UnplacedBeam = c.New<IfcBeam>(b => { b.GlobalId = c.NextGlobalId(); b.Name = "Unplaced"; });
                    Location = ((IfcAxis2Placement3D)placement.RelativePlacement).Location;
                    txn.Commit();
                }
            }

            public IfcStore Model { get; }
            public IfcBeam Beam { get; }
            public IfcBeam UnplacedBeam { get; }
            public IfcCartesianPoint Location { get; }

            public void Dispose() => Model.Dispose();
        }

        private static bool Update(BeamModel beam, JObject transforms)
        {
            using (var temp = new TempDirectory())
            {
                string path = temp.File("transforms.json");
                SyntheticModels.WriteJson(path, transforms);
                return PlacementUpdater.UpdateIfcPosition(path, beam.Model);
            }
        }

        [Fact]
        public void UpdateIfcPosition_AppliesTransformToProductByEntityLabel()
        {
            using (var beam = new BeamModel())
            {
                bool changed = Update(beam, SyntheticModels.Transforms(
                    SyntheticModels.TransformEntry(beam.Beam.EntityLabel, SyntheticModels.Matrix(100, 200, 300))));
                Assert.True(changed);
                AssertPoint(beam.Location, 101, 202, 303);
            }
        }

        [Fact]
        public void UpdateIfcPosition_SkipsEntriesWithoutIdOrTransformation()
        {
            using (var beam = new BeamModel())
            {
                bool changed = Update(beam, SyntheticModels.Transforms(
                    SyntheticModels.TransformEntry(null, SyntheticModels.Matrix(100, 0, 0)),
                    SyntheticModels.TransformEntry(beam.Beam.EntityLabel, null)));
                Assert.False(changed);
                AssertPoint(beam.Location, 1, 2, 3);
            }
        }

        [Fact]
        public void UpdateIfcPosition_IgnoresLabelsThatAreNotPlacedProducts()
        {
            using (var beam = new BeamModel())
            {
                bool changed = Update(beam, SyntheticModels.Transforms(
                    SyntheticModels.TransformEntry(beam.Location.EntityLabel, SyntheticModels.Matrix(100, 0, 0)),
                    SyntheticModels.TransformEntry(beam.UnplacedBeam.EntityLabel, SyntheticModels.Matrix(100, 0, 0))));
                Assert.False(changed);
                AssertPoint(beam.Location, 1, 2, 3);
            }
        }

        [Fact]
        public void UpdateIfcPosition_UnknownLabel_IsIgnored()
        {
            using (var beam = new BeamModel())
            {
                bool changed = Update(beam, SyntheticModels.Transforms(
                    SyntheticModels.TransformEntry(987654, SyntheticModels.Matrix(100, 0, 0))));
                Assert.False(changed);
            }
        }

        [Fact]
        public void Q5_UpdateIfcPosition_WithoutModifiedProducts_ThrowsNullReference()
        {
            using (var beam = new BeamModel())
                Assert.Throws<NullReferenceException>(() => Update(beam, new JObject()));
        }
    }
}
