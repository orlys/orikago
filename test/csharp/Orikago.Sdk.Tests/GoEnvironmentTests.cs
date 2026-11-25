namespace Orikago.Sdk.Tests;

/// <summary><c>GoEnvironment</c> reaches every go command, build and publish alike.</summary>
public sealed class GoEnvironmentTests
{
    private const string CGO_OFF =
        "<PropertyGroup><GoEnvironment>CGO_ENABLED=0;CC=no such cc</GoEnvironment></PropertyGroup>";

    private static TemporaryGoProject CreateCgoProbe(string body = "")
    {
        var project = new TemporaryGoProject();
        project.WriteFile(
            relativeName: "probe/main.go",
            content: """
                package main

                func main() { println(state) }
                """);
        project.WriteFile(
            relativeName: "probe/on.go",
            content: """
                //go:build cgo

                package main

                const state = "cgo on"
                """);
        project.WriteFile(
            relativeName: "probe/off.go",
            content: """
                //go:build !cgo

                package main

                const state = "cgo off"
                """);
        project.WriteFile(
            relativeName: "probe/main_test.go",
            content: """
                package main

                import "testing"

                func TestState(t *testing.T) { t.Log(state) }
                """);
        project.WriteProject("probe/probe.goproj", body);
        return project;
    }

    [Theory]
    [InlineData("build")]
    [InlineData("publish")]
    public async Task Build_GoEnvironment_IsPassedToGoBuild(string verb)
    {
        // Arrange
        using var project = CreateCgoProbe(CGO_OFF);

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: [verb, "probe", "-o", "out"],
            CancellationToken.None);
        var (_, output) = await project.RunAsync(
            fileName: project.GetPath("out/" + TemporaryGoProject.GetHostExecutableName("probe")),
            arguments: [],
            CancellationToken.None);

        // Assert
        Assert.Equal("cgo off", output);
    }

    [Fact]
    public async Task Build_GoEnvironmentChanged_Rebuilds()
    {
        // Arrange
        using var project = CreateCgoProbe();
        var executable = project.GetPath(
            "probe/bin/Debug/" + TemporaryGoProject.GetHostExecutableName("probe"));
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["build", "probe", "-p:GoEnvironment=CGO_ENABLED=1"],
            CancellationToken.None);
        var (_, before) = await project.RunAsync(executable, [], CancellationToken.None);

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["build", "probe", "-p:GoEnvironment=CGO_ENABLED=0"],
            CancellationToken.None);
        var (_, after) = await project.RunAsync(executable, [], CancellationToken.None);

        // Assert
        Assert.Equal(("cgo on", "cgo off"), (before, after));
    }

    [Fact]
    public async Task Test_GoEnvironment_IsPassedToGoTest()
    {
        // Arrange
        using var project = CreateCgoProbe(CGO_OFF);

        // Act
        var output = await project.RunDotnetSuccessfullyAsync(
            arguments: ["test", "probe/probe.goproj", "-p:GoTestFlags=-v"],
            CancellationToken.None);

        // Assert
        Assert.Contains("cgo off", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vet_GoEnvironment_IsPassedToGoVet()
    {
        // Arrange
        using var project = CreateCgoProbe(CGO_OFF);
        project.WriteFile(
            relativeName: "probe/faulty.go",
            content: """
                //go:build !cgo

                package main

                func faulty(x int) int {
                	x = x
                	return x
                }
                """);

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "probe", "-p:RunGoVet=true"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("faulty.go(6,2): error GOVET", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GOOS=linux", "GoOS")]
    [InlineData("CGO_ENABLED=0; GOARCH=arm64", "GoArch")]
    public async Task Build_TargetInGoEnvironment_FailsAndNamesTheProperty(
        string environment,
        string property)
    {
        // Arrange
        using var project = CreateCgoProbe(
            $"<PropertyGroup><GoEnvironment>{environment}</GoEnvironment></PropertyGroup>");

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "probe"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains(property, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_GoEnvironmentTurnsTheWorkspaceOff_ProjectDoesNotJoinIt()
    {
        // Arrange
        using var project = CreateCgoProbe(
            "<PropertyGroup><GoEnvironment>GOWORK=off</GoEnvironment></PropertyGroup>");
        var workspace = project.WriteFile("go.work", "go 1.21\n");

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "probe"], CancellationToken.None);

        // Assert
        Assert.Equal("go 1.21\n", File.ReadAllText(workspace));
    }

    [Fact]
    public async Task Restore_GoEnvironment_IsPassedToGoGet()
    {
        // Arrange
        using var project = CreateCgoProbe(
            """
            <PropertyGroup><GoEnvironment>GOPROXY=off</GoEnvironment></PropertyGroup>
            <ItemGroup>
              <GoModuleReference Include="example.com/never/published" Version="v1.0.0" />
            </ItemGroup>
            """);

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "probe"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("GOPROXY=off", output, StringComparison.Ordinal);
    }
}