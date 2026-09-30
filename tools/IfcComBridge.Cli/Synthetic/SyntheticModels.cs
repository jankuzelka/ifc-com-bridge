using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.ExternalReferenceResource;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MaterialResource;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.PresentationAppearanceResource;
using Xbim.Ifc4.PresentationOrganizationResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.ProfileResource;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.TopologyResource;
using Xbim.Ifc4.UtilityResource;
using Xbim.IO;

namespace IfcComBridge.Cli.Synthetic
{
    /// <summary>
    /// Small IFC4 models built entirely in code, plus the JSON inputs that belong to them (committed as
    /// tests/fixtures/synthetic/*.json and embedded in this assembly). Nothing here is derived from any
    /// real or private model; names, dimensions and identifiers are invented. Shared by the tests and by
    /// the 'synthetic' command, so both always use the same inputs.
    ///
    /// Units are millimetres. Catalogue products are boxes whose LENGTH runs along local X, width along
    /// Y and height along Z, which is what the library's length scaling assumes.
    /// </summary>
    public static class SyntheticModels
    {
        public const string BuildingStorey = "Level 0";

        public static readonly Guid BeamA = Id(1);
        public static readonly Guid Cat1000 = Id(2);
        public static readonly Guid Cat1200 = Id(3);
        public static readonly Guid Cat1500 = Id(4);
        public static readonly Guid ExtrudedB = Id(5);
        public static readonly Guid WallW = Id(6);

        public static XbimEditorCredentials Editor => new XbimEditorCredentials
        {
            ApplicationDevelopersName = "IfcComBridge test fixtures",
            ApplicationFullName = "IfcComBridge synthetic fixture writer",
            ApplicationIdentifier = "IfcComBridge.Tests",
            ApplicationVersion = "1.0",
            EditorsFamilyName = "Fixture",
            EditorsGivenName = "Synthetic",
            EditorsOrganisationName = "IfcComBridge test fixtures",
        };

        /// <summary>Deterministic GUIDs so the products map can refer to products by GlobalId.</summary>
        public static Guid Id(int n) => new Guid($"00000000-0000-4000-8000-{n:D12}");

        public static string Gid(Guid id) => ((IfcGloballyUniqueId)id).ToString();

        // ------------------------------------------------------------------ files

        public sealed class ComposeInputs
        {
            public string Building { get; set; }
            public string Products { get; set; }
            public string ProductsMap { get; set; }
            public string Layout { get; set; }
        }

        public static ComposeInputs WriteComposeInputs(string directory, JArray layout = null, bool duplicateProductGlobalId = false)
        {
            var inputs = new ComposeInputs
            {
                Building = Path.Combine(directory, "building.ifc"),
                Products = Path.Combine(directory, "products.ifc"),
                ProductsMap = Path.Combine(directory, "products-map.json"),
                Layout = Path.Combine(directory, "layout.json"),
            };
            WriteBuildingModel(inputs.Building);
            WriteProductsModel(inputs.Products, duplicateProductGlobalId);
            SyntheticFixtures.CopyTo(SyntheticFixtures.ProductsMapFile, inputs.ProductsMap);
            if (layout == null)
                SyntheticFixtures.CopyTo(SyntheticFixtures.LayoutFile, inputs.Layout);
            else
                WriteJson(inputs.Layout, layout);
            return inputs;
        }

        public static void WriteJson(string path, JToken json) =>
            File.WriteAllText(path, json.ToString(Formatting.Indented), new UTF8Encoding(false));

        // ------------------------------------------------------------------ models

