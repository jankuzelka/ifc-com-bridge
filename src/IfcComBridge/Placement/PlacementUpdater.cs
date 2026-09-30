using IfcComBridge.Infrastructure;
using IfcComBridge.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;

namespace IfcComBridge.Placement
{
    /// <summary>
    /// UpdateModel: applies 4x4 transformations from a JSON file to product placements, by entity label.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matrix convention: the values "0" to "15" fill XbimMatrix3D(M11, M12, ..., M44) in that order, and
    /// xBIM transforms points as row vectors (p' = p * M), so the translation is "12", "13", "14". This is
    /// the memory layout of a column-major matrix in the column-vector convention (WebGL, three.js).
    /// </para>
    /// <para>
    /// Frame: the placement's own Location, Axis and RefDirection are transformed, i.e. in the frame of its
    /// PlacementRelTo, not in world coordinates. The point and direction entities are edited in place, so
    /// an entity that several placements share moves all of them (Q7).
    /// </para>
    /// </remarks>
    internal static class PlacementUpdater
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _logger = LoggingSetup.CreateLogger(nameof(PlacementUpdater));

        internal static void SetIfcPosition(JToken aTransformation, IIfcAxis2Placement3D aPlacement)
        {
            //atransformation contains 4x4 matrix ordered by columns
            // (see the class remarks for how that maps onto xBIM's row-vector convention).
            XbimMatrix3D matrix3D = new XbimMatrix3D(
                aTransformation["0"]?.Value<double>() ?? 1, aTransformation["1"]?.Value<double>() ?? 0, aTransformation["2"]?.Value<double>() ?? 0, aTransformation["3"]?.Value<double>() ?? 0,
                aTransformation["4"]?.Value<double>() ?? 0, aTransformation["5"]?.Value<double>() ?? 1, aTransformation["6"]?.Value<double>() ?? 0, aTransformation["7"]?.Value<double>() ?? 0,
                aTransformation["8"]?.Value<double>() ?? 0, aTransformation["9"]?.Value<double>() ?? 0, aTransformation["10"]?.Value<double>() ?? 1, aTransformation["11"]?.Value<double>() ?? 0,
                aTransformation["12"]?.Value<double>() ?? 0, aTransformation["13"]?.Value<double>() ?? 0, aTransformation["14"]?.Value<double>() ?? 0, aTransformation["15"]?.Value<double>() ?? 1);
            // Q17: the directions use the rotation part only, and here a missing value defaults to 0, not to
            // the identity as above, so a partial matrix collapses Axis and RefDirection. The transformed
            // directions are not normalized or orthogonalized.
            XbimMatrix3D rotMatrix3D = new XbimMatrix3D(
				aTransformation["0"]?.Value<double>() ?? 0, aTransformation["1"]?.Value<double>() ?? 0, aTransformation["2"]?.Value<double>() ?? 0, 0,
				aTransformation["4"]?.Value<double>() ?? 0, aTransformation["5"]?.Value<double>() ?? 0, aTransformation["6"]?.Value<double>() ?? 0, 0,
				aTransformation["8"]?.Value<double>() ?? 0, aTransformation["9"]?.Value<double>() ?? 0, aTransformation["10"]?.Value<double>() ?? 0, 0,
				aTransformation["12"]?.Value<double>() ?? 0, aTransformation["13"]?.Value<double>() ?? 0, aTransformation["14"]?.Value<double>() ?? 0, 1);

			// Every model is IFC4, so the placement is always the IFC4 class.
			// Q7: a placement without Axis or RefDirection throws NullReferenceException below.
			IfcAxis2Placement3D newPlacement = aPlacement as IfcAxis2Placement3D;

            XbimPoint3D newLocation = matrix3D.Transform(newPlacement.Location.XbimPoint3D());
            XbimVector3D newAxis = rotMatrix3D.Transform(newPlacement.Axis.XbimVector3D());
            XbimVector3D newRefDir = rotMatrix3D.Transform(newPlacement.RefDirection.XbimVector3D());
            newPlacement.Location.SetXYZ(newLocation.X, newLocation.Y, newLocation.Z);
            newPlacement.Axis.SetXYZ(newAxis.X, newAxis.Y, newAxis.Z);
            newPlacement.RefDirection.SetXYZ(newRefDir.X, newRefDir.Y, newRefDir.Z);
        }

        internal static bool UpdateIfcPosition(string aJsonFile, IfcStore aBuildingModel)
        {
            JToken jsonValues = ModelFiles.LoadJson(aJsonFile, "JSON data");
            var positionChanged = false;
            // One transaction for all entries: an exception rolls back every entry of this call.
            using (ITransaction txn1 = aBuildingModel.BeginTransaction("Change position of IfcProduct"))
            {
                // Q5: a file without "modified_products" throws NullReferenceException.
                foreach (var item in jsonValues["modified_products"])
                {
                    var id = item["id"]?.Value<int>();
                    if (id == null)
                    {
                        _logger.LogInformation($"ID not defined, omitting...");
                        continue;
                    }

                    var transformation = item["transformation"]?.Value<JToken>();
                    if (transformation == null)
                    {
                        _logger.LogInformation($"Transformation not defined, omitting...");
                        continue;
                    }

                    _logger.LogInformation($"ID {id} found. Updating its position by given transformation...");

                    // id is the STEP entity label (#id) in this model: the file's labels after LoadIfc, the
                    // in-memory labels of a composed model (which a saved copy keeps). Labels that are not
                    // products with a 3-D local placement, including unknown ones, are skipped silently.
                    var product = aBuildingModel.Instances[(int)id] as IIfcProduct;
                    if ((product != null) &&
                        (product.ObjectPlacement is IIfcLocalPlacement localPlacement) &&
                        (localPlacement.RelativePlacement is IIfcAxis2Placement3D placement3D))
                    {
                        SetIfcPosition(transformation, placement3D);
                        positionChanged = true;
                    }
                }
                txn1.Commit();
            }

            return positionChanged;
        }
    }
}
