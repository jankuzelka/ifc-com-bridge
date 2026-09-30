using System.IO;
using IfcComBridge.Infrastructure;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;

namespace IfcComBridge.IO
{
    /// <summary>
    /// Input validation, JSON and IFC loading (IFC4 only), and saving IFC and WexBIM files.
    /// </summary>
    /// <remarks>
    /// Every IFC file is loaded as an in-memory model, whatever its size. Saving deletes an existing file
    /// first (Q16). The optional compression is never switched on: no caller passes aCompression.
    /// </remarks>
    internal static class ModelFiles
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _logger = LoggingSetup.CreateLogger(nameof(ModelFiles));

        internal static void CheckFile(string aFile, string aType)
        {
            if (string.IsNullOrWhiteSpace(aFile))
            {
                _logger.LogInformation($"No {aType} file selected.");
                throw new InvalidDataException($"No {aType} file selected.");
            }

            if (!File.Exists(aFile))
            {
                _logger.LogInformation($"File {aFile} not found.");
                throw new FileNotFoundException($"File {aFile} not found.");
            }

            _logger.LogInformation($"IFC {aType} file name: {aFile}");
            _logger.LogInformation($"IFC {aType} file size: {new FileInfo(aFile).Length / 1e6:N}MB");
        }

        internal static JToken LoadJson(string aJsonFile, string aType)
        {
            CheckFile(aJsonFile, aType);

            _logger.LogInformation($"Loading JSON...");
            // Newtonsoft's default settings; the root may be any JSON value, and the callers index into it.
            return JToken.Parse(File.ReadAllText(aJsonFile));
        }

        internal static IfcStore LoadIfcModel(string aIfcFile, string aType)
        {
            CheckFile(aIfcFile, aType);

            // Sets xBIM's process-wide model provider (Q9), on every load. The heuristic provider chooses
            // between an in-memory model and an Esent database by file size.
            IfcStore.ModelProviderFactory.UseHeuristicModelProvider();

            _logger.LogInformation($"Loading {aType}...");
            // -1 is that size threshold, in MB. A negative threshold makes the heuristic provider open every
            // IFC file in memory (verified in xBIM 5.1). Only .xbim database files would still use Esent.
            IfcStore model = IfcStore.Open(aIfcFile, EditorIdentity.Shared, -1);

            // Q11: another schema is reported as FileNotFoundException, which clients may rely on. The model
            // opened for the check is not disposed here; the garbage collector reclaims it.
            if (model.SchemaVersion != XbimSchemaVersion.Ifc4)
            {
                _logger.LogInformation($"Invalid schema version. Only IFC4 is supported.");
                throw new FileNotFoundException($"Invalid schema version. Only IFC4 is supported.");
            }

            return model;
        }

        internal static void SaveIfcModel(string aIfcFile, IfcStore aModel, bool aCompression = false)
        {
            _logger.LogInformation($"Saving IFC file.");

            if (string.IsNullOrWhiteSpace(aIfcFile))
            {
                _logger.LogInformation($"No IFC file selected to save as.");
                throw new InvalidDataException($"No IFC file selected to save as.");
            }

            // Q16: the old file is deleted before the model is written, so it is lost if writing fails.
            if (File.Exists(aIfcFile))
            {
                _logger.LogInformation($"File {aIfcFile} already exists, deleting...");
                File.Delete(aIfcFile);
            }

            aModel.SaveAs(aIfcFile);
            _logger.LogInformation($"Saved file: {aIfcFile}");

            if (aCompression)
                FileCompression.CompressFile(aIfcFile, aIfcFile + ".zip");
        }

        internal static void SaveIfcWexbim(string aWexbimFile, IfcStore aModel, bool aCompression = false)
        {
            _logger.LogInformation($"Saving Wexbim file.");

            if (string.IsNullOrWhiteSpace(aWexbimFile))
            {
                _logger.LogInformation($"No Wexbim file selected to save as.");
                throw new InvalidDataException($"No Wexbim selected to save as.");
            }

            // Q16, as in SaveIfcModel.
            if (File.Exists(aWexbimFile))
            {
                _logger.LogInformation($"File {aWexbimFile} already exists, deleting...");
                File.Delete(aWexbimFile);
            }

            RegenerateGeometry(aModel);

            using (var wexbimFile = File.Create(aWexbimFile))
            {
                using (var wexBimBinaryWriter = new BinaryWriter(wexbimFile))
                {
                    // All products, no extra translation: SaveAsWexBim's optional product filter and translation
                    // are left at null. Q18: xBIM writes regions of equal population in no fixed order.
                    aModel.SaveAsWexBim(wexBimBinaryWriter);
                    wexBimBinaryWriter.Close();
                }
                wexbimFile.Close();
                _logger.LogInformation($"Saved file: {aWexbimFile}");
            }

            if (aCompression)
                FileCompression.CompressFile(aWexbimFile, aWexbimFile + ".zip");
        }

        internal static void RegenerateGeometry(IfcStore aModel)
        {
            _logger.LogInformation($"Upgrading to new geometry using the default 3D model...");
            // Tessellates the "Model" representation contexts with the native engine, on every save.
            var context = new Xbim3DModelContext(aModel, "model", null, _logger);
            // adjustWcs true, xBIM's default: a single root placement displacement, such as a geo-located site,
            // is taken out of the placement tree and added to the world coordinate system.
            context.CreateContext(null, true);
        }
    }
}
