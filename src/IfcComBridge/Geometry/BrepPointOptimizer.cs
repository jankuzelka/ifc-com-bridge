using System.Collections.Generic;
using System.Linq;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcComBridge.Geometry
{
    /// <summary>
    /// Merges Cartesian points with identical coordinates in poly-loops and polylines
    /// (ProductLayoutComposer.CurrentOptimization = Size, the default).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs on the whole composed model, building copy included, and makes the output smaller: every copy
    /// of a scaled product brings its own vertices, and many of them coincide. Coordinates are compared
    /// exactly (ExactPoint3Comparer).
    /// </para>
    /// <para>
    /// Only references from poly-loops and polylines are redirected, to the point enumerated last for each
    /// coordinate triple; the replaced points are deleted. That kept point can also be used elsewhere: in
    /// the synthetic composition, B-rep vertices share their point with the Location of an extrusion's
    /// Position. If a product placement's Location were the kept point, editing it (UpdateModel, Q7) would
    /// move those vertices too. That case is not characterized (docs/quirks.md).
    /// </para>
    /// </remarks>
    internal static class BrepPointOptimizer
    {
        internal static void OptimizeBrepPoints(IfcStore aTargetModel)
        {
            using (var txn_1902417729 = aTargetModel.BeginTransaction("Optimize Brep points"))
            {
                var pointList = aTargetModel.Instances.OfType<IIfcCartesianPoint>();

                if (pointList.Count() < 1)
                    return;

                var comparer = new ExactPoint3Comparer();
                var toDeleteSet = new HashSet<IIfcCartesianPoint>();
                var pointDict = new Dictionary<double[], IIfcCartesianPoint>(comparer);

                // The last point enumerated for a coordinate triple is the one that is kept.
                foreach (var point in pointList)
                {
                    double[] d = {point.X, point.Y, point.Z};
                    pointDict[d] = point;
                }

                var polyloopList = aTargetModel.Instances.OfType<IIfcPolyLoop>();
                foreach(var polyLoop in polyloopList)
                    for (var i = 0; i < polyLoop.Polygon.Count(); i++)
                    {
                        var point = polyLoop.Polygon[i];
                        double[] d = {point.X, point.Y, point.Z};

                        if (pointDict.TryGetValue(d, out IIfcCartesianPoint pdPoint) && (pdPoint != point))
                        {
                            polyLoop.Polygon.RemoveAt(i);
                            polyLoop.Polygon.Insert(i, pdPoint);
                            toDeleteSet.Add(point);
                        }
                    }

                var polylineList = aTargetModel.Instances.OfType<IIfcPolyline>();
                foreach(var polyline in polylineList)
                    for (var i = 0; i < polyline.Points.Count(); i++)
                    {
                        var point = polyline.Points[i];
                        double[] d = {point.X, point.Y, point.Z};

                        if (pointDict.TryGetValue(d, out IIfcCartesianPoint pdPoint) && (pdPoint != point))
                        {
                            polyline.Points.RemoveAt(i);
                            polyline.Points.Insert(i, pdPoint);
                            toDeleteSet.Add(point);
                        }
                    }

                foreach (var point in toDeleteSet)
                    aTargetModel.Delete(point);

                txn_1902417729.Commit();
            }
        }
    }
}
