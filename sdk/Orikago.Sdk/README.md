# Orikago.Sdk

An MSBuild project SDK that builds, runs, tests and publishes Go programs with the `dotnet` CLI.
The Go toolchain does the compiling: the SDK drives `go build`, `go test` and `go vet`.

Experimental. Read the [repository](https://github.com/orlys/orikago) before relying on it.

## Usage

Put a `.goproj` next to the Go sources, for example `hello.goproj`:

```xml
<Project Sdk="Orikago.Sdk/0.2.1-preview">
</Project>
```

```shell
dotnet build                  # bin/Debug/hello, hello.exe on Windows
dotnet run
dotnet test                   # go test
dotnet publish -r linux-x64
```

Needs the .NET 10 SDK and Go on `PATH`. A missing `go.mod` is created on the first build.

The properties, items and Visual Studio support are described in the
[repository](https://github.com/orlys/orikago).
