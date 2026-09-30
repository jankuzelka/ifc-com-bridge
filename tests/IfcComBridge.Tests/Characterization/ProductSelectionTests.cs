using System;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using Xunit;
using IfcComBridge.Composition;

namespace IfcComBridge.Tests.Characterization
{
    /// <summary>
    /// Catalogue selection (ProductSelector.FindMostSuitableObjectToDimensions). A layout set's
    /// rectangle is rotated by its angle; its Y extent is the required length, its X extent the width.
    /// Variants are scanned in order: the first whose length+1 >= required length wins and is scaled
    /// down when more than 5 longer; otherwise the LAST variant is used, scaled up only if
    /// length_upscale != 0 and rejected if more than 5 too short.
    /// </summary>
    public class ProductSelectionTests
    {
        private static (JObject Chosen, double Scale, double Offset) Select(JArray catalogue, JObject set)
        {
            JObject chosen = ProductSelector.FindMostSuitableObjectToDimensions(catalogue, set, out double scale, out double offset);
            return (chosen, scale, offset);
        }

        private static JArray Catalogue(int lastUpscale = 0) => new JArray(
            SyntheticModels.Variant(SyntheticModels.Id(900), "L900", 900, 0),
            SyntheticModels.Variant(SyntheticModels.Id(1000), "L1000", 1000, 0),
            SyntheticModels.Variant(SyntheticModels.Id(1200), "L1200", 1200, lastUpscale));

        private static JObject Set(double angle, JArray vertices, JArray clip = null) =>
            SyntheticModels.Set("S", "1", "X", angle, vertices, 0, 0, clip);

        private static string Name(JObject chosen) => (string)chosen["ifc_name"];

        [Fact]
        public void ExactFit_IsChosenUnscaled()
        {
            var (chosen, scale, offset) = Select(Catalogue(), Set(0, SyntheticModels.Rect(0, 0, 100, 1000)));
            Assert.Equal("L1000", Name(chosen));
            Assert.Equal(1, scale);
            Assert.Equal(0, offset);
        }

        [Fact]
        public void LongerVariant_IsScaledDown()
        {
            var (chosen, scale, _) = Select(Catalogue(), Set(0, SyntheticModels.Rect(0, 0, 100, 1100)));
            Assert.Equal("L1200", Name(chosen));
            Assert.Equal(1100.0 / 1200.0, scale, 12);
        }

        [Theory]
        [InlineData(1195, 1.0)]                 // variant at most 5 longer: used as is
        [InlineData(1194, 1194.0 / 1200.0)]     // more than 5 longer: scaled down
        public void VariantLongerByAtMostFive_IsNotScaled(double required, double expectedScale)
        {
            var (chosen, scale, _) = Select(Catalogue(), Set(0, SyntheticModels.Rect(0, 0, 100, required)));
            Assert.Equal("L1200", Name(chosen));
            Assert.Equal(expectedScale, scale, 12);
        }

        [Fact]
        public void NothingLongEnough_NotUpscalable_ReturnsNull()
        {
            var (chosen, scale, _) = Select(Catalogue(lastUpscale: 0), Set(0, SyntheticModels.Rect(0, 0, 100, 1500)));
            Assert.Null(chosen);
            Assert.Equal(1, scale);
        }

        [Fact]
        public void NothingLongEnough_ButWithinTolerance_ReturnsLastVariantUnscaled()
        {
            var (chosen, scale, _) = Select(Catalogue(lastUpscale: 0), Set(0, SyntheticModels.Rect(0, 0, 100, 1204)));
            Assert.Equal("L1200", Name(chosen));
            Assert.Equal(1, scale);
        }

        [Fact]
        public void NothingLongEnough_Upscalable_ScalesLastVariantUp()
        {
            var (chosen, scale, _) = Select(Catalogue(lastUpscale: 1), Set(0, SyntheticModels.Rect(0, 0, 100, 1500)));
            Assert.Equal("L1200", Name(chosen));
            Assert.Equal(1500.0 / 1200.0, scale, 12);
        }

        [Fact]
        public void RectangleIsRotatedByTheSetAngleBeforeMeasuring()
        {
            // 1100 x 100 along X, rotated by 90 degrees: the required length becomes 1100.
            var (chosen, scale, _) = Select(Catalogue(), Set(90, SyntheticModels.Rect(0, 0, 1100, 100)));
            Assert.Equal("L1200", Name(chosen));
            Assert.Equal(1100.0 / 1200.0, scale, 9);
        }

        [Fact]
        public void WidthMismatchOver20_SwapsLengthAndWidth()
        {
            // Unrotated 1000 x 100 along X: width 1000 does not match 100, so the dimensions swap.
            var (chosen, scale, _) = Select(Catalogue(), Set(0, SyntheticModels.Rect(0, 0, 1000, 100)));
            Assert.Equal("L1000", Name(chosen));
            Assert.Equal(1, scale);
        }

        [Fact]
        public void Q4_SwapPersistsAcrossCandidates()
        {
            // The first variant's width (500) triggers a swap, after which the required "length" is the
            // rectangle's width (100), so a 150-long variant is accepted and scaled to 100.
            var catalogue = new JArray(
                SyntheticModels.Variant(SyntheticModels.Id(1), "WIDE_SHORT", 150, 0, width: 500),
                SyntheticModels.Variant(SyntheticModels.Id(2), "RIGHT", 1000, 0));
            var (chosen, scale, _) = Select(catalogue, Set(0, SyntheticModels.Rect(0, 0, 100, 1000)));
            Assert.Equal("WIDE_SHORT", Name(chosen));
            Assert.Equal(100.0 / 150.0, scale, 12);
        }

        [Fact]
        public void ClipAtTheTop_ShortensAndShiftsNegative()
        {
            var (chosen, scale, offset) = Select(Catalogue(),
                Set(0, SyntheticModels.Rect(0, 0, 100, 1000), clip: SyntheticModels.Rect(0, 0, 100, 900)));
            Assert.Equal("L1000", Name(chosen));
            Assert.Equal(0.9, scale, 12);
            Assert.Equal(-50, offset, 12);
        }

        [Fact]
        public void ClipAtTheBottom_ShortensAndShiftsPositive()
        {
            var (chosen, scale, offset) = Select(Catalogue(),
                Set(0, SyntheticModels.Rect(0, 0, 100, 1000), clip: SyntheticModels.Rect(0, 100, 100, 1000)));
            Assert.Equal("L1000", Name(chosen));
            Assert.Equal(0.9, scale, 12);
            Assert.Equal(50, offset, 12);
        }

        [Fact]
        public void MissingAngle_DefaultsTo360()
        {
            JObject set = Set(0, SyntheticModels.Rect(0, 0, 100, 1000));
            set["rotation"] = new JObject();
            var (chosen, scale, _) = Select(Catalogue(), set);
            Assert.Equal("L1000", Name(chosen));
            Assert.Equal(1, scale);
        }

        [Fact]
        public void Q5_MissingRotation_ThrowsNullReference()
        {
            JObject set = Set(0, SyntheticModels.Rect(0, 0, 100, 1000));
            set.Remove("rotation");
            Assert.Throws<NullReferenceException>(() => Select(Catalogue(), set));
        }
    }
}
