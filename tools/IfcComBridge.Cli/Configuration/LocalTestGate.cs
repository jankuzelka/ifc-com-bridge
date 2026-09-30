using System;

namespace IfcComBridge.Cli.Configuration
{
    public enum LocalScenario
    {
        /// <summary>scenarios.model (one IFC4 file).</summary>
        Model,
        /// <summary>scenarios.model including its optional transforms.</summary>
        ModelWithTransforms,
        /// <summary>scenarios.composition (all four inputs).</summary>
        Composition,
    }

    /// <summary>
    /// Decides whether a private-data test runs. The test host receives the configuration PATH (never
    /// the inputs themselves) from scripts\test.ps1 -Config through IFCCOMBRIDGE_TEST_CONFIG; for IDE
    /// runs that variable can be set by hand.
    ///
    /// No configuration, or a scenario that is not configured, skips the test. An INVALID configuration
    /// does not skip: the test runs and fails with the validation problems, because a broken private
    /// setup must never look like a green run.
    /// </summary>
    public static class LocalTestGate
    {
        public const string ConfigVariable = "IFCCOMBRIDGE_TEST_CONFIG";
        public const string BaselineVariable = "IFCCOMBRIDGE_BASELINE_DIR";

        public static string ConfiguredConfigPath => Clean(Environment.GetEnvironmentVariable(ConfigVariable));
        public static string ConfiguredBaselineDirectory => Clean(Environment.GetEnvironmentVariable(BaselineVariable));

        /// <returns>The reason to skip, or null when the test should run.</returns>
        public static string SkipReason(string configPath, LocalScenario scenario)
        {
            if (configPath == null)
                return "No private test configuration; run scripts\\test.ps1 -Config <tests.local.json>.";

            LocalTestConfiguration config;
            try
            {
                config = LocalTestConfiguration.Load(configPath);
            }
            catch (Exception ex) when (ex is ConfigurationException || ex is ArgumentException)
            {
                return null; // run, and fail loudly in Require()
            }

            switch (scenario)
            {
                case LocalScenario.Model:
                    return config.Model == null ? "Scenario 'model' is not configured." : null;
                case LocalScenario.ModelWithTransforms:
                    if (config.Model == null)
                        return "Scenario 'model' is not configured.";
                    return config.Model.Transforms == null ? "'scenarios.model.transforms' is not configured." : null;
                case LocalScenario.Composition:
                    return config.Composition == null ? "Scenario 'composition' is not configured." : null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }

        /// <returns>The reason to skip the baseline comparison, or null when it should run.</returns>
        public static string BaselineSkipReason(string configPath, string baselineDirectory)
        {
            if (configPath == null)
                return "No private test configuration; run scripts\\test.ps1 -Config <tests.local.json> -BaselineDir <folder>.";
            if (baselineDirectory == null)
                return "No baseline given; run scripts\\test.ps1 -Config <tests.local.json> -BaselineDir <folder>.";
            return null;
        }

        /// <summary>For test bodies: the validated configuration, or a ConfigurationException listing every problem.</summary>
        public static LocalTestConfiguration Require(string configPath) => LocalTestConfiguration.Load(configPath);

        private static string Clean(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
    }
}
