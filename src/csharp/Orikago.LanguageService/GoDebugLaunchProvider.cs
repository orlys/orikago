namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;

using Orikago.LanguageService.Definitions;

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// F5 for .goproj projects: hands the built Go executable to the delve DAP engine.
/// </summary>
/// <remarks>
/// <para>
/// The engine is dlv dap via Visual Studio's Debug Adapter Host; its registration is in
/// goproj.pkgdef. Ctrl+F5 (NoDebug) runs the executable directly without delve.
/// Breakpoints, stepping, locals and goroutine stacks all flow through DAP once the engine
/// attaches - nothing per-feature to do here.
/// </para>
/// <para>
/// Exported BOTH ways because the pipelines differ: with the
/// LaunchProfiles capability (which the managed design-time targets bring,
/// and whose subsystem owns the project's F5 plumbing - removing it makes
/// Debug.Start unavailable outright), LaunchProfilesDebugLaunchProvider is
/// the only IDebugLaunchProvider ever consulted, and it delegates to the
/// highest-Order IDebugProfileLaunchTargetsProvider whose SupportsProfile
/// says yes. The plain IDebugLaunchProvider export covers the no-profiles
/// pipeline for completeness.
/// </para>
/// </remarks>
[Export(typeof(IDebugLaunchProvider))]
[Export(typeof(IDebugProfileLaunchTargetsProvider))]
[AppliesTo(ProjectCapabilityNames.Orikago)]
// Must outrank every built-in provider or F5 silently goes elsewhere
[Order(9999999)]
internal sealed class GoDebugLaunchProvider :
    DebugLaunchProviderBase,
    IDebugProfileLaunchTargetsProvider
{
    [ImportingConstructor]
    public GoDebugLaunchProvider(ConfiguredProject configuredProject)
        : base(configuredProject)
    {
    }

    /// <summary>
    /// Every profile of a .goproj debugs the Go binary via delve.
    /// </summary>
    public bool SupportsProfile(ILaunchProfile profile)
    {
        return true;
    }

    public Task OnBeforeLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
    {
        return Task.CompletedTask;
    }

    public Task OnAfterLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(
        DebugLaunchOptions launchOptions,
        ILaunchProfile profile)
    {
        return QueryDebugTargetsAsync(launchOptions);
    }

    public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
    {
        return Task.FromResult(true);
    }

    public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(
        DebugLaunchOptions launchOptions)
    {
        // The built binary, its arguments and the project directory come from the project
        var propertiesProvider = ConfiguredProject.Services.ProjectPropertiesProvider ??
            throw new InvalidOperationException("The Go project exposes no project properties.");
        var properties = propertiesProvider.GetCommonProperties();
        var executable = await properties.GetEvaluatedPropertyValueAsync("GoOutputPath");
        var arguments = await properties.GetEvaluatedPropertyValueAsync("StartArguments");
        var workingDirectory =
            await properties.GetEvaluatedPropertyValueAsync("MSBuildProjectDirectory");

        if ((executable is not { Length: > 0 }) || !File.Exists(executable))
        {
            // The project has not been built yet, so there is nothing to launch
            throw new FileNotFoundException(
                GoStrings.GoExecutableMissing(executable ?? "(GoOutputPath?)"));
        }

        var commandLine = arguments ?? string.Empty;
        var settings = new DebugLaunchSettings(launchOptions)
        {
            LaunchOperation = DebugLaunchOperation.CreateProcess,
            Executable = executable,
            Arguments = commandLine,
            CurrentDirectory = workingDirectory,
        };

        if ((launchOptions & DebugLaunchOptions.NoDebug) == DebugLaunchOptions.NoDebug)
        {
            // Ctrl+F5: plain run, no delve involved.
            settings.LaunchDebugEngineGuid = DebuggerEngines.NativeOnlyEngine;
            return [settings];
        }

        var dlv = GoToolLocator.Find(GoToolExecutables.Delve) ??
            throw new FileNotFoundException(GoStrings.DlvMissing);

        // dlv dap speaks TCP only - it cannot be spawned by the Debug
        // Adapter Host over stdio. So it is started HERE, and the port it
        // reports is handed to the host via "$debugServer": the host then
        // connects instead of launching an adapter process itself. The CPS
        // launch contract passes no cancellation token to forward.
        var port = await DelveServer.StartAsync(
            dlvPath: dlv,
            workingDirectory: workingDirectory,
            visibleConsole: true,
            cancellationToken: CancellationToken.None);

        settings.LaunchDebugEngineGuid = new Guid(DelveEngine.GuidString);

        // The remaining (non-$) properties are what the host forwards as
        // the DAP launch-request arguments: dlv exec mode debugs the
        // already-built binary (Debug builds compile with -gcflags
        // "all=-N -l", so symbols and locals are intact).
        settings.Options = BuildLaunchOptions(port, executable, commandLine, workingDirectory);
        return [settings];
    }

    // dlv dap server startup/reaping lives in DelveServer (shared with the
    // attach path in GoAdapterLauncher).

    private static string BuildLaunchOptions(
        int debugServerPort,
        string executable,
        string arguments,
        string workingDirectory)
    {
        // Hand-rolled JSON (no serializer dependency): every value goes
        // through JsonString below, so paths with backslashes and quotes
        // survive.
        var json = new StringBuilder();
        json.Append("{");
        json
            .Append("\"$debugServer\":")
            .Append(debugServerPort.ToString(CultureInfo.InvariantCulture))
            .Append(',');
        json.Append("\"type\":\"go\",");
        json.Append("\"request\":\"launch\",");
        json.Append("\"mode\":\"exec\",");
        json.Append("\"stopOnEntry\":false,");
        // Keep the Threads window to USER goroutines; runtime internals
        // otherwise drown it (dlv >= 1.7.3).
        json.Append("\"hideSystemGoroutines\":true,");
        json.Append("\"program\":").Append(JsonString(executable)).Append(',');
        json.Append("\"cwd\":").Append(JsonString(workingDirectory));
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            // dlv's DAP "args" field is an array, one element per argument
            json.Append(",\"args\":[");
            var parts = SplitCommandLine(arguments);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    json.Append(',');
                }

                json.Append(JsonString(parts[i]));
            }

            json.Append(']');
        }

        json.Append("}");
        return json.ToString();
    }

    private static string JsonString(string? value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value ?? string.Empty)
        {
            switch (c)
            {
                case '"':
                {
                    builder.Append("\\\"");
                    break;
                }
                case '\\':
                {
                    builder.Append("\\\\");
                    break;
                }
                case '\n':
                {
                    builder.Append("\\n");
                    break;
                }
                case '\r':
                {
                    builder.Append("\\r");
                    break;
                }
                case '\t':
                {
                    builder.Append("\\t");
                    break;
                }
                default:
                {
                    if (c < 0x20)
                    {
                        // Any other control character is written as a \u escape
                        builder
                            .Append("\\u")
                            .Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                    }

                    builder.Append(c);
                    break;
                }
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>
    /// Splits <c>StartArguments</c> the way a shell would: whitespace-separated, double quotes
    /// group.
    /// </summary>
    /// <remarks>
    /// dlv's DAP <c>args</c> field wants an array, not a single command line.
    /// </remarks>
    private static string[] SplitCommandLine(string commandLine)
    {
        List<string> parts = [];
        var current = new StringBuilder();
        var inQuotes = default(bool);
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                // A quote toggles grouping and is not part of the argument
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                // Unquoted whitespace ends the current argument
                if (current is { Length: > 0 })
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current is { Length: > 0 })
        {
            // The last argument runs to the end of the command line
            parts.Add(current.ToString());
        }

        return [.. parts];
    }
}
