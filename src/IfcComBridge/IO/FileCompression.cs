using System.IO;
using System.IO.Compression;
using IfcComBridge.Infrastructure;
using Microsoft.Extensions.Logging;

namespace IfcComBridge.IO
{
    /// <summary>Zips a single output file (optional compression of saved IFC/WexBIM files).</summary>
    /// <remarks>Reachable only through ModelFiles' aCompression parameter, which no caller sets.</remarks>
    internal static class FileCompression
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _logger = LoggingSetup.CreateLogger(nameof(FileCompression));

        internal static void CompressFile(string aSourceFilePath, string aDestinationZipPath, CompressionLevel aCompressionLevel = CompressionLevel.Optimal)
        {
            _logger.LogInformation($"Compressing file...");

            if (!File.Exists(aSourceFilePath))
                throw new InvalidDataException($"No file selected to compress.");

            if (File.Exists(aDestinationZipPath))
                File.Delete(aDestinationZipPath);

            using (var zipToOpen = new FileStream(aDestinationZipPath, FileMode.Create))
            {
                using (var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    var fileName = Path.GetFileName(aSourceFilePath);

                    ZipArchiveEntry zipEntry = archive.CreateEntry(fileName, aCompressionLevel);

                    using (var originalFileStream = new FileStream(aSourceFilePath, FileMode.Open))
                    {
                        using (var entryStream = zipEntry.Open())
                        {
                            originalFileStream.CopyTo(entryStream);
                        }
                    }
                }
            }

            _logger.LogInformation($"Compressed file: {aDestinationZipPath}");
        }
    }
}
