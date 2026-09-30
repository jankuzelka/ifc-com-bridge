using Xunit;

// The library keeps process-wide static state (logger, editor credentials, CurrentOptimization, xBIM's
// global ModelProviderFactory). Tests that touch it must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
