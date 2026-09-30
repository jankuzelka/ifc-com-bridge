using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Cli.Synthetic
{
    /// <summary>
    /// Writes a complete, self-consistent synthetic input set: both IFC models, the JSON fixtures and a
    /// tests.local.json (relative paths) that the scripts and tests accept via -Config. It is a stand-in
    /// for private data with exactly the same shape; nothing in it relates to any real model.
    /// </summary>
    public static class SyntheticInputSet
    {
        public const string ConfigFileName = "tests.local.json";
        public const string BuildingFile = "building.ifc";
        public const string ProductsFile = "products.ifc";
        public const string EdgeCasesFolder = "edge-cases";

        /// <returns>The path of the written tests.local.json.</returns>
        public static string Write(string directory)
        {
            Directory.CreateDirectory(directory);
            SyntheticModels.WriteBuildingModel(Path.Combine(directory, BuildingFile));
            SyntheticModels.WriteProductsModel(Path.Combine(directory, ProductsFile));

            foreach (string file in SyntheticFixtures.MainFiles)
                SyntheticFixtures.CopyTo(file, Path.Combine(directory, file));

            string edgeCases = Path.Combine(directory, EdgeCasesFolder);
            Directory.CreateDirectory(edgeCases);
            foreach (string file in SyntheticFixtures.EdgeCaseFiles)
                SyntheticFixtures.CopyTo(file, Path.Combine(edgeCases, file));

            // The model scenario reuses products.ifc: transforms.json addresses entity labels in that file.
            var config = new JObject
            {
                ["version"] = 1,
                ["scenarios"] = new JObject
                {
                    ["model"] = new JObject
                    {
                        ["ifc"] = ProductsFile,
                        ["transforms"] = SyntheticFixtures.TransformsFile,
                    },
                    ["composition"] = new JObject
                    {
                        ["building"] = BuildingFile,
                        ["products"] = ProductsFile,
                        ["productsMap"] = SyntheticFixtures.ProductsMapFile,
                        ["layout"] = SyntheticFixtures.LayoutFile,
                    },
                },
            };
            string configPath = Path.Combine(directory, ConfigFileName);
            File.WriteAllText(configPath, config.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            return configPath;
        }
    }
}
