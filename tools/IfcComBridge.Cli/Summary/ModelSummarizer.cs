using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcComBridge.Cli.Summary
{
    /// <summary>
    /// Produces a deterministic, comparison-friendly JSON description of an IFC model (and optionally a
    /// WexBIM file) for characterization and regression tests.
    ///
    /// Deliberately excluded, because they legitimately differ between two runs of identical code:
    /// file paths, header timestamps and GlobalId values (new entities get random GUIDs). Entity labels
    /// are included: they are deterministic for a given input and are what UpdateModel addresses.
    ///
    /// Identity strings (header author/organization, IfcApplication/IfcOrganization/IfcPerson) are only
    /// included on request because they may carry private names.
    /// </summary>
    public static class ModelSummarizer
    {
        /// <summary>Bump when the summary layout changes; baselines of another format are not comparable.</summary>
        public const int FormatVersion = 1;

        private const int Decimals = 6;

        public static JObject Summarize(string ifcPath, string wexbimPath = null, bool includeIdentity = false)
        {
            var summary = new JObject { ["summaryFormat"] = FormatVersion };

            // -1 = always an in-memory model, exactly like the library's LoadIfcModel. This also guarantees
            // that no database/temp files are created next to (private) input files.
            using (IfcStore model = IfcStore.Open(ifcPath, null, -1))
            {
                summary["schema"] = model.SchemaVersion.ToString();
                summary["entityCounts"] = EntityCounts(model);

                HashSet<string> sharedIds;
                summary["globalIds"] = GlobalIdStatistics(model, out sharedIds);
                summary["units"] = Units(model);
                summary["representationContexts"] = RepresentationContexts(model);
                summary["spatialStructure"] = SpatialStructure(model);
                summary["products"] = Products(model, sharedIds);
                summary["geometry"] = GeometryStatistics(model);
                if (includeIdentity)
                    summary["identity"] = Identity(model);
            }

            if (wexbimPath != null)
                summary["wexbim"] = WexbimHeaderReader.Read(wexbimPath);

            // Round-trip through text so an in-memory summary has exactly the structure of one read back
            // from a file (e.g. null strings become JSON nulls).
            return JObject.Parse(summary.ToString(Formatting.None));
        }

        public static string ToJsonText(JToken summary) =>
            summary.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";

        private static JObject EntityCounts(IModel model)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (IPersistEntity entity in model.Instances)
            {
                string type = entity.GetType().Name;
                counts[type] = counts.TryGetValue(type, out int n) ? n + 1 : 1;
            }

            var result = new JObject();
            foreach (KeyValuePair<string, int> pair in counts)
                result[pair.Key] = pair.Value;
            return result;
        }

        private static JObject GlobalIdStatistics(IModel model, out HashSet<string> sharedIds)
        {
            List<string> ids = model.Instances.OfType<IIfcRoot>().Select(r => r.GlobalId.ToString()).ToList();
            var duplicated = ids.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
            sharedIds = new HashSet<string>(duplicated.Select(g => g.Key), StringComparer.Ordinal);
            return new JObject
            {
                ["rootEntities"] = ids.Count,
                ["distinct"] = ids.Distinct(StringComparer.Ordinal).Count(),
                ["duplicatedIds"] = duplicated.Count,
                ["entitiesSharingAnId"] = duplicated.Sum(g => g.Count()),
            };
        }

        private static JArray Units(IModel model)
        {
            var units = new List<string>();
            foreach (IIfcProject project in model.Instances.OfType<IIfcProject>())
            {
                if (project.UnitsInContext == null)
                {
                    units.Add("<project without UnitsInContext>");
                    continue;
                }

                foreach (IIfcUnit unit in project.UnitsInContext.Units)
                    units.Add(DescribeUnit(unit));
            }

            units.Sort(StringComparer.Ordinal);
            return new JArray(units);
        }

        private static string DescribeUnit(IIfcUnit unit)
        {
            switch (unit)
            {
                case IIfcSIUnit si:
                    return $"{si.UnitType}: {(si.Prefix.HasValue ? si.Prefix.Value.ToString() : string.Empty)}{si.Name}";
                case IIfcConversionBasedUnit conversion:
                    return $"{conversion.UnitType}: {conversion.Name} (conversion-based)";
                case IIfcContextDependentUnit contextDependent:
                    return $"{contextDependent.UnitType}: {contextDependent.Name} (context-dependent)";
                case IIfcDerivedUnit derived:
                    return $"{derived.UnitType}: derived";
                case IIfcMonetaryUnit monetary:
                    return $"MONETARY: {monetary.Currency}";
                default:
                    return unit.GetType().Name;
            }
        }

        private static JArray RepresentationContexts(IModel model)
        {
            var attached = new HashSet<int>(model.Instances.OfType<IIfcContext>()
                .SelectMany(c => c.RepresentationContexts).Select(c => c.EntityLabel));

            return new JArray(model.Instances.OfType<IIfcRepresentationContext>()
                .OrderBy(c => c.EntityLabel)
                .Select(c => new JObject
                {
                    ["label"] = c.EntityLabel,
                    ["type"] = c.GetType().Name,
                    ["identifier"] = Text(c.ContextIdentifier),
                    ["contextType"] = Text(c.ContextType),
                    ["attachedToProject"] = attached.Contains(c.EntityLabel),
                }));
        }

        private static JArray SpatialStructure(IModel model)
        {
            return new JArray(model.Instances.OfType<IIfcSpatialElement>()
                .OrderBy(s => s.EntityLabel)
                .Select(s => new JObject
                {
                    ["label"] = s.EntityLabel,
                    ["type"] = s.GetType().Name,
                    ["name"] = Text(s.Name),
                    ["elevation"] = s is IIfcBuildingStorey storey && storey.Elevation.HasValue ? Num(storey.Elevation.Value) : null,
                    ["containedElements"] = s.ContainsElements.SelectMany(r => r.RelatedElements).Count(),
                    ["placement"] = Placement(s.ObjectPlacement),
                }));
        }

        private static JArray Products(IModel model, HashSet<string> sharedIds)
        {
            return new JArray(model.Instances.OfType<IIfcProduct>()
                .Where(p => !(p is IIfcSpatialElement))
                .OrderBy(p => p.EntityLabel)
                .Select(p => new JObject
                {
                    ["label"] = p.EntityLabel,
                    ["type"] = p.GetType().Name,
                    ["name"] = Text(p.Name),
                    ["description"] = Text(p.Description),
                    ["objectType"] = Text(p.ObjectType),
                    ["globalIdShared"] = sharedIds.Contains(p.GlobalId.ToString()),
                    ["placement"] = Placement(p.ObjectPlacement),
                    ["containedIn"] = Strings((p as IIfcElement)?.ContainedInStructure.Select(r => Text(r.RelatingStructure?.Name))),
                    ["types"] = Strings(p.IsTypedBy.Select(r => Text(r.RelatingType?.Name))),
                    ["propertySets"] = PropertySets(p),
                    ["materials"] = Strings(p.HasAssociations.OfType<IIfcRelAssociatesMaterial>().Select(r => DescribeMaterial(r.RelatingMaterial))),
                    ["classifications"] = Strings(p.HasAssociations.OfType<IIfcRelAssociatesClassification>().Select(r => DescribeClassification(r.RelatingClassification))),
                    ["representations"] = Representations(p.Representation),
                }));
        }

        private static JArray PropertySets(IIfcProduct product)
        {
            var sets = new List<JObject>();
            foreach (IIfcRelDefinesByProperties rel in product.IsDefinedBy)
            {
                switch (rel.RelatingPropertyDefinition)
                {
                    case IIfcPropertySet pset:
                        sets.Add(new JObject
                        {
                            ["name"] = Text(pset.Name),
                            ["properties"] = Strings(pset.HasProperties.Select(DescribeProperty)),
                        });
                        break;
                    case IIfcElementQuantity quantities:
                        sets.Add(new JObject
                        {
                            ["name"] = Text(quantities.Name),
                            ["quantities"] = Strings(quantities.Quantities.Select(q => q.Name.ToString())),
                        });
                        break;
                    case null:
                        break;
                    default:
                        sets.Add(new JObject { ["name"] = rel.RelatingPropertyDefinition.GetType().Name });
                        break;
                }
            }

            return new JArray(sets.OrderBy(s => (string)s["name"], StringComparer.Ordinal));
        }

        private static string DescribeProperty(IIfcProperty property)
        {
            if (property is IIfcPropertySingleValue single)
                return $"{property.Name}={single.NominalValue}";
            return $"{property.Name} ({property.GetType().Name})";
        }

        private static string DescribeMaterial(IIfcMaterialSelect material)
        {
            switch (material)
            {
                case IIfcMaterial m:
                    return m.Name.ToString();
                case IIfcMaterialLayerSetUsage usage:
                    return "LayerSetUsage(" + string.Join(";", usage.ForLayerSet?.MaterialLayers.Select(l => Text(l.Material?.Name)) ?? Enumerable.Empty<string>()) + ")";
                case IIfcMaterialLayerSet set:
                    return "LayerSet(" + string.Join(";", set.MaterialLayers.Select(l => Text(l.Material?.Name))) + ")";
                case IIfcMaterialList list:
                    return "List(" + string.Join(";", list.Materials.Select(m => m.Name.ToString())) + ")";
                case null:
                    return "<null>";
                default:
                    return material.GetType().Name;
            }
        }

        private static string DescribeClassification(IIfcClassificationSelect classification)
        {
            switch (classification)
            {
                case IIfcClassificationReference reference:
                    return $"{Text(reference.Identification)}|{Text(reference.Name)}";
                case IIfcClassification system:
                    return system.Name.ToString();
                case null:
                    return "<null>";
                default:
                    return classification.GetType().Name;
            }
        }

        private static JArray Representations(IIfcProductRepresentation representation)
        {
            if (representation == null)
                return new JArray();

            return new JArray(representation.Representations.Select(r => new JObject
            {
                ["label"] = r.EntityLabel,
                ["identifier"] = Text(r.RepresentationIdentifier),
                ["type"] = Text(r.RepresentationType),
                ["contextLabel"] = r.ContextOfItems?.EntityLabel,
                ["layers"] = Strings(r.LayerAssignments.Select(l => l.Name.ToString())),
                ["items"] = new JArray(r.Items.Select(RepresentationItem)),
            }));
        }

        private static JObject RepresentationItem(IIfcRepresentationItem item)
        {
            var result = new JObject
            {
                ["label"] = item.EntityLabel,
                ["type"] = item.GetType().Name,
                ["styles"] = Strings(item.StyledByItem.SelectMany(StyleNames)),
            };

            switch (item)
            {
                case IIfcFacetedBrep brep:
                    List<IIfcCartesianPoint> points = brep.Outer.CfsFaces
                        .SelectMany(f => f.Bounds)
                        .Select(b => b.Bound)
                        .OfType<IIfcPolyLoop>()
                        .SelectMany(loop => loop.Polygon)
                        .ToList();
                    result["faces"] = brep.Outer.CfsFaces.Count();
                    result["loopPoints"] = points.Count;
                    result["distinctPointEntities"] = points.Select(p => p.EntityLabel).Distinct().Count();
                    result["bbox"] = BoundingBox(points);
                    break;

                case IIfcExtrudedAreaSolid solid:
                    result["depth"] = Num((double)solid.Depth);
                    result["extrudedDirection"] = Direction(solid.ExtrudedDirection);
                    result["position"] = Axis2Placement(solid.Position);
                    result["profile"] = Profile(solid.SweptArea);
                    break;

                case IIfcMappedItem mapped:
                    result["mappingSourceLabel"] = mapped.MappingSource?.EntityLabel;
                    break;
            }

            return result;
        }

        private static IEnumerable<string> StyleNames(IIfcStyledItem styledItem)
        {
            foreach (IIfcStyleAssignmentSelect style in styledItem.Styles)
            {
                switch (style)
                {
                    case IIfcSurfaceStyle surface:
                        yield return Text(surface.Name) ?? "<unnamed surface style>";
                        break;
                    case IIfcPresentationStyleAssignment assignment:
                        foreach (IIfcPresentationStyleSelect inner in assignment.Styles)
                            yield return inner is IIfcSurfaceStyle s ? Text(s.Name) ?? "<unnamed surface style>" : inner.GetType().Name;
                        break;
                    default:
                        yield return style.GetType().Name;
                        break;
                }
            }
        }

        private static JObject Profile(IIfcProfileDef profile)
        {
            var result = new JObject { ["type"] = profile?.GetType().Name };
            switch (profile)
            {
                case IIfcArbitraryClosedProfileDef arbitrary when arbitrary.OuterCurve is IIfcPolyline polyline:
                    result["points"] = polyline.Points.Count;
                    result["bbox"] = BoundingBox(polyline.Points);
                    break;
                case IIfcRectangleProfileDef rectangle:
                    result["xDim"] = Num((double)rectangle.XDim);
                    result["yDim"] = Num((double)rectangle.YDim);
                    break;
            }
            return result;
        }

        private static JObject GeometryStatistics(IModel model)
        {
            List<IIfcCartesianPoint> points = model.Instances.OfType<IIfcCartesianPoint>().ToList();
            return new JObject
            {
                ["cartesianPoints"] = points.Count,
                ["distinctPointCoordinates"] = points
                    .Select(p => string.Join(";", Coordinates(p).Select(c => c.ToString("R", CultureInfo.InvariantCulture))))
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
            };
        }

        private static JObject Identity(IModel model)
        {
            var fileName = model.Header?.FileName;
            return new JObject
            {
                ["headerOriginatingSystem"] = fileName?.OriginatingSystem,
                ["headerPreprocessorVersion"] = fileName?.PreprocessorVersion,
                ["headerAuthorization"] = fileName?.AuthorizationName,
                ["headerOrganizations"] = Strings(fileName?.Organization),
                ["headerAuthors"] = Strings(fileName?.AuthorName),
                ["applications"] = Strings(model.Instances.OfType<IIfcApplication>().Select(a =>
                    $"{a.ApplicationFullName}|{a.ApplicationIdentifier}|{a.Version}|{Text(a.ApplicationDeveloper?.Name)}")),
                ["organizations"] = Strings(model.Instances.OfType<IIfcOrganization>().Select(o => o.Name.ToString())),
                ["persons"] = Strings(model.Instances.OfType<IIfcPerson>().Select(p => $"{Text(p.GivenName)} {Text(p.FamilyName)}".Trim())),
            };
        }

        // ---------------------------------------------------------------- placements / geometry math

        private static JObject Placement(IIfcObjectPlacement placement)
        {
            if (placement == null)
                return null;

            var result = new JObject { ["type"] = placement.GetType().Name };
            if (placement is IIfcLocalPlacement local)
            {
                result["relative"] = Axis2Placement(local.RelativePlacement);
                int depth = 0;
                for (var parent = local.PlacementRelTo; parent != null && depth < 64; parent = (parent as IIfcLocalPlacement)?.PlacementRelTo)
                    depth++;
                result["parentDepth"] = depth;
                double[,] world = WorldMatrix(local, 0);
                result["world"] = world == null ? null : new JObject
                {
                    ["origin"] = Row(world, 3),
                    ["xAxis"] = Row(world, 0),
                    ["zAxis"] = Row(world, 2),
                };
            }
            return result;
        }

        private static JObject Axis2Placement(IIfcAxis2Placement placement)
        {
            switch (placement)
            {
                case IIfcAxis2Placement3D p3:
                    return new JObject
                    {
                        ["kind"] = "3D",
                        ["location"] = Point(p3.Location),
                        ["axis"] = Direction(p3.Axis),
                        ["refDirection"] = Direction(p3.RefDirection),
                    };
                case IIfcAxis2Placement2D p2:
                    return new JObject
                    {
                        ["kind"] = "2D",
                        ["location"] = Point(p2.Location),
                        ["refDirection"] = Direction(p2.RefDirection),
                    };
                default:
                    return null;
            }
        }

        /// <summary>
        /// World transform of a local placement chain, row-vector convention (p_world = p_local * M),
        /// computed here rather than via xBIM so the summary is independent of the library under test.
        /// </summary>
        private static double[,] WorldMatrix(IIfcObjectPlacement placement, int depth)
        {
            if (!(placement is IIfcLocalPlacement local) || depth > 64)
                return null;

            double[,] own = LocalMatrix(local.RelativePlacement);
            if (own == null)
                return null;
            if (local.PlacementRelTo == null)
                return own;

            double[,] parent = WorldMatrix(local.PlacementRelTo, depth + 1);
            return parent == null ? null : Multiply(own, parent);
        }

        private static double[,] LocalMatrix(IIfcAxis2Placement placement)
        {
            double[] origin, z, reference;
            switch (placement)
            {
                case IIfcAxis2Placement3D p3:
                    origin = Coordinates(p3.Location);
                    z = p3.Axis == null ? new[] { 0.0, 0, 1 } : new[] { p3.Axis.X, p3.Axis.Y, Finite(p3.Axis.Z) };
                    reference = p3.RefDirection == null ? new[] { 1.0, 0, 0 } : new[] { p3.RefDirection.X, p3.RefDirection.Y, Finite(p3.RefDirection.Z) };
                    break;
                case IIfcAxis2Placement2D p2:
                    origin = Coordinates(p2.Location);
                    z = new[] { 0.0, 0, 1 };
                    reference = p2.RefDirection == null ? new[] { 1.0, 0, 0 } : new[] { p2.RefDirection.X, p2.RefDirection.Y, 0 };
                    break;
                default:
                    return null;
            }

            // IFC semantics: X is the reference direction projected onto the plane normal to Z.
            z = Normalize(z);
            double dot = reference[0] * z[0] + reference[1] * z[1] + reference[2] * z[2];
            double[] x = Normalize(new[] { reference[0] - dot * z[0], reference[1] - dot * z[1], reference[2] - dot * z[2] });
            double[] y = { z[1] * x[2] - z[2] * x[1], z[2] * x[0] - z[0] * x[2], z[0] * x[1] - z[1] * x[0] };

            return new double[,]
            {
                { x[0], x[1], x[2], 0 },
                { y[0], y[1], y[2], 0 },
                { z[0], z[1], z[2], 0 },
                { Finite(origin[0]), Finite(origin[1]), Finite(origin[2]), 1 },
            };
        }

        private static double[,] Multiply(double[,] a, double[,] b)
        {
            var result = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    for (int k = 0; k < 4; k++)
                        result[i, j] += a[i, k] * b[k, j];
            return result;
        }

        private static double[] Normalize(double[] v)
        {
            double length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            // A zero vector yields NaN on purpose: invalid directions must show up in the summary.
            return new[] { v[0] / length, v[1] / length, v[2] / length };
        }

        private static double[] Coordinates(IIfcCartesianPoint point) =>
            point == null ? new[] { 0.0, 0, 0 } : new[] { point.X, point.Y, point.Z };

        // 2D points report Z as NaN in xBIM.
        private static double Finite(double value) => double.IsNaN(value) ? 0 : value;

        private static JArray Row(double[,] m, int row) => new JArray(Num(m[row, 0]), Num(m[row, 1]), Num(m[row, 2]));

        private static JArray Point(IIfcCartesianPoint point) =>
            point == null ? null : new JArray(Coordinates(point).Select(Num));

        private static JArray Direction(IIfcDirection direction) =>
            direction == null ? null : new JArray(Num(direction.X), Num(direction.Y), Num(direction.Z));

        private static JArray BoundingBox(IEnumerable<IIfcCartesianPoint> points)
        {
            List<double[]> coordinates = points.Select(Coordinates).ToList();
            if (coordinates.Count == 0)
                return null;
            var box = new JArray();
            for (int axis = 0; axis < 3; axis++)
                box.Add(Num(coordinates.Min(c => c[axis])));
            for (int axis = 0; axis < 3; axis++)
                box.Add(Num(coordinates.Max(c => c[axis])));
            return box;
        }

        // ---------------------------------------------------------------- value helpers

        /// <summary>Rounded number; NaN/infinity become strings (JSON has no such numbers).</summary>
        internal static JToken Num(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value.ToString(CultureInfo.InvariantCulture);
            double rounded = Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
            return rounded == 0 ? 0.0 : rounded; // no "-0"
        }

        private static string Text<T>(T? value) where T : struct => value.HasValue ? value.Value.ToString() : null;

        private static JArray Strings(IEnumerable<string> values) =>
            values == null ? new JArray() : new JArray(values.Select(v => v ?? "<null>").OrderBy(v => v, StringComparer.Ordinal));
    }
}
