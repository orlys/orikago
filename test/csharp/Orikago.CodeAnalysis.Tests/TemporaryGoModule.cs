namespace Orikago.CodeAnalysis.Tests;

using System.Globalization;
using System.Text;

/// <summary>
/// A throwaway Go module on disk: a temp directory containing a go.mod plus any
/// source files the test writes.
/// </summary>
/// <remarks>
/// Files are written with LF line endings and the exact bytes given, because the tests
/// assert 1-based line/column positions that were verified against go/parser + go/types
/// (go1.21) for these exact sources.
/// </remarks>
internal sealed class TemporaryGoModule : IDisposable
{
    public TemporaryGoModule(string moduleName = "orikagotestmod")
    {
        Directory = Path.Combine(
            path1: Path.GetTempPath(),
            path2: "orikago-tests",
            path3: Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        System.IO.Directory.CreateDirectory(Directory);
        WriteFile("go.mod", "module " + moduleName + "\n\ngo 1.21\n");
    }

    public string Directory { get; }

    /// <summary>
    /// Writes exact bytes (UTF-8, no BOM, LF only) and returns the absolute path.
    /// </summary>
    public string WriteFile(string relativeName, string content)
    {
        var path = Path.Combine(Directory, relativeName);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    /// <summary>
    /// Runs the real <c>go</c> command inside this module and returns its exit code and
    /// combined output.
    /// </summary>
    /// <remarks>
    /// Used to establish the ground truth a test compares against: "go build accepts this
    /// module" is the yardstick GetDiagnostics must agree with.
    /// </remarks>
    public async Task<(int ExitCode, string Output)> RunGoAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var (exitCode, standardOutput, standardError) = await ProcessRunner.RunAsync(
            fileName: "go",
            arguments,
            workingDirectory: Directory,
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken);
        return (exitCode, (standardOutput + standardError).Trim());
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