        /// <summary>Building: project/site/building/storey "Level 0" and one slab. Units: mm and m².</summary>
        public static void WriteBuildingModel(string path)
        {
            using (IfcStore model = IfcStore.Create(Editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (ITransaction txn = model.BeginTransaction("Synthetic building"))
                {
                    var c = new Builder(model, 100);
                    Spatial s = c.CreateProject("Synthetic Building", BuildingStorey, IfcUnitEnum.AREAUNIT, IfcSIUnitName.SQUARE_METRE);
                    var (shape, _) = c.Shape(s.Context, c.ExtrudedRectangle(6000, 6000, 200), "SweptSolid");
                    var slab = c.New<IfcSlab>(x =>
                    {
                        x.GlobalId = c.NextGlobalId();
                        x.Name = "Floor";
                        x.PredefinedType = IfcSlabTypeEnum.FLOOR;
                        x.ObjectPlacement = c.Placement(s.Storey.ObjectPlacement, 0, 0, -200);
                        x.Representation = shape;
                    });
                    c.New<IfcRelContainedInSpatialStructure>(r =>
                    {
                        r.GlobalId = c.NextGlobalId();
                        r.RelatingStructure = s.Storey;
                        r.RelatedElements.Add(slab);
                    });
                    txn.Commit();
                }
                model.SaveAs(path);
            }
        }

        /// <summary>
        /// Product catalogue: BEAM_A (box 1000), CAT_1000/1200/1500 (boxes), EXTRUDED_B (arbitrary-profile
        /// extrusion 800) and WALL_W (IfcWallStandardCase). Beams share a type, classification and style;
        /// all products share a property set, material and presentation layer. Units: mm and m³.
        /// </summary>
        public static void WriteProductsModel(string path, bool duplicateGlobalId = false)
        {
            using (IfcStore model = IfcStore.Create(Editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (ITransaction txn = model.BeginTransaction("Synthetic products"))
                {
                    var c = new Builder(model, 200);
                    Spatial s = c.CreateProject("Synthetic Products", "Catalogue", IfcUnitEnum.VOLUMEUNIT, IfcSIUnitName.CUBIC_METRE);

                    var beamType = c.New<IfcBeamType>(t =>
                    {
                        t.GlobalId = c.NextGlobalId();
                        t.Name = "SyntheticBeamType";
                        t.PredefinedType = IfcBeamTypeEnum.BEAM;
                    });
                    var material = c.New<IfcMaterial>(m => m.Name = "SyntheticTimber");
                    var classification = c.New<IfcClassification>(x => x.Name = "SynthClass");
                    var classReference = c.New<IfcClassificationReference>(r =>
                    {
                        r.Identification = "SC-01";
                        r.Name = "Synthetic beams";
                        r.ReferencedSource = classification;
                    });
                    var red = c.New<IfcSurfaceStyle>(st =>
                    {
                        st.Name = "SyntheticRed";
                        st.Side = IfcSurfaceSide.BOTH;
                        st.Styles.Add(c.New<IfcSurfaceStyleRendering>(r =>
                        {
                            r.SurfaceColour = c.New<IfcColourRgb>(col => { col.Red = 1.0; col.Green = 0.0; col.Blue = 0.0; });
                            r.ReflectanceMethod = IfcReflectanceMethodEnum.NOTDEFINED;
                        }));
                    });
                    var layer = c.New<IfcPresentationLayerAssignment>(l => l.Name = "SyntheticLayer");
                    var pset = c.New<IfcPropertySet>(ps =>
                    {
                        ps.GlobalId = c.NextGlobalId();
                        ps.Name = "Pset_Synthetic";
                        ps.HasProperties.Add(c.New<IfcPropertySingleValue>(p => { p.Name = "Grade"; p.NominalValue = new IfcLabel("A"); }));
                    });

                    IfcBeam Beam(Guid id, string name, IfcRepresentationItem item, string representationType, bool styled)
                    {
                        var (shape, body) = c.Shape(s.Context, item, representationType);
                        layer.AssignedItems.Add(body);
                        if (styled)
                            c.New<IfcStyledItem>(si => { si.Item = item; si.Styles.Add(red); });
                        return c.New<IfcBeam>(b =>
                        {
                            b.GlobalId = id;
                            b.Name = name;
                            b.ObjectPlacement = c.Placement(null, 0, 0, 0);
                            b.Representation = shape;
                        });
                    }

                    var beams = new List<IfcBeam>
                    {
                        Beam(BeamA, "BEAM_A", c.Box(1000, 100, 50), "Brep", styled: true),
                        Beam(Cat1000, "CAT_1000", c.Box(1000, 100, 50), "Brep", styled: true),
                        Beam(Cat1200, "CAT_1200", c.Box(1200, 100, 50), "Brep", styled: true),
                        Beam(Cat1500, "CAT_1500", c.Box(1500, 100, 50), "Brep", styled: true),
                        Beam(duplicateGlobalId ? BeamA : ExtrudedB, "EXTRUDED_B", c.ExtrudedRectangle(800, 100, 50), "SweptSolid", styled: false),
                    };

                    var (wallShape, wallBody) = c.Shape(s.Context, c.ExtrudedRectangle(2000, 200, 2500), "SweptSolid");
                    layer.AssignedItems.Add(wallBody);
                    var wall = c.New<IfcWallStandardCase>(w =>
                    {
                        w.GlobalId = WallW;
                        w.Name = "WALL_W";
                        w.ObjectPlacement = c.Placement(null, 0, 0, 0);
                        w.Representation = wallShape;
                    });
                    List<IfcProduct> all = beams.Cast<IfcProduct>().Concat(new IfcProduct[] { wall }).ToList();

                    c.New<IfcRelDefinesByType>(r => { r.GlobalId = c.NextGlobalId(); r.RelatingType = beamType; r.RelatedObjects.AddRange(beams); });
                    c.New<IfcRelDefinesByProperties>(r => { r.GlobalId = c.NextGlobalId(); r.RelatingPropertyDefinition = pset; r.RelatedObjects.AddRange(all); });
                    c.New<IfcRelAssociatesMaterial>(r => { r.GlobalId = c.NextGlobalId(); r.RelatingMaterial = material; r.RelatedObjects.AddRange(all); });
                    c.New<IfcRelAssociatesClassification>(r => { r.GlobalId = c.NextGlobalId(); r.RelatingClassification = classReference; r.RelatedObjects.AddRange(beams); });
                    c.New<IfcRelContainedInSpatialStructure>(r => { r.GlobalId = c.NextGlobalId(); r.RelatingStructure = s.Storey; r.RelatedElements.AddRange(all); });
                    txn.Commit();
                }
                model.SaveAs(path);
            }
        }

        public static void WriteIfc2x3Model(string path)
        {
            using (IfcStore model = IfcStore.Create(Editor, XbimSchemaVersion.Ifc2X3, XbimStoreType.InMemoryModel))
            {
                using (ITransaction txn = model.BeginTransaction("Synthetic IFC2x3"))
                {
                    model.Instances.New<Xbim.Ifc2x3.Kernel.IfcProject>(p => p.Name = "Synthetic IFC2x3 project");
                    txn.Commit();
                }
                model.SaveAs(path);
            }
        }

        // ------------------------------------------------------------------ JSON inputs

        /// <summary>
        /// Products map (tests/fixtures/synthetic/products-map.json). Key order matters: the library's
        /// prefix fallback takes the FIRST key a name starts with, so "CAT-1100" resolves to "CAT" and
        /// never to the later "CAT-11" (quirk Q8). Returns a fresh copy on every call.
        /// </summary>
        public static JObject ProductsMap() => (JObject)SyntheticFixtures.Read(SyntheticFixtures.ProductsMapFile);

        /// <summary>
        /// Layout (tests/fixtures/synthetic/layout.json): G1 exercises every placement path, G2 resolves
        /// nothing (skipped, quirk Q6), G3 has a single product. Returns a fresh copy on every call.
        /// </summary>
        public static JArray Layout() => (JArray)SyntheticFixtures.Read(SyntheticFixtures.LayoutFile);

        public static JObject Group(string groupId, params JObject[] sets) =>
            new JObject { ["group_id"] = groupId, ["sets"] = new JArray(sets) };

        public static JObject Set(string setId, string id, string name, double angle, JArray vertices, double tx, double ty, JArray clip = null)
        {
            var set = new JObject
            {
                ["set_id"] = setId,
                ["id"] = id,
                ["name"] = name,
                ["rotation"] = new JObject { ["angle"] = angle },
                ["vertices"] = vertices,
                ["translate"] = new JObject { ["x"] = tx, ["y"] = ty },
            };
            if (clip != null)
                set["clipvertices"] = clip;
            return set;
        }

        public static JArray Rect(double x0, double y0, double x1, double y1) =>
            new JArray(new JArray(x0, y0), new JArray(x1, y0), new JArray(x1, y1), new JArray(x0, y1));

        public static JObject Variant(Guid id, string name, double length, int upscale, double width = 100) => new JObject
        {
            ["ifc_guid"] = Gid(id),
            ["ifc_name"] = name,
            ["width"] = width,
            ["length"] = length,
            ["length_upscale"] = upscale,
        };

        /// <summary>
        /// Full 4x4 transform in the layout UpdateModel expects: 16 values keyed "0".."15", column-major
        /// (column-vector convention), translation in 12..14. Rotation is about +Z, counter-clockwise.
        /// </summary>
        public static JObject Matrix(double tx, double ty, double tz, double rotationZDegrees = 0)
        {
            double r = rotationZDegrees * Math.PI / 180;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            double[] m = { cos, sin, 0, 0, -sin, cos, 0, 0, 0, 0, 1, 0, tx, ty, tz, 1 };
            var json = new JObject();
            for (int i = 0; i < 16; i++)
                json[i.ToString()] = m[i];
            return json;
        }

        public static JObject Transforms(params JObject[] entries) => new JObject { ["modified_products"] = new JArray(entries) };

        public static JObject TransformEntry(int? id, JObject matrix)
        {
            var entry = new JObject();
            if (id.HasValue)
                entry["id"] = id.Value;
            if (matrix != null)
                entry["transformation"] = matrix;
            return entry;
        }

        // ------------------------------------------------------------------ builder

        public sealed class Spatial
        {
            public IfcProject Project { get; set; }
            public IfcBuildingStorey Storey { get; set; }
            public IfcGeometricRepresentationContext Context { get; set; }
        }

        public sealed class Builder
        {
            private int _nextId;

            public Builder(IModel model, int firstId)
            {
                Model = model;
                _nextId = firstId;
            }

            public IModel Model { get; }

            public IfcGloballyUniqueId NextGlobalId() => Id(_nextId++);

            public T New<T>(Action<T> init) where T : IInstantiableEntity => Model.Instances.New(init);

            public IfcCartesianPoint Point(double x, double y, double z) => New<IfcCartesianPoint>(p => p.SetXYZ(x, y, z));

            public IfcCartesianPoint Point2D(double x, double y) => New<IfcCartesianPoint>(p => p.SetXY(x, y));

            public IfcDirection Direction(double x, double y, double z) => New<IfcDirection>(d => d.SetXYZ(x, y, z));

            public IfcAxis2Placement3D Axis3D(double x, double y, double z) => New<IfcAxis2Placement3D>(a =>
            {
                a.Location = Point(x, y, z);
                a.Axis = Direction(0, 0, 1);
                a.RefDirection = Direction(1, 0, 0);
            });

            public IfcLocalPlacement Placement(IfcObjectPlacement relativeTo, double x, double y, double z) => New<IfcLocalPlacement>(lp =>
            {
                lp.PlacementRelTo = relativeTo;
                lp.RelativePlacement = Axis3D(x, y, z);
            });

            public Spatial CreateProject(string projectName, string storeyName, IfcUnitEnum secondUnitType, IfcSIUnitName secondUnitName)
            {
                var context = New<IfcGeometricRepresentationContext>(g =>
                {
                    g.ContextType = "Model";
                    g.CoordinateSpaceDimension = 3;
                    g.Precision = 1e-5;
                    g.WorldCoordinateSystem = Axis3D(0, 0, 0);
                });
                var units = New<IfcUnitAssignment>(u =>
                {
                    u.Units.Add(New<IfcSIUnit>(si => { si.UnitType = IfcUnitEnum.LENGTHUNIT; si.Prefix = IfcSIPrefix.MILLI; si.Name = IfcSIUnitName.METRE; }));
                    u.Units.Add(New<IfcSIUnit>(si => { si.UnitType = secondUnitType; si.Name = secondUnitName; }));
                });
                var project = New<IfcProject>(p =>
                {
                    p.GlobalId = NextGlobalId();
                    p.Name = projectName;
                    p.UnitsInContext = units;
                    p.RepresentationContexts.Add(context);
                });
                var site = New<IfcSite>(x =>
                {
                    x.GlobalId = NextGlobalId();
                    x.Name = "Site";
                    x.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    x.ObjectPlacement = Placement(null, 0, 0, 0);
                });
                var building = New<IfcBuilding>(x =>
                {
                    x.GlobalId = NextGlobalId();
                    x.Name = "Building";
                    x.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    x.ObjectPlacement = Placement(site.ObjectPlacement, 0, 0, 0);
                });
                var storey = New<IfcBuildingStorey>(x =>
                {
                    x.GlobalId = NextGlobalId();
                    x.Name = storeyName;
                    x.CompositionType = IfcElementCompositionEnum.ELEMENT;
                    x.Elevation = 0.0;
                    x.ObjectPlacement = Placement(building.ObjectPlacement, 0, 0, 0);
                });
                Aggregate(project, site);
                Aggregate(site, building);
                Aggregate(building, storey);
                return new Spatial { Project = project, Storey = storey, Context = context };
            }

            private void Aggregate(IfcObjectDefinition parent, IfcObjectDefinition child) => New<IfcRelAggregates>(r =>
            {
                r.GlobalId = NextGlobalId();
                r.RelatingObject = parent;
                r.RelatedObjects.Add(child);
            });

            /// <summary>Faceted B-rep box from the origin: X 0..lx (length), Y 0..ly, Z 0..lz; 8 shared corner points.</summary>
            public IfcFacetedBrep Box(double lx, double ly, double lz)
            {
                IfcCartesianPoint[] p =
                {
                    Point(0, 0, 0), Point(lx, 0, 0), Point(lx, ly, 0), Point(0, ly, 0),
                    Point(0, 0, lz), Point(lx, 0, lz), Point(lx, ly, lz), Point(0, ly, lz),
                };
                int[][] faces =
                {
                    new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
                    new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 },
                };
                var shell = New<IfcClosedShell>(sh =>
                {
                    foreach (int[] face in faces)
                    {
                        var loop = New<IfcPolyLoop>(l => l.Polygon.AddRange(face.Select(i => p[i])));
                        var bound = New<IfcFaceOuterBound>(b => { b.Bound = loop; b.Orientation = true; });
                        sh.CfsFaces.Add(New<IfcFace>(f => f.Bounds.Add(bound)));
                    }
                });
                return New<IfcFacetedBrep>(b => b.Outer = shell);
            }

            /// <summary>Rectangle profile (closed 2-D polyline, first point repeated) extruded along +Z.</summary>
            public IfcExtrudedAreaSolid ExtrudedRectangle(double lx, double ly, double depth)
            {
                IfcCartesianPoint first = Point2D(0, 0);
                var polyline = New<IfcPolyline>(pl => pl.Points.AddRange(new[] { first, Point2D(lx, 0), Point2D(lx, ly), Point2D(0, ly), first }));
                var profile = New<IfcArbitraryClosedProfileDef>(pd => { pd.ProfileType = IfcProfileTypeEnum.AREA; pd.OuterCurve = polyline; });
                return New<IfcExtrudedAreaSolid>(sd =>
                {
                    sd.SweptArea = profile;
                    sd.Position = Axis3D(0, 0, 0);
                    sd.ExtrudedDirection = Direction(0, 0, 1);
                    sd.Depth = depth;
                });
            }

            public (IfcProductDefinitionShape Shape, IfcShapeRepresentation Body) Shape(
                IfcGeometricRepresentationContext context, IfcRepresentationItem item, string representationType)
            {
                var body = New<IfcShapeRepresentation>(r =>
                {
                    r.ContextOfItems = context;
                    r.RepresentationIdentifier = "Body";
                    r.RepresentationType = representationType;
                    r.Items.Add(item);
                });
                var shape = New<IfcProductDefinitionShape>(pds => pds.Representations.Add(body));
                return (shape, body);
            }
        }
    }
}
