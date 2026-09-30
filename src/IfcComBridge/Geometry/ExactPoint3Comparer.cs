using System.Collections.Generic;

namespace IfcComBridge.Geometry
{
    /// <summary>Exact equality of 3-component points (B-rep point de-duplication).</summary>
    /// <remarks>
    /// No tolerance: only identical doubles are equal. A 2-D IfcCartesianPoint has Z = NaN in xBIM, and
    /// NaN never equals NaN, so 2-D points are never merged (tested).
    /// </remarks>
    internal class ExactPoint3Comparer : IEqualityComparer<double[]>
    {
        public bool Equals(double[] aD1, double[] aD2)
        {
            if ((aD1 == null) || (aD2 == null) || (aD1.Length != 3) || (aD2.Length != 3))
                return false;

            for (int i = 0; i < 3; i++)
                if (aD1[i] != aD2[i])
                    return false;

            return true;
        }

        public int GetHashCode(double[] aObj)
        {
            if ((aObj == null) || (aObj.Length != 3))
                return 0;

            // Compute a hash code considering all three values
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + aObj[0].GetHashCode();
                hash = hash * 23 + aObj[1].GetHashCode();
                hash = hash * 23 + aObj[2].GetHashCode();
                return hash;
            }
        }
    }
}
