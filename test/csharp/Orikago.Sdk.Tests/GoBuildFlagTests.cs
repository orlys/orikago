namespace Orikago.Sdk.Tests;

/// <summary>The knobs on the go build command line.</summary>
public sealed class GoBuildFlagTests
{
    private static TemporaryGoProject CreateExecutable(string body = "")
    {
        var project = new TemporaryGoProject();
        project.WriteFile(
            relativeName: "demo/main.go",
            content: """
                package main

                func main() { println(mark) }
                """);
        project.WriteFile(
            relativeName: "demo/plain.go",
            content: """
                //go:build !wrapped

                package main

                const mark = "plain"
                """);
        project.WriteFile(
            relativeName: "demo/wrapped.go",
            content: """
                //go:build wrapped

                package main

                const mark = "wrapped"
                """);
        project.WriteProject("demo/demo.goproj", body);
        return project;
    }

    private static string GetExecutable(TemporaryGoProject project, string directory)
    {
        return project.GetPath(
            directory + "/" + TemporaryGoProject.GetHostExecutableName("demo"));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("<PropertyGroup><Optimize>true</Optimize></PropertyGroup>", false)]
    public async Task Build_Debug_DisablesOptimizationUnlessOptimizeIsSet(string body, bool disabled)
    {
        // Arrange
        using var project = CreateExecutable(body);

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);
        var (_, buildInformation) = await project.RunAsync(
            fileName: "go",
            arguments: ["version", "-m", GetExecutable(project, "demo/bin/Debug")],
            CancellationToken.None);

        // Assert
        Assert.Contains("-buildmode=exe", buildInformation, StringComparison.Ordinal);
        Assert.Equal(disabled, buildInformation.Contains("-N -l", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("build")]
    [InlineData("publish")]
    public async Task Build_GoBuildCommand_ReplacesGoBuild(string verb)
    {
        // Arrange
        using var project = CreateExecutable(
            "<PropertyGroup><GoBuildCommand>go build -tags wrapped</GoBuildCommand></PropertyGroup>");

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: [verb, "demo", "-o", "out"],
            CancellationToken.None);
        var (_, output) = await project.RunAsync(
            fileName: GetExecutable(project, "out"),
            arguments: [],
            CancellationToken.None);

        // Assert
        Assert.Equal("wrapped", output);
    }

    [Fact]
    public async Task Build_LibraryWithOutputInGoFlags_WritesThatOutput()
    {
        // Arrange
        using var project = CreateExecutable(
            """
            <PropertyGroup>
              <OutputType>Library</OutputType>
              <GoFlags>-o out/chosen.bin</GoFlags>
            </PropertyGroup>
            """);

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Assert
        Assert.True(File.Exists(project.GetPath("demo/out/chosen.bin")));
    }

    [Fact]
    public async Task Build_GoProjectReferencesGoExecutable_CopiesIt()
    {
        // Arrange
        using var project = CreateExecutable(
            """<ItemGroup><ProjectReference Include="../helper/helper.goproj" /></ItemGroup>""");
        project.WriteFile("helper/main.go", "package main\n\nfunc main() {}\n");
        project.WriteProject("helper/helper.goproj");

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Assert
        var helper = TemporaryGoProject.GetHostExecutableName("helper");
        Assert.Contains(helper, project.GetFileNames("demo/bin/Debug"));
    }

    [Fact]
    public async Task Build_CompileError_ReportsTheGoDiagnosticOnly()
    {
        // Arrange
        using var project = CreateExecutable();
        project.WriteFile("demo/broken.go", "package main\n\nvar x int = \"no\"\n");

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "demo"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("broken.go(3,13): error GOBUILD", output, StringComparison.Ordinal);
        Assert.Contains("1 Error(s)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_GoBuildCommandChanged_Rebuilds()
    {
        // Arrange
        using var project = CreateExecutable();
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["build", "demo", "-p:GoBuildCommand=go build -tags wrapped"],
            CancellationToken.None);
        var (_, output) = await project.RunAsync(
            fileName: GetExecutable(project, "demo/bin/Debug"),
            arguments: [],
            CancellationToken.None);

        // Assert
        Assert.Equal("wrapped", output);
    }

    [Fact]
    public async Task Build_GoflagsEnvironmentVariable_IsLeftToGo()
    {
        // Arrange
        // -count only exists for "go test": go skips it for "go build", the command line must not
        using var project = CreateExecutable();
        project.EnvironmentVariables["GOFLAGS"] = "-count=1";

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "demo"], CancellationToken.None);

        // Assert
        Assert.True(File.Exists(GetExecutable(project, "demo/bin/Debug")));
    }

    [Fact]
    public async Task Publish_GoosEnvironmentVariable_DoesNotOverrideTheRuntimeIdentifier()
    {
        // Arrange
        using var project = CreateExecutable();
        project.EnvironmentVariables["GOOS"] = "linux";
        project.EnvironmentVariables["GOARCH"] = "arm64";

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["publish", "demo", "-r", "win-x64", "-o", "out"],
            CancellationToken.None);
        var (_, buildInformation) = await project.RunAsync(
            fileName: "go",
            arguments: ["version", "-m", project.GetPath("out/demo.exe")],
            CancellationToken.None);

        // Assert
        Assert.Contains("GOOS=windows", buildInformation, StringComparison.Ordinal);
        Assert.Contains("GOARCH=amd64", buildInformation, StringComparison.Ordinal);
    }
}