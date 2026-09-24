namespace Orikago.CodeAnalysis.Tests;

/// <summary>
/// Type checking must model Go *modules*, not just a directory of files.
/// </summary>
/// <remarks>
/// The sidecar used to type-check with go/importer's "source" importer, which knows
/// nothing about go.mod, the module cache or go.work. A module that imports an
/// external dependency therefore built fine with `go build` while GetDiagnostics()
/// reported a bogus "could not import ..." error. These tests pin the contract:
/// whatever `go build` accepts, GetDiagnostics() must accept too — and real type
/// errors must still be found in the presence of external dependencies.
/// </remarks>
public sealed class GoModuleResolutionTests
{
    public GoModuleResolutionTests()
    {
        Sidecar.EnsureConfigured();
    }

    /// <summary>
    /// Creates a module that really depends on <c>github.com/google/uuid</c>, resolving
    /// the requirement with the real toolchain so go.mod/go.sum are genuine.
    /// </summary>
    private static async Task<TemporaryGoModule> CreateModuleWithExternalDependencyAsync(
        string source,
        CancellationToken cancellationToken)
    {
        const string EXTERNAL_DEPENDENCY = "github.com/google/uuid";

        var module = new TemporaryGoModule();
        try
        {
            // Resolve the requirement with the real toolchain
            module.WriteFile("main.go", source);
            var (tidyExit, tidyOutput) = await module.RunGoAsync(
                arguments: ["mod", "tidy"],
                cancellationToken);
            Assert.True(
                condition: tidyExit == 0,
                userMessage: FormattableString.Invariant(
                    $"'go mod tidy' failed ({tidyExit}) while preparing the fixture: {tidyOutput}"));

            // The fixture is only useful if go.mod really requires the external module
            var goMod = File.ReadAllText(Path.Combine(module.Directory, "go.mod"));
            Assert.Contains(EXTERNAL_DEPENDENCY, goMod, StringComparison.Ordinal);

            return module;
        }
        catch
        {
            // Ownership never reached the caller: clean up before propagating
            module.Dispose();
            throw;
        }
    }

    [Fact]
    public async Task GetDiagnostics_ModuleWithExternalDependency_AgreesWithGoBuild()
    {
        // Arrange
        //  line 1: package main
        //  line 2:
        //  line 3: import (
        //  line 4: \t"fmt"
        //  line 5:
        //  line 6: \t"github.com/google/uuid"
        //  line 7: )
        //  line 8:
        //  line 9: func main() {
        // line 10: \tid := uuid.New()
        // line 11: \tfmt.Println(id.String())
        // line 12: }
        const string USES_EXTERNAL_DEPENDENCY =
            "package main\n" +
            "\n" +
            "import (\n" +
            "\t\"fmt\"\n" +
            "\n" +
            "\t\"github.com/google/uuid\"\n" +
            ")\n" +
            "\n" +
            "func main() {\n" +
            "\tid := uuid.New()\n" +
            "\tfmt.Println(id.String())\n" +
            "}\n";

        using var module = await CreateModuleWithExternalDependencyAsync(
            source: USES_EXTERNAL_DEPENDENCY,
            cancellationToken: CancellationToken.None);

        // Ground truth: the real compiler is happy with this module.
        var (buildExit, buildOutput) = await module.RunGoAsync(
            arguments: ["build", "-o", Path.Combine(module.Directory, "out.bin"), "."],
            cancellationToken: CancellationToken.None);
        Assert.True(
            condition: buildExit == 0,
            userMessage: $"'go build' unexpectedly failed: {buildOutput}");

        // Act
        var diagnostics = GoCompilation.Create(module.Directory).GetDiagnostics();

        // Assert
        // The old source-importer loader reported
        //   could not import github.com/google/uuid (no required module provides package ...)
        // at line 6 even though go build succeeded.
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GetDiagnostics_ModuleWithExternalDependency_StillReportsRealTypeErrors()
    {
        // Arrange
        // Same module, but line 11 assigns a string to an int:
        // line 11: \tvar n int = id.String()   <- error reported at `id`, byte/UTF-16 col 14
        const string USES_EXTERNAL_DEPENDENCY_WITH_TYPE_ERROR =
            "package main\n" +
            "\n" +
            "import (\n" +
            "\t\"fmt\"\n" +
            "\n" +
            "\t\"github.com/google/uuid\"\n" +
            ")\n" +
            "\n" +
            "func main() {\n" +
            "\tid := uuid.New()\n" +
            "\tvar n int = id.String()\n" +
            "\tfmt.Println(n)\n" +
            "}\n";

        using var module = await CreateModuleWithExternalDependencyAsync(
            source: USES_EXTERNAL_DEPENDENCY_WITH_TYPE_ERROR,
            cancellationToken: CancellationToken.None);

        // Ground truth: the real compiler rejects this module.
        var (buildExit, _) = await module.RunGoAsync(
            arguments: ["build", "-o", Path.Combine(module.Directory, "out.bin"), "."],
            cancellationToken: CancellationToken.None);
        Assert.True(
            condition: buildExit != 0,
            userMessage: "'go build' was expected to fail on the deliberate type error.");

        // Act
        var diagnostics = GoCompilation.Create(module.Directory).GetDiagnostics();

        // Assert
        var error = Assert.Single(diagnostics);
        Assert.Equal("GOTYPE", error.Id);
        Assert.Equal(GoDiagnosticSeverity.Error, error.Severity);
        Assert.Contains("as int value", error.Message, StringComparison.Ordinal);
        Assert.Equal(11, error.Location.Line);
        Assert.Equal(14, error.Location.Column);

        // No bogus import diagnostic tagging along.
        Assert.DoesNotContain(
            collection: diagnostics,
            filter: diagnostic =>
                diagnostic.Message.Contains("could not import", StringComparison.Ordinal));
    }
}