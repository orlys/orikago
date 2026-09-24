namespace Orikago.LanguageService;

using Orikago.LanguageService.Definitions;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

/// <summary>
/// Locates Go ecosystem executables (<c>gopls.exe</c>, <c>dlv.exe</c>).
/// </summary>
/// <remarks>
/// Probe order: every directory on PATH, then GOBIN, then each GOPATH entry's bin\, then
/// %USERPROFILE%\go\bin. GOBIN/GOPATH are read from the process environment AND from
/// <c>go env</c> - <c>go env -w</c> persists them into Go's env file, invisible to
/// GetEnvironmentVariable, and <c>go install</c> honours exactly those values, so the probe
/// must too or an extension-suggested <c>go install ...</c> would land somewhere this class
/// never looks. <see cref="GoStrings.DlvMissing"/> quotes this probe order to the user, so a
/// change here must be mirrored there.
/// </remarks>
internal static class GoToolLocator
{
    /// <summary>
    /// Finds a Go ecosystem executable in the probe directories, in probe order.
    /// </summary>
    /// <param name="executableName">
    /// File name including .exe, e.g. <see cref="GoToolExecutables.Delve"/>.
    /// </param>
    /// <returns>Full path, or <see langword="null"/> when not found.</returns>
    public static string? Find(string executableName)
    {
        foreach (var directory in CandidateDirectories())
        {
            if (directory is not { Length: > 0 })
            {
                // An empty entry (e.g. a doubled PATH separator) names no directory
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed entry (invalid characters) - skip it.
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        // Every PATH entry, as this process sees it
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDirectory in pathVariable.Split([";"], StringSplitOptions.None))
        {
            yield return rawDirectory.Trim().Trim('"');
        }

        if (Environment.GetEnvironmentVariable("GOBIN") is { Length: > 0 } environmentGobin)
        {
            // GOBIN is set in the process environment
            yield return environmentGobin.Trim();
        }

        if (Environment.GetEnvironmentVariable("GOPATH") is { Length: > 0 } environmentGopath)
        {
            // GOPATH is set in the process environment: probe each entry's bin\
            foreach (var binDirectory in GopathBinDirectories(environmentGopath))
            {
                yield return binDirectory;
            }
        }

        // `go env` also reports values persisted with `go env -w`
        var goEnvironment = RunGoEnv("GOBIN", "GOPATH");
        if ((goEnvironment is { Length: > 0 }) && (goEnvironment[0] is { Length: > 0 } gobin))
        {
            // go env reports a GOBIN
            yield return gobin;
        }

        if ((goEnvironment is { Length: > 1 }) && (goEnvironment[1] is { Length: > 0 } gopath))
        {
            // go env reports a GOPATH: probe each entry's bin\
            foreach (var binDirectory in GopathBinDirectories(gopath))
            {
                yield return binDirectory;
            }
        }

        if (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            is { Length: > 0 } userProfile)
        {
            // The bin directory of the default GOPATH
            yield return Path.Combine(userProfile, "go", "bin");
        }
    }

    /// <summary>
    /// The bin\ directory of every non-blank entry of a GOPATH list.
    /// </summary>
    private static IEnumerable<string> GopathBinDirectories(string gopath)
    {
        foreach (var entry in gopath.Split([";"], StringSplitOptions.None))
        {
            if (entry.Trim() is { Length: > 0 } trimmed)
            {
                yield return Path.Combine(trimmed, "bin");
            }
        }
    }

    /// <summary>
    /// Runs <c>go env &lt;names&gt;</c> and returns one trimmed line per requested name.
    /// </summary>
    /// <remarks>
    /// Positional: unset values come back as empty strings. Returns an empty array when the go
    /// command is unavailable or misbehaves.
    /// </remarks>
    private static string[] RunGoEnv(params string[] names)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GoToolExecutables.Go,
                Arguments = "env " + string.Join(" ", names),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo);

            // Read both pipes without blocking on either: stderr is
            // redirected, so leaving it undrained lets "go" block writing
            // it (GOTOOLCHAIN=...+auto prints "go: downloading go1.x"
            // there) while this thread waits for stdout - which would also
            // make the 5s timeout below unreachable.
            List<string> lines = [];
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data is { } line)
                {
                    lines.Add(line);
                }
            };
            process.ErrorDataReceived += delegate
            {
                // stderr only has to be drained; its content is not used
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(5000))
            {
                // go did not answer in time (e.g. it is downloading a toolchain)
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // It exited between the timeout and the kill
                }

                ExtensionLog.Warning("go env did not finish within 5 seconds.");
                return [];
            }

            // The parameterless wait also lets the asynchronous readers deliver their last line
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                // go ran but could not report its environment
                ExtensionLog.Warning(
                    "go env exited with code " +
                    process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".");
                return [];
            }

            // Positional lines; an unset variable is an EMPTY line, so
            // empty lines are kept or the mapping would shift.
            var values = new string[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                values[i] = (i < lines.Count) ? lines[i].Trim() : string.Empty;
            }

            return values;
        }
        catch (Exception ex)
        {
            // The go command could not be run (not installed, or not on PATH)
            ExtensionLog.Warning("go env could not be run: " + ex.Message);
            return [];
        }
    }
}
