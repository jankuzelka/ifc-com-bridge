using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IfcComBridge.Cli.Summary
{
    /// <summary>
    /// Reads the fixed-size header of a WexBIM file (the xBIM web-viewer format) for regression checks.
    /// Layout as written by xBIM 5.x SaveAsWexBim: int32 magic 94132117, byte version, int32 counts of
    /// shapes, vertices, triangles, matrices, products and styles, float32 meter, int16 region count, then
    /// per region int32 population + float32[3] centre + float32[6] bounds.
    ///
    /// xBIM writes regions of equal population in a non-deterministic order, so two runs of identical
    /// code produce different bytes. Regions are therefore reported in a canonical order and no file
    /// hash is included.
    /// </summary>
    public static class WexbimHeaderReader
    {
        public const int Magic = 94132117;

        public static JObject Read(string path)
        {
            var result = new JObject
            {
                ["fileBytes"] = new FileInfo(path).Length,
            };

            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 4)
                {
                    result["valid"] = false;
                    return result;
                }

                int magic = reader.ReadInt32();
                result["valid"] = magic == Magic;
                if (magic != Magic)
                    return result;

                try
                {
                    result["version"] = reader.ReadByte();
                    result["shapes"] = reader.ReadInt32();
                    result["vertices"] = reader.ReadInt32();
                    result["triangles"] = reader.ReadInt32();
                    result["matrices"] = reader.ReadInt32();
                    result["products"] = reader.ReadInt32();
                    result["styles"] = reader.ReadInt32();
                    result["meter"] = ModelSummarizer.Num(reader.ReadSingle());
                    short regionCount = reader.ReadInt16();
                    var regions = new JArray();
                    for (int i = 0; i < regionCount; i++)
                    {
                        var region = new JObject { ["population"] = reader.ReadInt32() };
                        region["centre"] = ReadFloats(reader, 3);
                        region["bounds"] = ReadFloats(reader, 6);
                        regions.Add(region);
                    }
                    result["regions"] = new JArray(regions.OrderBy(r => r.ToString(Formatting.None), StringComparer.Ordinal));
                }
                catch (EndOfStreamException)
                {
                    result["truncatedHeader"] = true;
                }
            }

            return result;
        }

        private static JArray ReadFloats(BinaryReader reader, int count)
        {
            var values = new JArray();
            for (int i = 0; i < count; i++)
                values.Add(ModelSummarizer.Num(reader.ReadSingle()));
            return values;
        }

    }
}
