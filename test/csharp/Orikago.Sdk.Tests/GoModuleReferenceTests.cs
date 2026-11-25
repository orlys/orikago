namespace Orikago.Sdk.Tests;

/// <summary>GoModuleReference leaves go.mod and go.sum the way "go mod tidy" wants them.</summary>
/// <remarks>Resolves real modules through the configured GOPROXY.</remarks>
public sealed class GoModuleReferenceTests
{
    private const string UUID = "github.com/google/uuid";

    private const string IMPORTING_SOURCE =
        """
        package main

        import "github.com/google/uuid"

        func main() { println(uuid.Nil.String()) }
        """;

    private const string EMPTY_SOURCE =
        """
        package main

        func main() {}
        """;

    private static void WriteProject(TemporaryGoProject project, string version)
    {
        project.WriteProject(
            relativeName: "demo/demo.goproj",
            body: $"""
                <ItemGroup><GoModuleReference Include="{UUID}" Version="{version}" /></ItemGroup>
                """);
    }

    [Fact]
    public async Task Build_VersionChanged_LeavesTheModuleTidy()
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", IMPORTING_SOURCE);
        WriteProject(project, "v1.5.0");
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Act
        WriteProject(project, "v1.6.0");
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);
        var (exitCode, difference) = await project.RunAsync(
            fileName: "go",
            arguments: ["-C", "demo", "mod", "tidy", "-diff"],
            CancellationToken.None);

        // Assert
        Assert.Equal((0, ""), (exitCode, difference));
        Assert.Contains(
            UUID + " v1.6.0 h1:",
            File.ReadAllText(project.GetPath("demo/go.sum")),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v1.6.0")]
    [InlineData("v1.6")]
    public async Task Build_ReferenceNotImportedYet_IsKeptAndGoModStaysUntouched(string version)
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", EMPTY_SOURCE);
        WriteProject(project, version);
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);
        var goMod = project.GetPath("demo/go.mod");
        var firstWrite = File.GetLastWriteTimeUtc(goMod);

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Assert
        Assert.Contains(UUID + " v1.6.0", File.ReadAllText(goMod), StringComparison.Ordinal);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(goMod));
    }

    [Fact]
    public async Task Build_SomeReferencesWithoutVersion_ResolvesAllOfThemOnce()
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", IMPORTING_SOURCE);
        project.WriteProject(
            relativeName: "demo/demo.goproj",
            body: $"""
                <ItemGroup>
                  <GoModuleReference Include="{UUID}" Version="v1.5.0" />
                  <GoModuleReference Include="golang.org/x/sync" />
                </ItemGroup>
                """);
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);
        var goMod = project.GetPath("demo/go.mod");
        var resolved = File.ReadAllText(goMod);
        var firstWrite = File.GetLastWriteTimeUtc(goMod);

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Assert
        Assert.Contains(UUID + " v1.5.0", resolved, StringComparison.Ordinal);
        Assert.Contains("golang.org/x/sync v", resolved, StringComparison.Ordinal);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(goMod));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("<PropertyGroup><GoModTidy>false</GoModTidy></PropertyGroup>", true)]
    public async Task Build_UnusedRequire_IsTidiedAwayUnlessTidyIsOff(string body, bool kept)
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", IMPORTING_SOURCE);
        var goMod = project.WriteFile(
            relativeName: "demo/go.mod",
            content: "module demo\n\ngo 1.21\n\nrequire golang.org/x/sync v0.7.0\n");
        project.WriteProject(
            relativeName: "demo/demo.goproj",
            body: $"""
                {body}
                <ItemGroup><GoModuleReference Include="{UUID}" Version="v1.6.0" /></ItemGroup>
                """);

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Assert
        var requires = File.ReadAllText(goMod);
        Assert.Contains(UUID + " v1.6.0", requires, StringComparison.Ordinal);
        Assert.Equal(kept, requires.Contains("golang.org/x/sync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Build_ModuleCannotBeTidied_WarnsAndStillBuilds()
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", IMPORTING_SOURCE);

        // "go mod tidy" considers every build tag, "go build" only the active ones
        project.WriteFile(
            relativeName: "demo/inactive.go",
            content: """
                //go:build neveractive

                package main

                import _ "example.invalid/missing"
                """);
        WriteProject(project, "v1.6.0");

        // Act
        var output = await project.RunDotnetSuccessfullyAsync(
            arguments: ["build", "demo"],
            CancellationToken.None);

        // Assert
        Assert.Contains("warning GOTIDY", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoRestoreModules_InvokedAlone_UpdatesGoModWithoutBuilding()
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", IMPORTING_SOURCE);
        WriteProject(project, "v1.6.0");

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["build", "demo", "-t:GoRestoreModules"],
            CancellationToken.None);

        // Assert
        Assert.Contains(
            UUID + " v1.6.0",
            File.ReadAllText(project.GetPath("demo/go.mod")),
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(project.GetPath("demo/bin")));
    }
}