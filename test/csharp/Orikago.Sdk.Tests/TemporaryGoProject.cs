namespace Orikago.Sdk.Tests;

using System.Globalization;
using System.Reflection;
using System.Text;
using Orikago.CodeAnalysis.Tests;

/// <summary>
/// A throwaway directory for the projects a test writes, outside any workspace.
/// </summary>
/// <remarks>
/// Projects import the SDK sources of this repository, and every command is the real
/// <c>dotnet</c> / <c>go</c>. The c-shared tests additionally need a C compiler on PATH.
/// </remarks>
internal sealed class TemporaryGoProject : IDisposable
{
    public TemporaryGoProject()
    {
        Directory = Path.Combine(
            path1: Path.GetTempPath(),
            path2: "orikago-sdk-tests",
            path3: Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    /// <summary>Environment variables added for every command this project runs.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.Ordinal);

    /// <summary>The file name a c-shared build should get on this machine.</summary>
    public static string GetHostLibraryName(string name)
    {
        return true switch
        {
            _ when OperatingSystem.IsWindows() => name + ".dll",
            _ when OperatingSystem.IsMacOS() => "lib" + name + ".dylib",
            _ => "lib" + name + ".so",
        };
    }

    public static string GetHostExecutableName(string name)
    {
        return OperatingSystem.IsWindows() ? name + ".exe" : name;
    }

    public string GetPath(string relativeName)
    {
        return Path.GetFullPath(Path.Combine(Directory, relativeName));
    }

    /// <summary>The files directly inside a directory, sorted by name.</summary>
    public string[] GetFileNames(string relativeDirectory)
    {
        return System.IO.Directory
            .GetFiles(GetPath(relativeDirectory))
            .Select(x => Path.GetFileName(x))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Writes a file (UTF-8, no BOM, LF only), creating its directory.</summary>
    public string WriteFile(string relativeName, string content)
    {
        var path = GetPath(relativeName);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n")));
        return path;
    }

    /// <summary>
    /// Writes a .goproj that imports the SDK sources around <paramref name="body"/>.
    /// </summary>
    public string WriteProject(string relativeName, string body = "")
    {
        var sdkDirectory = typeof(TemporaryGoProject)
            .Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => string.Equals(x.Key, "SdkDirectory", StringComparison.Ordinal))
            .Value;
        return WriteFile(
            relativeName,
            content: $"""
                <Project>
                  <Import Project="{sdkDirectory}/Sdk.props" />
                  {body}
                  <Import Project="{sdkDirectory}/Sdk.targets" />
                </Project>
                """);
    }

    public async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var (exitCode, standardOutput, standardError) = await ProcessRunner.RunAsync(
            fileName,
            arguments,
            workingDirectory: Directory,
            environmentVariables: EnvironmentVariables,
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken);
        return (exitCode, (standardOutput + standardError).Trim());
    }

    /// <summary>Runs an MSBuild-backed <c>dotnet</c> verb.</summary>
    /// <remarks>
    /// No lingering build servers: they would inherit the redirected pipes and keep the
    /// read open after the command itself has exited.
    /// </remarks>
    public Task<(int ExitCode, string Output)> RunDotnetAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            fileName: "dotnet",
            arguments: [.. arguments, "-nodeReuse:false", "-p:UseSharedCompilation=false"],
            cancellationToken);
    }

    /// <summary>
    /// Runs an MSBuild-backed <c>dotnet</c> verb that the test needs to succeed.
    /// </summary>
    public async Task<string> RunDotnetSuccessfullyAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunDotnetAsync(arguments, cancellationToken);
        Assert.True(
            condition: exitCode == 0,
            userMessage: $"'dotnet {string.Join(' ', arguments)}' failed: {output}");
        return output;
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; leaked temp dirs are not a test failure.
        }
    }
}