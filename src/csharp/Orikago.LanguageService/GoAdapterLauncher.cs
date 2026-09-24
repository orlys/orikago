namespace Orikago.LanguageService;

using Microsoft.VisualStudio.Debugger.DebugAdapterHost.Interfaces;

using Orikago.LanguageService.Definitions;

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Debug Adapter Host adapter launcher for the Go engine; its job is the ATTACH path.
/// </summary>
/// <remarks>
/// Registered in goproj.pkgdef as an extensibility object of the Go engine (the numbered
/// values under its <c>ExtensibilityObjects</c> key; the <c>AdapterLauncher</c> metric is
/// the deprecated spelling and is not used). 偵錯 → 附加至處理序 selects a PID and the code
/// type "Go Debugger (Delve)", the host asks this class to prepare the session, and it starts
/// <c>dlv dap</c>, then rewrites the launch JSON to
/// <c>{"$debugServer":port, "request":"attach", "mode":"local", "processId":pid}</c> - the host
/// connects to the TCP port instead of spawning an adapter (dlv has no stdio mode), same
/// trick as F5. F5 launches pass through untouched: GoDebugLaunchProvider already supplies a
/// complete configuration.
/// </remarks>
[ComVisible(true)]
[Guid(ClsidString)]
public sealed class GoAdapterLauncher : IAdapterLauncher
{
    /// <summary>
    /// The COM class ID of this launcher.
    /// </summary>
    /// <remarks>
    /// Must match both the entry under the Go engine's <c>ExtensibilityObjects</c> key and the
    /// <c>CLSID</c> registration of this class in goproj.pkgdef.
    /// </remarks>
    public const string ClsidString = "B7A3F2D9-5C81-4E6A-9F42-8D0E3C7B6A25";

    public void Initialize(IDebugAdapterHostContext context)
    {
    }

    public void UpdateLaunchOptions(IAdapterLaunchInfo launchInfo)
    {
        if (launchInfo.LaunchType != LaunchType.Attach)
        {
            // F5/Ctrl+F5: GoDebugLaunchProvider owns the configuration.
            return;
        }

        var dlv = GoToolLocator.Find(GoToolExecutables.Delve) ??
            throw new FileNotFoundException(GoStrings.DlvMissing);
        var pid = launchInfo.AttachProcessId;

        // Working directory: the target's own directory when readable, so
        // any relative paths dlv reports line up with the binary.
        var workingDirectory = default(string);
        try
        {
            using var target = System.Diagnostics.Process.GetProcessById(pid);
            workingDirectory = Path.GetDirectoryName(target.MainModule.FileName);
        }
        catch (Exception)
        {
            // Access denied on MainModule (elevated target etc.) - dlv will
            // fail attaching to such a process anyway with its own message.
        }

        // Sync-over-async: UpdateLaunchOptions is synchronous by contract and receives no
        // cancellation token. Worst case is the 10s listen timeout; typical is <300ms.
        var port = DelveServer
            .StartAsync(
                dlvPath: dlv,
                workingDirectory: workingDirectory,
                visibleConsole: false,
                cancellationToken: CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        launchInfo.LaunchJson =
            "{\"$debugServer\":" + port.ToString(CultureInfo.InvariantCulture) + "," +
            "\"type\":\"go\",\"request\":\"attach\",\"mode\":\"local\"," +
            "\"processId\":" + pid.ToString(CultureInfo.InvariantCulture) + "}";
    }

    public ITargetHostProcess LaunchAdapter(
        IAdapterLaunchInfo launchInfo,
        ITargetHostInterop targetInterop)
    {
        // Never reached: every configuration this launcher produces carries
        // "$debugServer", which makes the host connect instead of spawning.
        throw new NotSupportedException(
            "The Go debug adapter is reached via $debugServer, not spawned.");
    }
}
