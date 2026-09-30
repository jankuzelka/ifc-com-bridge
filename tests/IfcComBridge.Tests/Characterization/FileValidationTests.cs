using System.IO;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using Newtonsoft.Json.Linq;
using IfcComBridge;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xunit;
using IfcComBridge.IO;

namespace IfcComBridge.Tests.Characterization
{
    public class FileValidationTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void CheckFile_WithoutPath_ThrowsInvalidData(string path)
        {
            Assert.Throws<InvalidDataException>(() => ModelFiles.CheckFile(path, "test"));
        }

        [Fact]
        public void CheckFile_MissingFile_ThrowsFileNotFound()
        {
            using (var temp = new TempDirectory())
                Assert.Throws<FileNotFoundException>(() => ModelFiles.CheckFile(temp.File("missing.ifc"), "test"));
        }

        [Fact]
        public void LoadJson_ParsesFile()
        {
            using (var temp = new TempDirectory())
            {
                string path = temp.File("a.json");
                File.WriteAllText(path, "{\"a\": [1, 2]}");
                JToken json = ModelFiles.LoadJson(path, "test");
                Assert.Equal(2, ((JArray)json["a"]).Count);
            }
        }

        [Fact]
        public void LoadIfcModel_Ifc4_OpensInMemory()
        {
            using (var temp = new TempDirectory())
            {
                string path = temp.File("building.ifc");
                SyntheticModels.WriteBuildingModel(path);
                using (IfcStore model = ModelFiles.LoadIfcModel(path, "test"))
                    Assert.Equal(XbimSchemaVersion.Ifc4, model.SchemaVersion);
            }
        }

        [Fact]
        public void Q11_LoadIfcModel_Ifc2x3_ThrowsFileNotFound()
        {
            using (var temp = new TempDirectory())
            {
                string path = temp.File("ifc2x3.ifc");
                SyntheticModels.WriteIfc2x3Model(path);
                var ex = Assert.Throws<FileNotFoundException>(() => ModelFiles.LoadIfcModel(path, "test"));
                Assert.Equal("Invalid schema version. Only IFC4 is supported.", ex.Message);
            }
        }
    }
}
