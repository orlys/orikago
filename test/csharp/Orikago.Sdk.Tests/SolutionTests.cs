namespace Orikago.Sdk.Tests;

public sealed class SolutionTests
{
    [Theory]
    [InlineData("slnx")]
    [InlineData("sln")]
    public async Task SlnAdd_GoProject_IsAddedAndBuilds(string format)
    {
        // Arrange
        using var project = new TemporaryGoProject();
        project.WriteFile(
            relativeName: "demo/main.go",
            content: """
                package main

                func main() {}
                """);
        project.WriteProject("demo/demo.goproj");
        await project.RunAsync(
            fileName: "dotnet",
            arguments: ["new", "sln", "-n", "All", "--format", format],
            CancellationToken.None);

        // Act
        var (exitCode, output) = await project.RunAsync(
            fileName: "dotnet",
            arguments: ["sln", "All." + format, "add", "demo/demo.goproj"],
            CancellationToken.None);
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["build", "All." + format],
            CancellationToken.None);

        // Assert
        Assert.True(exitCode == 0, output);
        var executable = TemporaryGoProject.GetHostExecutableName("demo");
        Assert.True(File.Exists(project.GetPath("demo/bin/Debug/" + executable)));
    }
}