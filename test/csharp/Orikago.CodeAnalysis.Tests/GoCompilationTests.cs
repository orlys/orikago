namespace Orikago.CodeAnalysis.Tests;

public sealed class GoCompilationTests
{
    private const string FIXED_SOURCE =
        "package main\n" +
        "\n" +
        "import \"fmt\"\n" +
        "\n" +
        "func main() {\n" +
        "\tfmt.Println(\"orikago-e2e\", 6*7)\n" +
        "}\n";

    public GoCompilationTests()
    {
        Sidecar.EnsureConfigured();
    }

    [Fact]
    public void GetDiagnostics_TypeErrorModule_ReportsGoTypeMismatchAtLine8()
    {
        // Arrange
        //  line 1: package main
        //  line 2:
        //  line 3: import "fmt"
        //  line 4:
        //  line 5: func main() {
        //  line 6: \tvar s string = "a"
        //  line 7: \tvar i int = 1
        //  line 8: \tx := s + i          <- go/types: invalid operation: s + i (mismatched
        //  line 9: \tfmt.Println(x)         types string and int) at line 8, col 7 (the `s`)
        // line 10: }
        const string TYPE_ERROR_SOURCE =
            "package main\n" +
            "\n" +
            "import \"fmt\"\n" +
            "\n" +
            "func main() {\n" +
            "\tvar s string = \"a\"\n" +
            "\tvar i int = 1\n" +
            "\tx := s + i\n" +
            "\tfmt.Println(x)\n" +
            "}\n";

        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", TYPE_ERROR_SOURCE);
        var compilation = GoCompilation.Create(module.Directory);

        // Act
        var diagnostics = compilation.GetDiagnostics();

        // Assert
        Assert.NotEmpty(diagnostics);

        var typeError = Assert.Single(
            collection: diagnostics,
            predicate: diagnostic =>
                string.Equals(diagnostic.Id, "GOTYPE", StringComparison.Ordinal));
        Assert.Equal(GoDiagnosticSeverity.Error, typeError.Severity);
        Assert.Contains(
            expectedSubstring: "mismatched types string and int",
            actualString: typeError.Message,
            comparisonType: StringComparison.Ordinal);
        Assert.Equal(8, typeError.Location.Line);
        Assert.Equal(7, typeError.Location.Column);
    }

    [Fact]
    public void GetDiagnostics_ValidModule_IsEmpty()
    {
        // Arrange
        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", FIXED_SOURCE);
        var compilation = GoCompilation.Create(module.Directory);

        // Act
        var diagnostics = compilation.GetDiagnostics();

        // Assert
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Emit_BuildErrorAfterMultibyteText_ReportsUtf16Column()
    {
        // Arrange
        using var module = new TemporaryGoModule();

        // 第 6 行：\tfmt.Println("你好世界", undefinedVar)
        // undefinedVar 位於位元組欄 30（你好世界 佔 12 位元組），UTF-16 欄 22
        module.WriteFile(
            relativeName: "main.go",
            content: "package main\n\nimport \"fmt\"\n\n" +
                "func main() {\n\tfmt.Println(\"你好世界\", undefinedVar)\n}\n");
        var compilation = GoCompilation.Create(module.Directory);

        // Act
        var result = compilation.Emit(Path.Combine(module.Directory, "out.exe"));

        // Assert
        Assert.False(result.Success);
        var diagnostic = Assert.Single(
            collection: result.Diagnostics,
            predicate: candidate =>
                candidate.Message.Contains("undefinedVar", StringComparison.Ordinal));
        Assert.Equal(6, diagnostic.Location.Line);
        Assert.Equal(22, diagnostic.Location.Column);
    }

    [Fact]
    public async Task Emit_ValidModule_ProducesExecutableThatPrintsExpectedOutput()
    {
        // Arrange
        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", FIXED_SOURCE);
        var compilation = GoCompilation.Create(module.Directory);
        Assert.Empty(compilation.GetDiagnostics());

        var hostExtension = OperatingSystem.IsWindows() ? ".exe" : "";
        var outputDirectory = Path.Combine(module.Directory, "out");
        var outputPath = Path.Combine(outputDirectory, "app" + hostExtension);
        Directory.CreateDirectory(outputDirectory);

        // Act
        var emitResult = compilation.Emit(outputPath);

        // Assert
        Assert.True(
            condition: emitResult.Success,
            userMessage: "Emit reported failure: " + string.Join("; ", emitResult.Diagnostics));
        Assert.True(
            condition: File.Exists(outputPath),
            userMessage: $"Emit did not produce a file at '{outputPath}'.");
        Assert.True(
            condition: new FileInfo(outputPath) is { Length: > 0 },
            userMessage: "Emitted binary is empty.");

        // The binary is real: run it and capture stdout.
        var (exitCode, standardOutput, standardError) = await ProcessRunner.RunAsync(
            fileName: outputPath,
            arguments: [],
            workingDirectory: null,
            timeout: TimeSpan.FromMinutes(1),
            cancellationToken: CancellationToken.None);
        Assert.True(
            condition: exitCode == 0,
            userMessage: FormattableString.Invariant(
                $"Emitted binary exited with {exitCode}. stderr: {standardError}"));
        Assert.Equal("orikago-e2e 42", standardOutput.Trim());
    }

    [Fact]
    public void Emit_LinuxArm64_ProducesElfBinaryWithoutExeSuffixDifferingFromHostBuild()
    {
        // Arrange
        using var module = new TemporaryGoModule();
        module.WriteFile("main.go", FIXED_SOURCE);
        var compilation = GoCompilation.Create(module.Directory);

        var hostExtension = OperatingSystem.IsWindows() ? ".exe" : "";
        var hostPath = Path.Combine(module.Directory, "out", "app-host" + hostExtension);
        var crossPath = Path.Combine(module.Directory, "out", "app-linux-arm64");
        Directory.CreateDirectory(Path.Combine(module.Directory, "out"));

        // Act
        var hostResult = compilation.Emit(hostPath);
        var crossOptions = new GoEmitOptions { OS = "linux", Arch = "arm64" };
        var crossResult = compilation.Emit(crossPath, crossOptions);

        // Assert
        Assert.True(
            condition: hostResult.Success,
            userMessage: "Host emit reported failure: " +
                string.Join("; ", hostResult.Diagnostics));
        Assert.True(
            condition: crossResult.Success,
            userMessage: "Cross emit reported failure: " +
                string.Join("; ", crossResult.Diagnostics));

        Assert.True(File.Exists(hostPath), $"Host emit missing at '{hostPath}'.");
        Assert.True(File.Exists(crossPath), $"Cross emit missing at '{crossPath}'.");
        Assert.False(
            condition: File.Exists(crossPath + ".exe"),
            userMessage: "linux/arm64 output must not gain a .exe suffix.");

        var hostLength = new FileInfo(hostPath).Length;
        var crossLength = new FileInfo(crossPath).Length;
        Assert.True(hostLength > 0, "Host binary is empty.");
        Assert.True(crossLength > 0, "Cross binary is empty.");
        Assert.NotEqual(hostLength, crossLength);

        // The cross output is genuinely a different target: ELF magic, not a copy of
        // the host PE ("MZ") binary.
        byte[] elfMagic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];
        var magic = new byte[4];
        using var stream = File.OpenRead(crossPath);
        Assert.Equal(4, stream.Read(magic, 0, 4));
        Assert.Equal(elfMagic, magic);
    }
}