using System.Collections.Generic;
using System.Linq;
using IfcComBridge.Geometry;
using IfcComBridge.Infrastructure;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xbim.Common;
using Xbim.Common.Metadata;
using Xbim.Ifc;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.ProfileResource;

namespace IfcComBridge.Composition
{
    /// <summary>Copies one source product into the target model with its new placement and scaled geometry.</summary>
    /// <remarks>
    /// <para>
    /// Frames and units: every layout and products-map value is a raw number in the products model's
    /// length unit (Q3), in degrees for angles. The new placement is written into the copy's
    /// IfcAxis2Placement3D, so it is relative to the copy's PlacementRelTo, which is inherited from the
    /// source product; copies are not re-parented under the building.
    /// </para>
    /// <para>
    /// The copy's placement: Axis = +Z; RefDirection, the copy's local X axis, = (cos(-angle),
    /// sin(-angle), 0); Location = (translate.x, translate.y, -center_z) + (-center_x + centerOffset,
    /// -center_y) turned by -angle. The product's point (center_x, center_y, center_z) thus lands at
    /// (translate.x, translate.y, 0), moved by centerOffset along the copy's local X.
    /// </para>
    /// <para>
    /// Length scaling stretches local X about x = center_x, for two geometry types only: IfcFacetedBrep
    /// poly-loops, and IfcExtrudedAreaSolid with an IfcArbitraryClosedProfileDef whose outer curve is an
    /// IfcPolyline. Other geometry is copied unscaled. That local X is the product's length axis is an
    /// assumption of the code, not something it checks.
    /// </para>
    /// </remarks>
    internal static class ProductCopier
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _logger = LoggingSetup.CreateLogger(nameof(ProductCopier));

