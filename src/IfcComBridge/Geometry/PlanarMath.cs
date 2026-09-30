using System;

namespace IfcComBridge.Geometry
{
    /// <summary>2-D rotation helpers (degrees) for placements and catalogue selection.</summary>
    /// <remarks>
    /// Both use the right-handed XY plane with positive angles counter-clockwise (from +X towards +Y).
    /// ProductCopier passes -angle for the placement, ProductSelector +angle for measuring.
    /// </remarks>
    internal static class PlanarMath
    {
        // The unit direction at the given angle from +X, in the XY plane: an IfcAxis2Placement3D
        // RefDirection, i.e. the local X axis, for Axis = +Z.
        internal static double[] CalcRefDirection(double aAngleDeg)
        {
            double angleRad = aAngleDeg * Math.PI / 180.0;

            return new double[] {Math.Cos(angleRad), Math.Sin(angleRad), 0};
        }

        // Rotates (aX, aY) about the origin.
        internal static void RotateVector(ref double aX, ref double aY, double aRotationDeg)
        {
            double rad = aRotationDeg * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double newX = aX*cos - aY*sin;
            double newY = aX*sin + aY*cos;
            aX = newX;
            aY = newY;
        }
    }
}
