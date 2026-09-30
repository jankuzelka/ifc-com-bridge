using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Cli.Synthetic
{
    /// <summary>
    /// The synthetic JSON fixtures committed under tests/fixtures/synthetic, embedded into this assembly
    /// (see IfcComBridge.Cli.csproj). They reference only names, GlobalIds and entity labels that exist in
    /// the models written by <see cref="SyntheticModels"/>; a test checks that correspondence.
    /// </summary>
    public static class SyntheticFixtures
    {
        public const string ProductsMapFile = "products-map.json";
        public const string LayoutFile = "layout.json";
        /// <summary>Entity labels refer to products.ifc written by <see cref="SyntheticModels.WriteProductsModel"/>.</summary>
        public const string TransformsFile = "transforms.json";

        // tests/fixtures/synthetic/edge-cases
        public const string PartialMatrixTransformsFile = "transforms.partial-matrix.json";
        public const string SkippedEntriesTransformsFile = "transforms.skipped-entries.json";
        public const string MissingModifiedProductsTransformsFile = "transforms.missing-modified-products.json";
        public const string MissingRotationLayoutFile = "layout.missing-rotation.json";

        public static readonly string[] MainFiles = { ProductsMapFile, LayoutFile, TransformsFile };

        public static readonly string[] EdgeCaseFiles =
        {
            PartialMatrixTransformsFile, SkippedEntriesTransformsFile, MissingModifiedProductsTransformsFile, MissingRotationLayoutFile,
        };

        private const string ResourcePrefix = "IfcComBridge.Cli.Synthetic.Fixtures.";

        public static string ReadText(string fileName)
        {
            using (Stream stream = typeof(SyntheticFixtures).Assembly.GetManifestResourceStream(ResourcePrefix + fileName))
            {
                if (stream == null)
                    throw new ArgumentException($"No embedded synthetic fixture '{fileName}'.", nameof(fileName));
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>Parses the fixture; every call returns a fresh, independent copy.</summary>
        public static JToken Read(string fileName) => JToken.Parse(ReadText(fileName));

        /// <summary>Writes the fixture text verbatim (UTF-8, no BOM).</summary>
        public static void CopyTo(string fileName, string path) =>
            File.WriteAllText(path, ReadText(fileName), new UTF8Encoding(false));
    }
}
