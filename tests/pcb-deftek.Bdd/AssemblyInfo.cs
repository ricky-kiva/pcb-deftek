using Xunit;

// Reqnroll scenarios assert on timers, progress marshalling, and temp artifacts.
// Running feature collections in parallel starves the thread pool on CI and turns
// wall-clock assertions (batch cancellation, export cancellation) into flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