        internal static IIfcProduct CopyProduct(IfcStore aTargetModel, IIfcProduct aSourceProduct, XbimInstanceHandleMap aProductMap, XbimInstanceHandleMap aStylesMap, JObject aSet, JObject aProductParams, double aLengthScaleRatio, double aCenterOffset)
        {
            // True for the first copy of this product in this map, i.e. at this length scale. Only then are
            // vertices scaled: later copies at the same scale reuse the item copies, which are already scaled.
            bool hasNewRepresentation = (aSourceProduct.Representation == null) || !aProductMap.ContainsKey(new XbimInstanceHandle(aSourceProduct.Representation));

            // Q5: a set without "rotation" throws NullReferenceException. Without "angle" it is 360, a full turn.
            double angle = aSet["rotation"]["angle"]?.Value<double>() ?? 360;
            double[] axis = { 0, 0, 1 },
                     location = { 0, 0, 0 },
                     offset = { 0, 0 };

            List<IIfcStyledItem> styledItemList = new List<IIfcStyledItem>();
            HashSet<IPersistEntity> processedEntitySet = new HashSet<IPersistEntity>();

            if (aSet.TryGetValue("translate", out JToken translate) && translate is JObject t)
            {
                location[0] = t["x"].Value<double>();
                location[1] = t["y"].Value<double>();
            }

            if (aProductParams.TryGetValue("rotation", out JToken value))
                angle += value.Value<double>();

            if (aProductParams.TryGetValue("center_z", out JToken offsetZ))
                location[2] -= offsetZ.Value<double>();

            if (aProductParams.TryGetValue("center_x", out JToken offsetX))
                offset[0] -= offsetX.Value<double>();

            if (aProductParams.TryGetValue("center_y", out JToken offsetY))
                offset[1] -= offsetY.Value<double>();

            double[] rotated = { offset[0] + aCenterOffset, offset[1] };

            // The placement turns by -angle (counter-clockwise positive, degrees), while ProductSelector turns
            // the set's vertices by +angle before measuring them. Which convention the layout's producer uses
            // is not established (docs/input-contracts.md, section 4).
            PlanarMath.RotateVector(ref rotated[0], ref rotated[1], -angle);

            var refDirection = PlanarMath.CalcRefDirection(-angle);
            var centerX = -offset[0];

            // Stretches a point list along local X about x = centerX (center_x). processedEntitySet keeps a
            // point that several loops share from being stretched twice.
            void UpdateVertexWidth(IItemSet<IIfcCartesianPoint> aPointList)
            {
                foreach (IfcCartesianPoint vertex in aPointList.Cast<IfcCartesianPoint>())
                    if (!processedEntitySet.Contains(vertex))
                    {
                        processedEntitySet.Add(vertex);
                        vertex.X = centerX + (vertex.X - centerX) * aLengthScaleRatio;
                    }
            }

            // resolves proper transformation of placement and representation
            // InsertCopy calls this for every property of every entity it copies; aParentObject is the SOURCE
            // entity. The first time an entity comes by, the delegate edits the source in place (inside the
            // caller's uncommitted transaction on the products model) and then returns the edited value, so
            // the copy gets the new placement and the scaled vertices, and the rollback restores the source.
            object ProductTransformDelegate(ExpressMetaProperty aProperty, object aParentObject)
            {
                if (aProperty.PropertyInfo.Name == nameof(IIfcProduct.OwnerHistory))
                    return null;

                if (aParentObject is IPersistEntity entity && !processedEntitySet.Contains(entity))
                {
                    processedEntitySet.Add(entity);

                    if (aParentObject is IIfcProduct product)
                    {
                        // Other placement types are copied unchanged. Axis and RefDirection must be set,
                        // otherwise this throws NullReferenceException (see Q7).
                        if (product.ObjectPlacement is IIfcLocalPlacement localPlacement && localPlacement.RelativePlacement is IfcAxis2Placement3D relativePlacement)
                        {
                            relativePlacement.Axis.SetXYZ(axis[0], axis[1], axis[2]);
                            relativePlacement.Location.SetXYZ(location[0] + rotated[0], location[1] + rotated[1], location[2]);
                            relativePlacement.RefDirection.SetXYZ(refDirection[0], refDirection[1], refDirection[2]);
                        }
                    }
                    else if (aParentObject is IIfcFacetedBrep facetedBrep)
                    {
                        // Styles are collected for B-reps only, so other geometry types lose their styles.
                        styledItemList.AddRange(facetedBrep.StyledByItem);

                        if (hasNewRepresentation && (aLengthScaleRatio != 1))
                            foreach (var outerShell in facetedBrep.Outer.CfsFaces)
                                foreach (var bounds in outerShell.Bounds)
                                    if (bounds.Bound is IIfcPolyLoop polyLoop)
                                        UpdateVertexWidth(polyLoop.Polygon);
                    }
                    else if (aParentObject is IIfcExtrudedAreaSolid s)
                    {
                        if (hasNewRepresentation && (aLengthScaleRatio != 1))
                            if (s.SweptArea is IfcArbitraryClosedProfileDef prof)
                                if (prof.OuterCurve is IIfcPolyline polyline)
                                    // Q14: the whole list is processed once per vertex. processedEntitySet makes the
                                    // repeats no-ops, so the result is right, but the work grows quadratically.
                                    foreach (IfcCartesianPoint vertex in polyline.Points)
                                        UpdateVertexWidth(polyline.Points);
                    }
                }

                return aProperty.PropertyInfo.GetValue(aParentObject);
            }

            // remove mapping of product, its placement and representation for future cloning
            // Without these entries, InsertCopy creates a new product, placement and representation for every
            // copy. The geometry items behind the representations stay mapped, so copies at one scale share
            // them. PlacementRelTo stays mapped too: all copies at one scale share one parent placement copy.
            List<IPersistEntity> mapEntityToRemoveList = new List<IPersistEntity> {aSourceProduct};

            if (aSourceProduct.ObjectPlacement is IIfcLocalPlacement sourceLocalPlacement && sourceLocalPlacement.RelativePlacement is IfcAxis2Placement3D sourceRelativePlacement)
            {
                mapEntityToRemoveList.Add(sourceLocalPlacement);
                mapEntityToRemoveList.Add(sourceRelativePlacement);
                mapEntityToRemoveList.Add(sourceRelativePlacement.Axis);
                mapEntityToRemoveList.Add(sourceRelativePlacement.Location);
                mapEntityToRemoveList.Add(sourceRelativePlacement.RefDirection);
            }

            if (aSourceProduct.Representation != null)
            {
                mapEntityToRemoveList.Add(aSourceProduct.Representation);
                mapEntityToRemoveList.AddRange(aSourceProduct.Representation.Representations);
            }

            foreach (var entity in mapEntityToRemoveList)
                if (entity != null)
                    aProductMap.Remove(new XbimInstanceHandle(entity));

            // create copy of product with given productTransformDelegate
            _logger.LogInformation($"Updating placement, representation and BREP styles...");
            // includeInverses false: relations are not followed here; ProductRelationsCopier recreates them.
            // Q13: the GlobalId is copied unchanged, so repeated copies of one product share it.
            IIfcProduct productCpy = aTargetModel.InsertCopy(aSourceProduct, aProductMap, ProductTransformDelegate, false, false);

            // A styled item points at its geometry item, the inverse direction, so copying the product did not
            // copy it. Each style is copied with Item cleared (on the source, inside the caller's uncommitted
            // transaction), so the copy does not drag the source B-rep along. The copy is then pointed at this
            // product's B-rep copy. Removing the style's own map entry gives every B-rep copy its own styled
            // item, while the style definitions behind it stay shared through aStylesMap.
            if (styledItemList.Count() > 0)
            {
                object StylesTransformDelegate(ExpressMetaProperty aProperty, object aParentObject)
                {
                    if (aParentObject is IIfcStyledItem styledItem)
                        styledItem.Item = null;

                    return aProperty.PropertyInfo.GetValue(aParentObject);
                }

                foreach (var style in styledItemList)
                {
                    var brepHandler = new XbimInstanceHandle(style.Item);
                    if (aProductMap.ContainsKey(brepHandler))
                    {
                        aStylesMap.Remove(new XbimInstanceHandle(style));

                        var styleCpy = aTargetModel.InsertCopy(style, aStylesMap, StylesTransformDelegate, false, false);
                        styleCpy.Item = aProductMap[brepHandler].GetEntity() as IIfcRepresentationItem;
                    }
                }
            }

            return productCpy;
        }
    }
}
