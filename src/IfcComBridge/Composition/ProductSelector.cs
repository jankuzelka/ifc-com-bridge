using System;
using System.Linq;
using IfcComBridge.Geometry;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Composition
{
    /// <summary>Chooses a catalogue variant (a products-map array) for a layout set and its length scale.</summary>
    /// <remarks>
    /// Lengths are raw numbers in the products model's unit, and the tolerances (20 for the width, 1 and 5
    /// for the length) are absolute values in that unit. The algorithm, step by step, is in
    /// docs/input-contracts.md, section 3.
    /// </remarks>
    internal static class ProductSelector
    {
        internal static JObject FindMostSuitableObjectToDimensions(JArray aIfcProducts, JToken aDimData, out double aLengthScaleRatio, out double aClippedOffset)
        {
            aLengthScaleRatio = 1;
            aClippedOffset = 0;

            // Q5: a set without "rotation" throws NullReferenceException.
            double angle = aDimData["rotation"]["angle"]?.Value<double>() ?? 360;

            // Measure the set's rectangle axis-aligned: the vertices are turned by +angle (ProductCopier places
            // the copy with -angle). w is the X extent, h the Y extent, and h is compared with the lengths.
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (JToken v in aDimData["vertices"])
            {
                JArray va = v as JArray;
                double x = va[0].Value<double>();
                double y = va[1].Value<double>();
                PlanarMath.RotateVector(ref x, ref y, angle);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            double h = maxY - minY;
            double w = maxX - minX;

            double clipXOffset = 0;
            double clipYOffset = 0;

            // Clipping shrinks the extents to the clip box where it lies inside them. The clip offset is half
            // of each shrink, signed; CopyProduct applies the Y offset along the copy's local X.
            JArray clip = aDimData["clipvertices"] as JArray;

            if (clip != null && clip.Count > 0)
            {
                double clipMinX = double.MaxValue;
                double clipMinY = double.MaxValue;
                double clipMaxX = double.MinValue;
                double clipMaxY = double.MinValue;
                foreach (JToken v in clip)
                {
                    JArray va = v as JArray;
                    double x = va[0].Value<double>();
                    double y = va[1].Value<double>();
                    PlanarMath.RotateVector(ref x, ref y, angle);
                    clipMinX = Math.Min(clipMinX, x);
                    clipMinY = Math.Min(clipMinY, y);
                    clipMaxX = Math.Max(clipMaxX, x);
                    clipMaxY = Math.Max(clipMaxY, y);
                }
                if (clipMaxX < maxX)
                {
                    clipXOffset = -(maxX - clipMaxX) / 2;
                    maxX = clipMaxX;
                }
                if (clipMinX > minX)
                {
                    clipXOffset += (clipMinX - minX) / 2;
                    minX = clipMinX;
                }

                if (clipMaxY < maxY)
                {
                    clipYOffset = -(maxY - clipMaxY) / 2;
                    maxY = clipMaxY;
                }
                if (clipMinY > minY)
                {
                    clipYOffset += (clipMinY - minY) / 2;
                    minY = clipMinY;
                }
            }

            double clippedH = maxY - minY;
            double clippedW = maxX - minX;

            // The entries are scanned in array order and the first one that fits wins, which assumes ascending
            // lengths. Nothing checks the order.
            foreach (JObject item in aIfcProducts.Cast<JObject>())
            {
                //check if width and length needs to be swapped
                // Q4: the swap is not undone, so it stays in effect for the following entries.
                double width = item["width"].Value<double>();
                if (Math.Abs(width - w) > 20)
                {
                    (w, h) = (h, w);
                    (clippedW, clippedH) = (clippedH, clippedW);
                    (clipXOffset, clipYOffset) = (clipYOffset, clipXOffset);
                }

				double len = item["length"].Value<double>();
				// The unclipped length decides whether an entry fits (tolerance 1); the clipped length decides
				// the scale (an entry more than 5 longer is scaled down).
				if (len+1 >= h)
                {
                    //Item can be used
                    if (len > clippedH + 5)
                    {
                        //down scale if necessary
                        aLengthScaleRatio = clippedH / len;
                        aClippedOffset = clipYOffset;
                    }
                    return item;
                }
            }

			//item not found in the list, use the last one
			JObject lastItem = aIfcProducts.Last as JObject;
            if (lastItem != null)
            {
                double len = lastItem["length"].Value<double>();
                if (lastItem["length_upscale"].Value<int>() == 0)
                {
                    //Item is not upscallable
                    if (len + 5 < clippedH)
                    {
                        //If item length with some tolerance is shorther than we require we can't use the object
                        lastItem = null;
                    }
                }
                else
                {
                    //can be upscalled
                    aLengthScaleRatio = clippedH / len;
					aClippedOffset = clipYOffset;
				}
            }

            return lastItem;
        }
    }
}
