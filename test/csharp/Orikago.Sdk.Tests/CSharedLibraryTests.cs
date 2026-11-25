namespace Orikago.Sdk.Tests;

/// <summary>
/// <c>GoBuildMode=c-shared</c>: build, publish, TargetPath and clean agree on one file.
/// </summary>
public sealed class CSharedLibraryTests
{
    private const string C_SHARED =
        "<PropertyGroup><GoBuildMode>c-shared</GoBuildMode></PropertyGroup>";

    private const string C_SHARED_THROUGH_FLAGS =
        "<PropertyGroup><GoFlags>-buildmode=c-shared</GoFlags></PropertyGroup>";

    private const string COPIED_REFERENCE =
        "ReferenceOutputAssembly=\"false\" OutputItemType=\"None\" " +
        "CopyToOutputDirectory=\"PreserveNewest\"";

    private static TemporaryGoProject CreateLibrary(string body = C_SHARED)
    {
        var project = new TemporaryGoProject();
        project.WriteFile(
            relativeName: "native/native.go",
            content: """
                package main

                import "C"

                //export Add
                func Add(a, b C.int) C.int { return a + b }

                func main() {}
                """);
        project.WriteProject("native/native.goproj", body);
        return project;
    }

    private static void WriteApplication(TemporaryGoProject project, string referenceAttributes)
    {
        project.WriteFile(
            relativeName: "app/app.csproj",
            content: $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../native/native.goproj" {referenceAttributes} />
                  </ItemGroup>
                </Project>
                """);
        project.WriteFile(
            relativeName: "app/Program.cs",
            content: """
                [System.Runtime.InteropServices.DllImport("native")]
                static extern int Add(int a, int b);
                System.Console.WriteLine(Add(40, 2));
                """);
    }

    [Fact]
    public async Task Build_CsprojCopiesTheReferencedLibrary_ApplicationCallsIntoGo()
    {
        // Arrange
        using var project = CreateLibrary();
        WriteApplication(project, COPIED_REFERENCE);

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "app"], CancellationToken.None);
        var (exitCode, output) = await project.RunAsync(
            fileName: "dotnet",
            arguments: ["app/bin/Debug/net10.0/app.dll"],
            CancellationToken.None);

        // Assert
        Assert.Equal((0, "42"), (exitCode, output));
    }

    [Fact]
    public async Task Publish_CsprojCopiesTheReferencedLibrary_LibraryIsPublished()
    {
        // Arrange
        using var project = CreateLibrary();
        WriteApplication(project, COPIED_REFERENCE);

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["publish", "app", "-o", "out"],
            CancellationToken.None);

        // Assert
        var library = TemporaryGoProject.GetHostLibraryName("native");
        Assert.True(File.Exists(project.GetPath("out/" + library)));
    }

    [Fact]
    public async Task Build_CsprojReferencesTheLibraryWithoutMetadata_Succeeds()
    {
        // Arrange
        using var project = CreateLibrary();
        WriteApplication(project, referenceAttributes: "");

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "app"], CancellationToken.None);

        // Assert
        var library = TemporaryGoProject.GetHostLibraryName("native");
        Assert.True(File.Exists(project.GetPath("native/bin/Debug/" + library)));
    }

    [Theory]
    [InlineData("windows", "native.dll")]
    [InlineData("linux", "libnative.so")]
    [InlineData("darwin", "libnative.dylib")]
    public async Task TargetPath_PerGoos_IsTheLibraryGoBuildWrites(string os, string fileName)
    {
        // Arrange
        using var project = CreateLibrary();

        // Act
        var targetPath = await project.RunDotnetSuccessfullyAsync(
            arguments: [
                "msbuild",
                "native/native.goproj",
                "-p:GoOS=" + os,
                "-getProperty:TargetPath"
            ],
            CancellationToken.None);
        var outputPath = await project.RunDotnetSuccessfullyAsync(
            arguments: [
                "msbuild",
                "native/native.goproj",
                "-p:GoOS=" + os,
                "-getProperty:GoOutputPath"
            ],
            CancellationToken.None);

        // Assert
        Assert.Equal(project.GetPath("native/bin/Debug/" + fileName), targetPath);
        Assert.Equal(targetPath, outputPath);
    }

    [Theory]
    [InlineData("win-x64", "native.dll")]
    [InlineData("linux-x64", "libnative.so")]
    [InlineData("linux-arm64", "libnative.so")]
    [InlineData("osx-arm64", "libnative.dylib")]
    public async Task GoPublishOutputPath_PerRuntimeIdentifier_IsTheLibraryName(
        string runtimeIdentifier,
        string fileName)
    {
        // Arrange
        using var project = CreateLibrary();

        // Act
        var outputPath = await project.RunDotnetSuccessfullyAsync(
            arguments: [
                "msbuild",
                "native/native.goproj",
                "-t:_GoPreparePublishSettings",
                "-p:RuntimeIdentifier=" + runtimeIdentifier,
                "-getProperty:GoPublishOutputPath"
            ],
            CancellationToken.None);

        // Assert
        Assert.Equal(fileName, Path.GetFileName(outputPath));
    }

    [Fact]
    public async Task Publish_CSharedLibrary_WritesTheLibraryAndItsHeader()
    {
        // Arrange
        using var project = CreateLibrary();

        // Act
        await project.RunDotnetSuccessfullyAsync(
            arguments: ["publish", "native", "-o", "out"],
            CancellationToken.None);

        // Assert
        var library = TemporaryGoProject.GetHostLibraryName("native");
        Assert.True(File.Exists(project.GetPath("out/" + library)));
        Assert.True(File.Exists(project.GetPath("out/" + Path.ChangeExtension(library, ".h"))));
    }

    [Fact]
    public async Task Build_SecondTime_LeavesTheLibraryUntouched()
    {
        // Arrange
        using var project = CreateLibrary();
        project.WriteFile("native/go.mod", "module native\n\ngo 1.21\n");
        var library = project.GetPath(
            "native/bin/Debug/" + TemporaryGoProject.GetHostLibraryName("native"));
        await project.RunDotnetSuccessfullyAsync(["build", "native"], CancellationToken.None);
        var firstWrite = File.GetLastWriteTimeUtc(library);
        Assert.True(File.Exists(library));

        // Act
        await project.RunDotnetSuccessfullyAsync(["build", "native"], CancellationToken.None);

        // Assert
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(library));
    }

    [Theory]
    [InlineData("build")]
    [InlineData("publish")]
    public async Task Build_CSharedLibrary_WritesNothingIntoTheSourceDirectory(string verb)
    {
        // Arrange
        using var project = CreateLibrary();

        // Act
        await project.RunDotnetSuccessfullyAsync([verb, "native"], CancellationToken.None);

        // Assert
        Assert.Equal(
            expected: ["go.mod", "native.go", "native.goproj"],
            actual: project.GetFileNames("native"));
    }

    [Theory]
    [InlineData("build")]
    [InlineData("publish")]
    public async Task Build_OutputTypeLibraryWithBuildModeInGoFlags_WritesNothingIntoTheSourceDirectory(
        string verb)
    {
        // Arrange
        using var project = CreateLibrary(
            """
            <PropertyGroup>
              <OutputType>Library</OutputType>
              <GoFlags>-buildmode=c-shared</GoFlags>
            </PropertyGroup>
            """);

        // Act
        await project.RunDotnetSuccessfullyAsync([verb, "native"], CancellationToken.None);

        // Assert
        Assert.Equal(
            expected: ["go.mod", "native.go", "native.goproj"],
            actual: project.GetFileNames("native"));
    }

    [Fact]
    public async Task TargetPath_GoOutputPathOverridden_FollowsTheOverride()
    {
        // Arrange
        using var project = CreateLibrary(
            """
            <PropertyGroup>
              <GoFlags>-buildmode=c-shared</GoFlags>
              <GoOutputPath>custom/library.dll</GoOutputPath>
            </PropertyGroup>
            """);

        // Act
        var targetPath = await project.RunDotnetSuccessfullyAsync(
            arguments: ["msbuild", "native/native.goproj", "-getProperty:TargetPath"],
            CancellationToken.None);
        var targetFileName = await project.RunDotnetSuccessfullyAsync(
            arguments: ["msbuild", "native/native.goproj", "-getProperty:TargetFileName"],
            CancellationToken.None);

        // Assert
        Assert.Equal(project.GetPath("native/custom/library.dll"), targetPath);
        Assert.Equal("library.dll", targetFileName);
    }

    [Theory]
    [InlineData(C_SHARED)]
    [InlineData(C_SHARED_THROUGH_FLAGS)]
    public async Task Clean_AfterBuild_RemovesTheLibraryAndItsGeneratedHeader(string body)
    {
        // Arrange
        using var project = CreateLibrary(body);
        await project.RunDotnetSuccessfullyAsync(["build", "native"], CancellationToken.None);
        Assert.Equal(2, project.GetFileNames("native/bin/Debug").Length);

        // Act
        await project.RunDotnetSuccessfullyAsync(["clean", "native"], CancellationToken.None);

        // Assert
        Assert.Empty(project.GetFileNames("native/bin/Debug"));
    }

    [Fact]
    public async Task Clean_HandWrittenHeaderBesideTheOutput_IsKept()
    {
        // Arrange
        using var project = CreateLibrary();
        var header = project.WriteFile("native/bin/Debug/native.h", "int hand_written;\n");

        // Act
        await project.RunDotnetSuccessfullyAsync(["clean", "native"], CancellationToken.None);

        // Assert
        Assert.True(File.Exists(header));
    }

    [Theory]
    [InlineData(C_SHARED)]
    [InlineData(C_SHARED_THROUGH_FLAGS)]
    public async Task Test_CSharedLibrary_RunsTheGoTests(string body)
    {
        // Arrange
        using var project = CreateLibrary(body);
        project.WriteFile(
            relativeName: "native/native_test.go",
            content: """
                package main

                import "testing"

                func TestLogs(t *testing.T) { t.Log("the test ran") }
                """);

        // Act
        var output = await project.RunDotnetSuccessfullyAsync(
            arguments: ["test", "native/native.goproj", "-p:GoTestFlags=-v"],
            CancellationToken.None);

        // Assert
        Assert.Contains("the test ran", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_HeaderOfAnotherProjectInTheSameDirectory_IsKept()
    {
        // Arrange
        // Where the suffix is empty, "native.cli" minus its extension is "native"
        using var project = new TemporaryGoProject();
        project.WriteProject("cli/native.cli.goproj");
        var header = project.WriteFile(
            relativeName: "cli/bin/Debug/native.h",
            content: "/* Code generated by cmd/cgo; DO NOT EDIT. */\n");

        // Act
        await project.RunDotnetSuccessfullyAsync(["clean", "cli"], CancellationToken.None);

        // Assert
        Assert.True(File.Exists(header));
    }

    [Fact]
    public async Task Build_UnsupportedGoBuildMode_FailsWithAnExplanation()
    {
        // Arrange
        using var project = CreateLibrary(
            "<PropertyGroup><GoBuildMode>c-archive</GoBuildMode></PropertyGroup>");

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: ["build", "native"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("GoBuildMode 'c-archive'", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("build", C_SHARED)]
    [InlineData("publish", C_SHARED)]
    [InlineData("publish", C_SHARED_THROUGH_FLAGS)]
    public async Task Build_CgoIsOff_PointsAtCgoEnabledAndCc(string verb, string body)
    {
        // Arrange
        using var project = CreateLibrary(body);

        // Act
        // Never the host: Go turns cgo off by itself when cross-compiling
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: [verb, "native", "-p:GoOS=freebsd", "-p:GoArch=arm64"],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains("error GOCGO", output, StringComparison.Ordinal);
        Assert.Contains("CGO_ENABLED=1;CC=", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_CustomCommandFails_DoesNotBlameTheLocalCgo()
    {
        // Arrange
        using var project = CreateLibrary();

        // Act
        var (exitCode, output) = await project.RunDotnetAsync(
            arguments: [
                "build",
                "native",
                "-p:GoOS=freebsd",
                "-p:GoArch=arm64",
                "-p:GoBuildCommand=go build -x"
            ],
            CancellationToken.None);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain("GOCGO", output, StringComparison.Ordinal);
    }
}