namespace Orikago.CodeAnalysis.Tests;

/// <summary>
/// Analysis and emit must be able to look at the *same* set of files.
/// </summary>
/// <remarks>
/// Go decides which files belong to a package from GOOS/GOARCH and build tags, so a
/// compilation that emits for linux while type-checking for the host is checking code
/// that will never be built and ignoring code that will. Emit already accepted
/// OS/Arch/Tags; GetDiagnostics and GetSemanticModel now accept the same options
/// (<see cref="GoAnalysisOptions"/>, the base type of <see cref="GoEmitOptions"/>), so
/// one options object can drive all three.
/// </remarks>
public sealed class GoBuildContextTests
{
    private const string PORTABLE_MAIN =
        "package main\n" +
        "\n" +
        "func main() {}\n";

    //  line 1: //go:build linux
    //  line 2:
    //  line 3: package main
    //  line 4:
    //  line 5: func linuxOnly() int {
    //  line 6: \tvar n int = "not an int"     <- error at col 14 (the string literal)
    //  line 7: \treturn n
    //  line 8: }
    private const string LINUX_ONLY_WITH_TYPE_ERROR =
        "//go:build linux\n" +
        "\n" +
        "package main\n" +
        "\n" +
        "func linuxOnly() int {\n" +
        "\tvar n int = \"not an int\"\n" +
        "\treturn n\n" +
        "}\n";

    public GoBuildContextTests()
    {
        Sidecar.EnsureConfigured();
    }

