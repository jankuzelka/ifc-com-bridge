using IfcComBridge.Cli.Configuration;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IfcComBridge.IntegrationTests
{
    /// <summary>
    /// Private inputs come from ONE configuration file (tests.local.json, kept outside the repository).
    /// scripts\test.ps1 -Config passes its path to the test host; see docs\local-testing.md.
    /// </summary>
    public static class LocalData
    {
        public static string ConfigPath => LocalTestGate.ConfiguredConfigPath;
        public static string BaselineDirectory => LocalTestGate.ConfiguredBaselineDirectory;

        /// <summary>The validated configuration; throws with every problem when it is invalid.</summary>
        public static LocalTestConfiguration Config => LocalTestGate.Require(ConfigPath);
    }

    /// <summary>
    /// A [Fact] that runs only when the private configuration enables <see cref="LocalScenario"/>.
    /// Without a configuration, or with the scenario left out, it is skipped (CI stays green). With an
    /// INVALID configuration it runs and fails with the validation problems.
    /// </summary>
    public sealed class LocalScenarioFactAttribute : FactAttribute
    {
        public LocalScenarioFactAttribute(LocalScenario scenario)
        {
            Skip = LocalTestGate.SkipReason(LocalData.ConfigPath, scenario);
        }
    }

    /// <summary>A [Fact] that runs only with both a private configuration and a baseline folder.</summary>
    public sealed class BaselineFactAttribute : FactAttribute
    {
        public BaselineFactAttribute()
        {
            Skip = LocalTestGate.BaselineSkipReason(LocalData.ConfigPath, LocalData.BaselineDirectory);
        }
    }
}
