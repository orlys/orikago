namespace Orikago.LanguageService;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Starts and reaps <c>dlv dap</c> TCP servers.
/// </summary>
/// <remarks>
/// Shared by the F5 launch provider and the attach adapter launcher - both hand the port to
/// the Debug Adapter Host via <c>$debugServer</c> (dlv dap speaks TCP only; the host must never
/// spawn it over stdio).
/// </remarks>
internal static class DelveServer
{
    /// <summary>
    /// The dlv dap server of the most recent debug session.
    /// </summary>
    /// <remarks>
    /// dlv exits by itself when its single session ends; this reference exists to reap a
    /// server whose session never started (connection failure, user cancel), which would
    /// otherwise linger until the next launch.
    /// </remarks>
    private static Process? s_server;

    /// <summary>
    /// Starts <c>dlv dap</c> and returns the port the Debug Adapter Host should connect to.
    /// </summary>
    /// <param name="dlvPath">Full path of <c>dlv.exe</c>.</param>
    /// <param name="workingDirectory">
    /// Server working directory; <see langword="null"/> means the user profile.
    /// </param>
    /// <param name="visibleConsole">
    /// <see langword="true"/> for F5 launches: the debuggee inherits dlv's console, which is
    /// where the Go program's stdio lives. <see langword="false"/> for attach - the target
    /// process already owns its own console.
    /// </param>
    /// <param name="cancellationToken">Stops waiting for dlv to start listening.</param>
    /// <returns>The loopback port of the DAP relay in front of dlv.</returns>
    /// <exception cref="InvalidOperationException">
    /// dlv exited during startup, or did not start listening within 10 seconds.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// <c>dlv.exe</c> could not be started.
    /// </exception>
    /// <exception cref="System.Net.Sockets.SocketException">
    /// The relay could not listen on a loopback port.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before dlv started listening; the
    /// half-started dlv is killed.
    /// </exception>
    public static async Task<int> StartAsync(
        string dlvPath,
        string? workingDirectory,
        bool visibleConsole,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref s_server, null) is { } previous)
        {
            // Reap the server of an earlier session that never finished
            using var previousServer = previous;
            try
            {
                if (!previousServer.HasExited)
                {
                    previousServer.Kill();
                }
            }
            catch (Exception)
            {
                // It exited between the check and the kill: nothing is left to reap
            }
        }

        // dlv picks the port (:0) and we read it back from the OS listener
        // table, filtered to dlv's own PID. Preselecting a port here instead
        // (bind :0, read it back, release, pass it to dlv) leaves a window in
        // which another process can take it: the readiness poll below would
        // then see SOMEONE listening, report success, and the proxy would
        // relay the debug session to that other service. Asking who owns the
        // socket removes the window rather than narrowing it.
        //
        // Reading the port from dlv's stdout is not an option: dlv runs
        // WITHOUT redirected stdio (see visibleConsole) so the debuggee can
        // inherit the console.
        var startInfo = new ProcessStartInfo
        {
            FileName = dlvPath,
            // --check-go-version=false: delve only "supports" the last two Go
            // releases and refuses binaries built by an older toolchain
            // outright (a modal error kills the session). A no-op when
            // versions match.
            Arguments = "dap --check-go-version=false --listen=127.0.0.1:0",
            WorkingDirectory = workingDirectory ??
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
            CreateNoWindow = !visibleConsole,
        };

        var process = Process.Start(startInfo);
        try
        {
            // Wait until dlv actually listens before handing the port to the
            // Debug Adapter Host (it connects immediately, no retry). The
            // check must NOT open a connection - dlv dap serves a single
            // client, and a probe connect would consume the session - so the
            // OS listener table is consulted instead.
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                if (process.HasExited)
                {
                    // dlv gave up during startup (bad flags, unsupported binary, ...)
                    throw new InvalidOperationException(
                        GoStrings.DlvExitedEarly(process.ExitCode));
                }

                var port = TcpListenerTable.FindLoopbackListenerPort(process.Id);
                if (port != 0)
                {
                    // dlv is listening. The host talks to the relay, not to dlv
                    // directly - see DelveProxy for the one behaviour it changes.
                    s_server = process;
                    return DelveProxy.Start(port);
                }

                if (elapsed.Elapsed > TimeSpan.FromSeconds(10))
                {
                    // dlv never started listening within the startup budget
                    throw new InvalidOperationException(GoStrings.DlvNotListening);
                }

                // ConfigureAwait(false): the attach path blocks on this task
                // from the UI thread (GetResult) - a captured UI context
                // here would deadlock.
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Startup failed: never leave a half-started dlv behind
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // It exited on its own in the meantime
            }

            process.Dispose();
            throw;
        }
    }
}
