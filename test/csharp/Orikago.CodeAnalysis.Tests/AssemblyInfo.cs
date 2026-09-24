// GoBuildContextTests exercises the sidecar's handling of the *inherited* GOFLAGS, which
// can only be set process-wide (Environment.SetEnvironmentVariable) because the public
// API deliberately offers no environment injection point. xUnit runs separate test
// classes in parallel by default, so an unserialised run would leak that GOFLAGS into the
// concurrent `go mod tidy` / `go build` invocations of GoModuleResolutionTests and friends
// and make them nondeterministic. The suite is process-bound (it spawns go), not
// CPU-bound, so serialising it costs little.
[assembly: CollectionBehavior(DisableTestParallelization = true)]