using IfcComBridge.Tests.Infrastructure;
using IfcComBridge;
using Xunit;
using IfcComBridge.Geometry;

namespace IfcComBridge.Tests.Characterization
{
    /// <summary>2-D rotation helpers used for placements and catalogue selection (PlanarMath).</summary>
    public class PlanarMathTests
    {
        [Theory]
        [InlineData(1, 0, 90, 0, 1)]
        [InlineData(1, 0, -90, 0, -1)]
        [InlineData(1, 0, 360, 1, 0)]
        [InlineData(2, 3, 0, 2, 3)]
        [InlineData(-399, -50, -120, 156.198729810778, 370.544136109991)]
        public void RotateVector_RotatesCounterClockwiseByDegrees(double x, double y, double degrees, double expectedX, double expectedY)
        {
            double rx = x, ry = y;
            PlanarMath.RotateVector(ref rx, ref ry, degrees);
            Assert.Equal(expectedX, rx, 9);
            Assert.Equal(expectedY, ry, 9);
        }

        [Theory]
        [InlineData(0, 1, 0)]
        [InlineData(90, 0, 1)]
        [InlineData(-90, 0, -1)]
        [InlineData(-120, -0.5, -0.866025403784)]
        public void CalcRefDirection_IsUnitVectorInXyPlane(double degrees, double expectedX, double expectedY)
        {
            double[] direction = PlanarMath.CalcRefDirection(degrees);
            Assert.Equal(3, direction.Length);
            Assert.Equal(expectedX, direction[0], 9);
            Assert.Equal(expectedY, direction[1], 9);
            Assert.Equal(0, direction[2]);
        }
    }

    /// <summary>Exact-equality comparer behind the B-rep point de-duplication.</summary>
    public class ExactPoint3ComparerTests
    {
        private readonly ExactPoint3Comparer _comparer = new ExactPoint3Comparer();

        [Fact]
        public void EqualCoordinates_AreEqual_AndHashAlike()
        {
            Assert.True(_comparer.Equals(new[] { 1.0, 2, 3 }, new[] { 1.0, 2, 3 }));
            Assert.Equal(_comparer.GetHashCode(new[] { 1.0, 2, 3 }), _comparer.GetHashCode(new[] { 1.0, 2, 3 }));
        }

        [Fact]
        public void ComparisonIsExact_NoTolerance()
        {
            Assert.False(_comparer.Equals(new[] { 1.0, 2, 3 }, new[] { 1.0, 2, 3.0000000001 }));
        }

        [Fact]
        public void OnlyThreeComponentArraysCanBeEqual()
        {
            Assert.False(_comparer.Equals(new[] { 1.0, 2 }, new[] { 1.0, 2 }));
            Assert.False(_comparer.Equals(null, new[] { 1.0, 2, 3 }));
            Assert.Equal(0, _comparer.GetHashCode(null));
            Assert.Equal(0, _comparer.GetHashCode(new[] { 1.0, 2 }));
        }

        [Fact]
        public void NaN_IsNeverEqual_SoTwoDimensionalPointsAreNeverMerged()
        {
            // xBIM reports Z = NaN for 2-D points; NaN != NaN, so the optimizer never merges them.
            Assert.False(_comparer.Equals(new[] { 1.0, 2, double.NaN }, new[] { 1.0, 2, double.NaN }));
        }
    }
}
