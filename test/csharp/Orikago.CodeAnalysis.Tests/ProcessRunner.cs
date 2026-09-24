namespace Orikago.CodeAnalysis.Tests;

using System.Diagnostics;
using System.Text;

/// <summary>Runs an executable with stdout/stderr capture; fails the test on timeout.</summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> to completion and captures its UTF-8 stdout and stderr.
    /// </summary>
    /// <remarks>
    /// Both pipes are drained concurrently (docs/pitfalls.md #20: go writes its progress to
    /// stderr, and a full pipe blocks the child). A child that outlives
    /// <paramref name="timeout"/> is killed together with its descendants and reported as a
    /// <see cref="TimeoutException"/>.
    /// </remarks>
    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Redirect both output pipes as UTF-8
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (workingDirectory is not null)
        {
            // Run inside the requested directory (the module root for go commands)
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");

        // Drain both pipes concurrently so neither can fill up and block the child
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        // Wait for exit within the budget; a hung child must not hang the test run
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The child outlived the budget: stop the whole tree and report the timeout
            KillProcessTree(process);
            throw new TimeoutException(FormattableString.Invariant(
                $"'{fileName} {string.Join(' ', arguments)}' did not exit within {timeout.TotalSeconds} s."));
        }

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort: the child may already have exited between the timeout and the kill
        }
    }
}