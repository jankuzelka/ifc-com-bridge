using System;
using System.IO;
using System.Text;
using IfcComBridge.Cli.Summary;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IfcComBridge.Tests.Infrastructure
{
    /// <summary>
    /// Golden-file assertions for summaries of SYNTHETIC fixtures (never private data).
    /// Golden files are read from the test output folder (copied from tests\IfcComBridge.Tests\Golden).
    /// To (re)generate them, set IFCCOMBRIDGE_UPDATE_GOLDEN to the source Golden folder and run the
    /// tests once; review the diff before committing.
    /// </summary>
    public static class Golden
    {
        public const string UpdateVariable = "IFCCOMBRIDGE_UPDATE_GOLDEN";

        public static void AssertMatches(string name, JToken actual)
        {
            string text = ModelSummarizer.ToJsonText(actual);
            string updateDirectory = Environment.GetEnvironmentVariable(UpdateVariable);
            if (!string.IsNullOrWhiteSpace(updateDirectory))
            {
                Directory.CreateDirectory(updateDirectory);
                File.WriteAllText(Path.Combine(updateDirectory, name), text, new UTF8Encoding(false));
                return;
            }

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Golden", name);
            Assert.True(File.Exists(path), $"Golden file '{name}' is missing. Generate it with {UpdateVariable}=<tests\\IfcComBridge.Tests\\Golden>.");

            var differences = SummaryComparer.Compare(JToken.Parse(File.ReadAllText(path)), actual);
            Assert.True(differences.Count == 0,
                $"'{name}' differs from the golden file ({differences.Count} difference(s)):\n  " + string.Join("\n  ", differences));
        }
    }
}
