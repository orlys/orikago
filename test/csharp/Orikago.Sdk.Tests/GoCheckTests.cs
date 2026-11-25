namespace Orikago.Sdk.Tests;

/// <summary>The opt-in checks: go vet per tag set, and gofmt.</summary>
public sealed class GoCheckTests
{
    private const string MAIN_SOURCE =
        """
        package main

        func main() {}

        """;

    private const string OTHER_DEFINED =
        "<PropertyGroup><DefineConstants>other</DefineConstants></PropertyGroup>";

    private const string TAG_SETS =
        """
        <ItemGroup>
          <GoVetTagSet Include="plain" />
          <GoVetTagSet Include="feature" Tags="feature; other" />
        </ItemGroup>
        """;

    private static TemporaryGoProject CreateVetFinding(string body, string constraint)
    {
        // A self-assignment compiles, but go vet reports it
        var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", MAIN_SOURCE);
        project.WriteFile(
            relativeName: "demo/faulty.go",
            content: $$"""
                //go:build {{constraint}}

                package main

                func faulty(x int) int {
                	x = x
                	return x
                }
                """);
        project.WriteProject("demo/demo.goproj", body);
        return project;
    }

    [Theory]
    [InlineData("!other")]
    [InlineData("feature && other")]
    public async Task GoVet_TagSets_EverySetIsVetted(string constraint)
    {
        // Arrange
        using var project = CreateVetFinding(OTHER_DEFINED + TAG_SETS, constraint);

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "demo", "-p:RunGoVet=true"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("faulty.go(6,2): error GOVET", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoVet_SeveralTagSetsFail_AllAreReported()
    {
        // Arrange
        using var project = CreateVetFinding(OTHER_DEFINED + TAG_SETS, "!other");
        project.WriteFile(
            relativeName: "demo/second.go",
            content: """
                //go:build feature

                package main

                func second(x int) int {
                	x = x
                	return x
                }
                """);

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "demo", "-p:RunGoVet=true"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("faulty.go(6,2): error GOVET", output, StringComparison.Ordinal);
        Assert.Contains("second.go(6,2): error GOVET", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("!other", true)]
    [InlineData("other", false)]
    public async Task GoVet_NoTagSets_VetsDefineConstantsOnly(string constraint, bool succeeds)
    {
        // Arrange
        using var project = CreateVetFinding(OTHER_DEFINED, constraint);

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "demo", "-p:RunGoVet=true"],
            CancellationToken.None);

        // Assert
        Assert.True(succeeds == (exitCode == 0), output);
    }

    [Theory]
    [InlineData("demo/sub/messy.go", "true", false)]
    [InlineData("demo/sub/messy.go", "false", true)]
    [InlineData("demo/sub/中文messy.go", "true", false)]
    [InlineData("demo/vendor/example.com/messy.go", "true", true)]
    [InlineData("demo/obj/messy.go", "true", true)]
    public async Task GoFmt_UnformattedFile_FailsOnlyWhenEnabledAndOwned(
        string messyFile,
        string enabled,
        bool succeeds)
    {
        // Arrange
        // GoFlags: the vendor directory here only holds a file to skip
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", MAIN_SOURCE);
        project.WriteFile(messyFile, "package  sub\n");
        project.WriteProject(
            relativeName: "demo/demo.goproj",
            body: "<PropertyGroup><GoFlags>-mod=mod</GoFlags></PropertyGroup>");

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "demo", "-p:RunGoFmt=" + enabled],
            CancellationToken.None);

        // Assert
        Assert.True(succeeds == (exitCode == 0), output);
        Assert.Equal(
            expected: !succeeds,
            actual: output.Contains("messy.go : error GOFMT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("demo/vendor/example.com/broken.go", "package\n", true)]
    [InlineData("demo/testdata/messy.go", "package  testdata\n", true)]
    [InlineData("demo/_skipped/messy.go", "package  skipped\n", true)]
    [InlineData("demo/inactive.go", "//go:build neveractive\n\npackage\n", false)]
    public async Task GoFmt_FileOutsideThePackagesOrUnparsable_FailsOnlyWhenOwned(
        string file,
        string content,
        bool succeeds)
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", MAIN_SOURCE);
        project.WriteFile(file, content);
        project.WriteProject(
            relativeName: "demo/demo.goproj",
            body: "<PropertyGroup><GoFlags>-mod=mod</GoFlags></PropertyGroup>");

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "demo", "-p:RunGoFmt=true"],
            CancellationToken.None);

        // Assert
        Assert.True(succeeds == (exitCode == 0), output);
        Assert.Equal(
            expected: !succeeds,
            actual: output.Contains("inactive.go : error GOFMT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-p:RunGoVet=true")]
    [InlineData("-p:RunGoFmt=true")]
    public async Task Check_Fails_ReferencingProjectIsNotBuilt(string check)
    {
        // Arrange
        // Both checks fail on this file: a self-assignment, indented with spaces
        using var project = CreateVetFinding(TAG_SETS, "!other");
        project.WriteFile(
            relativeName: "demo/messy.go",
            content: "package main\n\nfunc messy(x int) int {\n  x = x\n  return x\n}\n");
        project.WriteFile(
            relativeName: "app/app.csproj",
            content: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../demo/demo.goproj" ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Project>
                """);

        // Act
        var (exitCode, _) = await project.RunDotnetAsync(
            arguments: ["build", "app", check],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.False(File.Exists(project.GetPath("app/bin/Debug/net10.0/app.dll")));
    }

    [Fact]
    public async Task GoFmt_SeveralUnformattedFiles_AllAreReported()
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile("demo/main.go", MAIN_SOURCE);
        project.WriteFile("demo/one.go", "package  main\n");
        project.WriteFile("demo/two.go", "package  main\n");
        project.WriteProject("demo/demo.goproj");

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "demo", "-p:RunGoFmt=true"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("one.go : error GOFMT", output, StringComparison.Ordinal);
        Assert.Contains("two.go : error GOFMT", output, StringComparison.Ordinal);
    }
}