    [Fact]
    public void GetDiagnostics_LinuxGatedError_InvisibleByDefaultAndVisibleForGoosLinux()
    {
        // Arrange
        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", PORTABLE_MAIN);
        var gated = module.WriteFile("linux_gated.go", LINUX_ONLY_WITH_TYPE_ERROR);
        var compilation = GoCompilation.Create(module.Directory);
        var linuxOptions = new GoAnalysisOptions { OS = "linux", Arch = "amd64" };

        // Act
        var defaultDiagnostics = compilation.GetDiagnostics();
        var linuxDiagnostics = compilation.GetDiagnostics(linuxOptions);

        // Assert
        // Default build context (this host): the file is excluded, so nothing to report.
        Assert.Empty(defaultDiagnostics);

        // Asking for the linux build context brings the file — and its error — into view.
        var error = Assert.Single(linuxDiagnostics);
        Assert.Equal("GOTYPE", error.Id);
        Assert.Equal(gated, error.Location.File);
        Assert.Equal(6, error.Location.Line);
        Assert.Equal(14, error.Location.Column);
        Assert.Contains("as int value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDiagnostics_TagGatedError_InvisibleWithoutTagAndVisibleWithIt()
    {
        // Arrange
        //  line 1: //go:build orikagofeature
        //  ...
        //  line 6: \tvar n int = "not an int"     <- error at col 14
        const string TAG_GATED_WITH_TYPE_ERROR =
            "//go:build orikagofeature\n" +
            "\n" +
            "package main\n" +
            "\n" +
            "func gated() int {\n" +
            "\tvar n int = \"not an int\"\n" +
            "\treturn n\n" +
            "}\n";

        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", PORTABLE_MAIN);
        var gated = module.WriteFile("tag_gated.go", TAG_GATED_WITH_TYPE_ERROR);
        var compilation = GoCompilation.Create(module.Directory);
        var tagged = new GoAnalysisOptions { Tags = { "orikagofeature" } };

        // Act
        var untaggedDiagnostics = compilation.GetDiagnostics();
        var taggedDiagnostics = compilation.GetDiagnostics(tagged);

        // Assert
        Assert.Empty(untaggedDiagnostics);

        var error = Assert.Single(taggedDiagnostics);
        Assert.Equal(gated, error.Location.File);
        Assert.Equal(6, error.Location.Line);
        Assert.Equal(14, error.Location.Column);
    }

    [Fact]
    public void GetSemanticModel_LinuxGatedFile_ResolvesSymbolsOnlyInTheLinuxBuildContext()
    {
        // Arrange
        //  line 1: //go:build linux
        //  line 2:
        //  line 3: package main
        //  line 4:
        //  line 5: func linuxGreeting() string {
        //  line 6: \tgreeting := "hi"          <- declaration at line 6, col 2
        //  line 7: \treturn greeting           <- use at line 7, col 9
        //  line 8: }
        const string LINUX_ONLY_SYMBOL_SOURCE =
            "//go:build linux\n" +
            "\n" +
            "package main\n" +
            "\n" +
            "func linuxGreeting() string {\n" +
            "\tgreeting := \"hi\"\n" +
            "\treturn greeting\n" +
            "}\n";

        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", PORTABLE_MAIN);
        var gated = module.WriteFile("linux_symbol.go", LINUX_ONLY_SYMBOL_SOURCE);
        var compilation = GoCompilation.Create(module.Directory);

        // Act
        // In the linux build context the file is part of package main and `greeting`
        // resolves to the local variable declared one line above.
        var linuxModel = compilation.GetSemanticModel(
            new GoAnalysisOptions { OS = "linux", Arch = "amd64" });
        var symbol = linuxModel.GetSymbolAt(gated, 7, 9);

        // Assert
        Assert.NotNull(symbol);
        Assert.Equal("greeting", symbol.Name);
        Assert.Equal("var", symbol.Kind);
        Assert.Equal("string", symbol.Type);
        Assert.NotNull(symbol.DeclaredAt);
        Assert.Equal(6, symbol.DeclaredAt.Line);
        Assert.Equal(2, symbol.DeclaredAt.Column);
    }

    [Fact]
    public void GoEmitOptions_IsUsableAsAnalysisOptions_SoCheckAndEmitSeeTheSameFiles()
    {
        // Arrange
        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", PORTABLE_MAIN);
        module.WriteFile("linux_gated.go", LINUX_ONLY_WITH_TYPE_ERROR);

        // One options object, used for both analysis and emit: that is the whole point
        // of GoEmitOptions deriving from GoAnalysisOptions.
        var options = new GoEmitOptions { OS = "linux", Arch = "amd64" };
        var compilation = GoCompilation.Create(module.Directory);
        var outputDirectory = Path.Combine(module.Directory, "out");
        var outputPath = Path.Combine(outputDirectory, "app-linux");
        Directory.CreateDirectory(outputDirectory);

        // Act
        var diagnostics = compilation.GetDiagnostics(options);
        var emitResult = compilation.Emit(outputPath, options);

        // Assert
        Assert.Single(diagnostics);

        // And the emit for that same context fails on exactly that file, confirming the
        // check was not analyzing a different file set than the build compiles.
        Assert.False(
            condition: emitResult.Success,
            userMessage: "Emit was expected to fail: the linux file has a type error.");
        Assert.Contains(
            collection: emitResult.Diagnostics,
            filter: diagnostic => diagnostic.Location.File is { } file &&
                file.EndsWith("linux_gated.go", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Build tags reach the analysis sidecar's `go list` through GOFLAGS, because
    /// golang.org/x/tools/go/packages exposes no command line of its own.
    /// </summary>
    /// <remarks>
    /// Emit, by contrast, puts -tags on the `go build` command line and leaves the inherited
    /// GOFLAGS alone. If the sidecar *replaced* GOFLAGS with its own -tags it would
    /// silently discard every other flag the environment asked for, and the two would
    /// disagree about the very same module - which is exactly what
    /// <see cref="GoBuildContextTests"/> exists to prevent.
    /// </remarks>
    [Fact]
    public async Task GetDiagnostics_WithTags_KeepsInheritedGoflags_SoAnalysisStillAgreesWithEmit()
    {
        // Arrange
        using var module = new TemporaryGoModule("goflagsmod");

        // A dependency resolved through a local `replace` (nothing is downloaded), then
        // vendored - and the vendored copy is given a function the real dependency source
        // does not have. The module's meaning therefore depends on -mod: under vendor mode
        // it compiles, under -mod=mod the call is undefined.
        Directory.CreateDirectory(Path.Combine(module.Directory, "depsrc"));
        module.WriteFile(
            relativeName: Path.Combine("depsrc", "go.mod"),
            content: "module example.com/dep\n\ngo 1.21\n");
        module.WriteFile(
            relativeName: Path.Combine("depsrc", "dep.go"),
            content: "package dep\n\nfunc Base() string { return \"base\" }\n");
        module.WriteFile(
            relativeName: "go.mod",
            content: "module goflagsmod\n\ngo 1.21\n\n" +
                "require example.com/dep v0.0.0\n\nreplace example.com/dep => ./depsrc\n");
        module.WriteFile(
            relativeName: "main.go",
            content: "package main\n\nimport \"example.com/dep\"\n\n" +
                "func main() { println(dep.Base()) }\n");

        var (vendorExit, vendorOutput) = await module.RunGoAsync(
            arguments: ["mod", "vendor"],
            cancellationToken: CancellationToken.None);
        Assert.True(
            condition: vendorExit == 0,
            userMessage: "go mod vendor failed: " + vendorOutput);

        module.WriteFile(
            relativeName: Path.Combine("vendor", "example.com", "dep", "dep.go"),
            content: "package dep\n\nfunc Base() string { return \"base\" }\n\n" +
                "func VendorOnly() string { return \"vendor\" }\n");
        module.WriteFile(
            relativeName: "main.go",
            content: "package main\n\nimport \"example.com/dep\"\n\n" +
                "func main() { println(dep.Base()); println(dep.VendorOnly()) }\n");

        var compilation = GoCompilation.Create(module.Directory);

        // One options object for both calls: whatever they disagree about, it is not this.
        var options = new GoEmitOptions { Tags = { "orikagofeature" } };
        var outputDirectory = Path.Combine(module.Directory, "out");
        var outputPath = Path.Combine(outputDirectory, "app.bin");
        Directory.CreateDirectory(outputDirectory);

        var savedGoflags = Environment.GetEnvironmentVariable("GOFLAGS");
        try
        {
            // Act
            // Control: no inherited GOFLAGS. vendor/ exists, so the go command selects
            // vendor mode by itself, the patched function resolves, and both agree "clean".
            Environment.SetEnvironmentVariable("GOFLAGS", null);
            var controlDiagnostics = compilation.GetDiagnostics(options);
            var controlEmit = compilation.Emit(outputPath, options);

            // The real case: the environment asks for -mod=mod, which overrides the vendor
            // default.
            Environment.SetEnvironmentVariable("GOFLAGS", "-mod=mod");
            var emitResult = compilation.Emit(outputPath, options);
            var diagnostics = compilation.GetDiagnostics(options);

            // Assert
            Assert.Empty(controlDiagnostics);
            Assert.True(
                condition: controlEmit.Success,
                userMessage: "Emit was expected to succeed in the default (vendor) mode.");

            // Emit inherits GOFLAGS and fails...
            Assert.False(
                condition: emitResult.Success,
                userMessage: "Emit was expected to honour the inherited GOFLAGS=-mod=mod.");
            Assert.Contains(
                collection: emitResult.Diagnostics,
                filter: diagnostic => diagnostic.Message.Contains(
                    value: "undefined: dep.VendorOnly",
                    comparisonType: StringComparison.Ordinal));

            // ...so the analysis must report the same problem. A sidecar that overwrote
            // GOFLAGS with "-tags=orikagofeature" would drop -mod=mod, fall back to vendor
            // mode and report nothing here.
            var error = Assert.Single(diagnostics);
            Assert.Equal("GOTYPE", error.Id);
            Assert.Contains(
                expectedSubstring: "undefined: dep.VendorOnly",
                actualString: error.Message,
                comparisonType: StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOFLAGS", savedGoflags);
        }
    }

    /// <summary>
    /// Merging the requested -tags into the inherited GOFLAGS must still let the explicit
    /// option win when GOFLAGS already carries a -tags of its own.
    /// </summary>
    /// <remarks>
    /// The go command parses GOFLAGS left to right into a single flag set, so the last
    /// occurrence of a repeated flag is the one that takes effect and appending is an
    /// override - this test pins that down rather than leaving it as an assumption.
    /// </remarks>
    [Fact]
    public void GetDiagnostics_ExplicitTags_OverrideATagsAlreadyPresentInGoflags()
    {
        // Arrange
        using var module = new TemporaryGoModule("tagoverridemod");
        module.WriteFile("main.go", PORTABLE_MAIN);
        var gatedA = module.WriteFile(
            relativeName: "a_taga.go",
            content: "//go:build orikagoa\n\npackage main\n\n" +
                "func fa() int {\n\tvar n int = \"A\"\n\treturn n\n}\n");
        var gatedB = module.WriteFile(
            relativeName: "b_tagb.go",
            content: "//go:build orikagob\n\npackage main\n\n" +
                "func fb() int {\n\tvar n int = \"B\"\n\treturn n\n}\n");
        var compilation = GoCompilation.Create(module.Directory);

        var savedGoflags = Environment.GetEnvironmentVariable("GOFLAGS");
        try
        {
            // Act
            Environment.SetEnvironmentVariable("GOFLAGS", "-tags=orikagoa");
            var inheritedDiagnostics = compilation.GetDiagnostics();
            var explicitDiagnostics = compilation.GetDiagnostics(
                new GoAnalysisOptions { Tags = { "orikagob" } });

            // Assert
            // No explicit tags: the inherited -tags still selects the file set.
            var inherited = Assert.Single(inheritedDiagnostics);
            Assert.Equal(gatedA, inherited.Location.File);

            // Explicit tags: appended last, therefore the effective ones.
            var explicitly = Assert.Single(explicitDiagnostics);
            Assert.Equal(gatedB, explicitly.Location.File);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOFLAGS", savedGoflags);
        }
    }
}