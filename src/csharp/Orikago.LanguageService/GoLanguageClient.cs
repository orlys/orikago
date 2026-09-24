namespace Orikago.LanguageService;

using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

using Orikago.LanguageService.Definitions;

using StreamJsonRpc;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Task = System.Threading.Tasks.Task;

/// <summary>
/// Language client that launches gopls (the official Go language server) for .go files.
/// </summary>
/// <remarks>
/// gopls runs over stdio and is connected to Visual Studio's LSP infrastructure. It provides
/// completion, hover, signature help, go-to-definition, find references, rename, formatting,
/// and diagnostics.
/// </remarks>
[Export(typeof(ILanguageClient))]
[ContentType(ContentTypeNames.Orikago)]
public sealed class GoLanguageClient :
    ILanguageClient,
    ILanguageClientCustomMessage2,
    IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private Process? _serverProcess;
    private JsonRpc? _rpc;
    private FileSystemWatcher[]? _watchers;
    private string? _workspaceRoot;

    [ImportingConstructor]
    public GoLanguageClient(
        [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Display name shown in Visual Studio (e.g. in the language client status messages).
    /// </summary>
    public string Name
    {
        get
        {
            return "Orikago Language Service";
        }
    }

    /// <summary>
    /// Deliberately <see langword="null"/>: this client opts out of
    /// workspace/didChangeConfiguration.
    /// </summary>
    /// <remarks>
    /// Every setting is pushed once through <see cref="InitializationOptions"/> instead, so
    /// gopls is fully configured by the time <c>initialize</c> returns and never depends on a
    /// post-startup settings round-trip.
    /// </remarks>
    public IEnumerable<string>? ConfigurationSections
    {
        get
        {
            return null;
        }
    }

    /// <summary>
    /// Settings pushed to gopls in the LSP <c>initialize</c> request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// gopls flattens hierarchical setting names before dispatching them
    /// (internal/lsp/source/options.go:
    /// <c>split := strings.Split(name, "."); name = split[len(split)-1]</c>),
    /// so the <c>ui.*</c> / <c>build.*</c> prefixes used in the gopls documentation are
    /// presentation grouping only. The wire format is a flat object, which is what we emit.
    /// </para>
    /// <para>
    /// An unrecognised key is not ignored: it produces
    /// <c>"Invalid settings: unexpected gopls setting ..."</c> as an Error-severity
    /// window/showMessage on every solution open. Only keys that exist in
    /// <c>gopls api-json</c> -> <c>.Options.User[].Name</c> are sent.
    /// </para>
    /// </remarks>
    public object InitializationOptions
    {
        get
        {
            // Keep gopls from walking build output. gopls' own default only excludes
            // node_modules; bin/ and obj/ are added for the SDK's output layout.
            string[] directoryFilters = ["-**/node_modules", "-**/bin", "-**/obj"];

            // Extra flags (e.g. -tags) for the underlying go/packages loads.
            string[] buildFlags = [];

            // Extra environment for the go command, on top of the inherited process
            // environment.
            Dictionary<string, string> environment = [];

            return new Dictionary<string, object>
            {
                // Without this gopls answers textDocument/semanticTokens/full with a
                // "semantictokens are disabled" error, and .go files fall back to
                // plain-text colouring.
                ["semanticTokens"] = true,

                // staticcheck's SA/S/ST checks on top of the default vet-style analyzers.
                ["staticcheck"] = true,

                // Completion of a function inserts its parameters as editable placeholders.
                ["usePlaceholders"] = true,

                // gofumpt is stricter than gofmt and rewrites code on format; leave it opt-in.
                ["gofumpt"] = false,

                // gopls defaults "hints" to an empty map, which disables inlay hints outright.
                // All seven hint kinds implemented by gopls v0.14.x are enabled here.
                ["hints"] = new Dictionary<string, bool>
                {
                    ["assignVariableTypes"] = true,
                    ["compositeLiteralFields"] = true,
                    ["compositeLiteralTypes"] = true,
                    ["constantValues"] = true,
                    ["functionTypeParameters"] = true,
                    ["parameterNames"] = true,
                    ["rangeVariableTypes"] = true,
                },

                // Analyzer toggles beyond the default set. "shadow" is listed explicitly at its
                // default of false because it is noisy enough that its absence should be
                // intentional.
                ["analyses"] = new Dictionary<string, bool>
                {
                    ["unusedparams"] = true,
                    ["shadow"] = false,
                },

                ["directoryFilters"] = directoryFilters,
                ["buildFlags"] = buildFlags,
                ["env"] = environment,
            };
        }
    }

    /// <summary>
    /// Glob patterns Visual Studio watches on gopls' behalf, forwarding matches as
    /// workspace/didChangeWatchedFiles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are edits the editor never sees: the SDK's GoEnsureMod target runs
    /// <c>go mod init</c> / <c>go mod edit -go=&lt;LangVersion&gt;</c> and GoEnsureWorkspace
    /// runs <c>go work use</c> (both guarded so they only fire when the file actually needs
    /// changing, but a LangVersion change or a new project does rewrite go.mod/go.work
    /// mid-build); <c>go build</c> refreshes go.sum; and <c>go get</c> / <c>go mod tidy</c> run
    /// from a terminal change go.mod, go.sum and .go files. Without these patterns gopls keeps
    /// serving a stale module graph until the changed file happens to be opened.
    /// This is the only channel available: VS sends
    /// <c>Capabilities.Workspace.DidChangeWatchedFiles = new DynamicRegistrationSetting(false)</c>
    /// in <c>initialize</c>, so gopls cannot register its own watchers via
    /// client/registerCapability.
    /// </para>
    /// <para>
    /// The trailing entries are exclusions. The documented syntax is "glob patterns
    /// following the standard in .gitignore", and VS implements that literally: the
    /// patterns are handed to <c>IWorkspaceItemFilterService.CreateFileMatcher</c>,
    /// whose rule parser treats a leading '!' as a negation
    /// (<c>SingleRuleMatcher.FromGlob</c>:
    /// <c>if (span[0] == '!') action = FilterResult.NotMatch;</c>)
    /// and whose aggregate matcher reverses the rule list and stops at the first hit,
    /// so the *last* matching pattern wins. Two consequences shape the list below:
    /// includes must come first, and a negation-only list would match nothing at all
    /// (an unmatched path yields FilterResult.Unknown, which the matcher reports as
    /// "no match").
    /// </para>
    /// <para>
    /// The excluded directories mirror the <c>directoryFilters</c> in
    /// <see cref="InitializationOptions"/>; without them VS forwards changes for trees
    /// gopls has been told to ignore, which on a large solution is pure noise (the
    /// SDK writes into bin/ and obj/ on every build). vendor/ is deliberately *not*
    /// excluded: it is part of the build in vendor mode, so gopls does need to hear
    /// about it, and it is absent from directoryFilters for the same reason.
    /// </para>
    /// <para>
    /// Two limits worth knowing. Exclusions filter notifications, they do not narrow
    /// what VS watches - the file-watcher subscription is workspace-wide and the
    /// patterns are evaluated per event - so the saving is in LSP traffic and gopls
    /// work, not in OS-level watching. And the only code that reads this property is
    /// the Open Folder host (<c>OpenFolderServices.OnWorkspaceFileSystemChangedAsync</c>);
    /// in .sln mode nothing consumes it, which is why <see cref="StartFileWatchers"/>
    /// runs the same patterns through client-side FileSystemWatchers and forwards
    /// the events over the rpc itself - solution mode gets watched-file events
    /// from there instead.
    /// </para>
    /// </remarks>
    public IEnumerable<string> FilesToWatch
    {
        get
        {
            return [
                "**/*.go",
                "**/go.mod",
                "**/go.sum",
                "**/go.work",

                // Excludes, last-match-wins: these must stay after the includes above.
                "!**/node_modules/**",
                "!**/bin/**",
                "!**/obj/**"
            ];
        }
    }

    /// <summary>
    /// Show the gold bar notification if the server fails to initialize.
    /// </summary>
    public bool ShowNotificationOnInitializeFailed
    {
        get
        {
            return true;
        }
    }

    /// <summary>
    /// Middle layer is not needed; messages pass through untouched.
    /// </summary>
    public object? MiddleLayer
    {
        get
        {
            return null;
        }
    }

    /// <summary>
    /// No custom server-to-client messages are handled.
    /// </summary>
    public object? CustomMessageTarget
    {
        get
        {
            return null;
        }
    }

    public event AsyncEventHandler<EventArgs>? StartAsync;

    // The event is part of the ILanguageClient contract; VS raises stop internally.
#pragma warning disable CS0067
    public event AsyncEventHandler<EventArgs>? StopAsync;
#pragma warning restore CS0067

    /// <summary>
    /// Launches gopls and returns a <see cref="Connection"/> over its stdin/stdout.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> (no crash) when gopls cannot be located or started.
    /// </remarks>
    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        if (FindGopls() is not { } goplsPath)
        {
            // gopls is not installed anywhere the probe looks; the client stays inactive
            ExtensionLog.Warning(
                "gopls.exe was not found on PATH, in GOBIN, in GOPATH\\bin, or in " +
                "%USERPROFILE%\\go\\bin. Install it with: go install golang.org/x/tools/gopls@latest");
            return null;
        }

        // gopls runs from the solution root when there is one, otherwise from the user profile
        var workspaceRoot = await GetWorkspaceRootAsync(token);
        _workspaceRoot = (workspaceRoot is { Length: > 0 }) && Directory.Exists(workspaceRoot)
            ? workspaceRoot
            : null;
        var workingDirectory = _workspaceRoot ??
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var startInfo = new ProcessStartInfo
        {
            FileName = goplsPath,
            Arguments = BuildGoplsArguments(),
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Visual Studio calls ActivateAsync again when it restarts the client (solution
        // reload, server crash). Without this the previous gopls keeps running with no
        // reader on its stdout and is never reaped - one orphan per restart.
        TerminateServer();

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start())
            {
                // Process.Start reported that no process was started
                ExtensionLog.Error("Failed to start gopls process at '" + goplsPath + "'.");
                process.Dispose();
                return null;
            }
        }
        catch (Exception ex)
        {
            // The gopls executable could not be launched at all
            ExtensionLog.Error("Exception starting gopls at '" + goplsPath + "': " + ex);
            process.Dispose();
            return null;
        }

        // Drain stderr so the gopls process never blocks on a full pipe; forward to debug
        // output.
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                Debug.WriteLine("[gopls] " + line);
            }
        };
        process.BeginErrorReadLine();

        // Drop the reference once gopls is gone so TerminateServer/Dispose never touch
        // a recycled PID.
        process.Exited += (sender, e) =>
        {
            if (ReferenceEquals(_serverProcess, sender))
            {
                _serverProcess = null;
            }
        };

        _serverProcess = process;
        return new Connection(
            reader: process.StandardOutput.BaseStream,
            writer: process.StandardInput.BaseStream);
    }

    /// <summary>
    /// Kills the gopls server, if one is running.
    /// </summary>
    /// <remarks>
    /// MEF disposes exported parts when Visual Studio shuts down; that is the last
    /// chance to reap gopls before it is orphaned.
    /// </remarks>
    public void Dispose()
    {
        TerminateServer();
    }

    /// <summary>
    /// Called once the client has been loaded into VS; signals that the server may be started.
    /// </summary>
    public async Task OnLoadedAsync()
    {
        if (StartAsync is { } startAsync)
        {
            await startAsync.InvokeAsync(this, EventArgs.Empty);
        }
    }

    public Task OnServerInitializedAsync()
    {
        // Not before "initialized": pushing workspace/didChangeWatchedFiles at
        // any earlier point would violate the LSP lifecycle.
        StartFileWatchers();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Captures the JsonRpc connection to gopls.
    /// </summary>
    /// <remarks>
    /// This is the channel the file watchers below use to hand-deliver
    /// workspace/didChangeWatchedFiles.
    /// </remarks>
    public Task AttachForCustomMessageAsync(JsonRpc rpc)
    {
        _rpc = rpc;
        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(
        ILanguageClientInitializationInfo initializationState)
    {
        // Prefer the status message; fall back to the initialization exception, if any
        var details = (initializationState?.StatusMessage is { Length: > 0 } statusMessage)
            ? statusMessage
            : (initializationState?.InitializationException?.Message ?? "unknown error");

        ExtensionLog.Error("gopls language server failed to initialize: " + details);

        var failureContext = new InitializationFailureContext
        {
            FailureMessage = "Orikago Language Service could not start gopls (" + details + "). " +
                "Verify that gopls is installed and on PATH (go install golang.org/x/tools/gopls@latest).",
        };
        return Task.FromResult<InitializationFailureContext?>(failureContext);
    }

    /// <summary>
    /// Kills the running gopls, if any.
    /// </summary>
    /// <remarks>
    /// gopls normally exits when Visual Studio closes its stdin, but that only happens on a
    /// clean LSP shutdown; a crashed or hung server, or a client restart, leaves it alive with
    /// nobody draining its pipes.
    /// </remarks>
    private void TerminateServer()
    {
        // The watchers and the rpc belong to the server instance being torn down.
        DisposeWatchers();
        _rpc = null;

        if (Interlocked.Exchange(ref _serverProcess, null) is not { } process)
        {
            // No server is running, so there is nothing to reap
            return;
        }

        // Kill the server unless it is already gone; the process handle is released either way
        using var ownedProcess = process;
        try
        {
            if (!ownedProcess.HasExited)
            {
                ownedProcess.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between the check and the kill: nothing is left to reap
        }
        catch (Win32Exception ex)
        {
            // Kill is refused both for a process that is already exiting and for one Windows
            // will not let us terminate
            if (!ownedProcess.HasExited)
            {
                // Windows refused to terminate it, so gopls is left running without a client
                ExtensionLog.Error(
                    "gopls could not be terminated and may be left running: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            // Any other failure also leaves gopls in an unknown state
            ExtensionLog.Error("Terminating gopls failed: " + ex);
        }
    }

    /// <summary>
    /// Client-side replacement for the watched-files channel that solution mode lacks.
    /// </summary>
    /// <remarks>
    /// VS only consumes <see cref="FilesToWatch"/> in Open Folder mode, and it hardcodes
    /// DidChangeWatchedFiles <c>dynamicRegistration=false</c> so gopls cannot register its own
    /// watcher - so in .sln/.slnx mode (the product's primary mode) nothing would ever tell
    /// gopls that <c>go get</c> in a terminal, or the SDK's GoEnsureMod target, rewrote
    /// go.mod/go.sum/go.work on disk, and it would keep serving a stale module graph until VS
    /// restarts. These watchers close that gap by forwarding the same patterns as
    /// <see cref="FilesToWatch"/> straight over the rpc.
    /// ponytail: no debounce - gopls dedups changes per snapshot; add coalescing only if large
    /// workspaces show notification pressure.
    /// </remarks>
    private void StartFileWatchers()
    {
        DisposeWatchers();

        if ((_workspaceRoot is not { } root) || (_rpc is null))
        {
            // No folder to watch (a loose file was opened) or no rpc to forward over
            return;
        }

        // One recursive watcher per include pattern of FilesToWatch; a pattern whose watcher
        // cannot be created is skipped so the others still run
        string[] filters = ["*.go", "go.mod", "go.sum", "go.work"];
        List<FileSystemWatcher> watchers = [];
        foreach (var filter in filters)
        {
            var watcher = default(FileSystemWatcher);
            try
            {
                watcher = new FileSystemWatcher(root, filter)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size,
                };
                watcher.Created += (sender, e) => NotifyFileChange(e.FullPath, 1);
                watcher.Changed += (sender, e) => NotifyFileChange(e.FullPath, 2);
                watcher.Deleted += (sender, e) => NotifyFileChange(e.FullPath, 3);
                watcher.Renamed += (sender, e) =>
                {
                    NotifyFileChange(e.OldFullPath, 3);
                    NotifyFileChange(e.FullPath, 1);
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                // gopls will not hear about changes matching this pattern; a watcher that was
                // created but never handed to the list is released here
                watcher?.Dispose();
                ExtensionLog.Warning("file watcher for '" + filter + "' failed: " + ex.Message);
            }
        }

        _watchers = [.. watchers];
    }

    /// <summary>
    /// Forwards one file event to gopls as workspace/didChangeWatchedFiles.
    /// </summary>
    /// <remarks>
    /// <paramref name="changeType"/> is the LSP FileChangeType (<c>1</c> = Created, <c>2</c> =
    /// Changed, <c>3</c> = Deleted). Excluded trees mirror the negations in
    /// <see cref="FilesToWatch"/>: build output and node_modules are noise gopls was told to
    /// ignore via directoryFilters anyway.
    /// </remarks>
    private void NotifyFileChange(string fullPath, int changeType)
    {
        if ((_rpc is not { } rpc) || IsExcludedPath(fullPath))
        {
            // No live server to tell, or a path in a tree gopls ignores
            return;
        }

        try
        {
            // The notification params are spelled out key by key, so the wire names never
            // depend on C# member names
            var change = new Dictionary<string, object>
            {
                ["uri"] = new Uri(fullPath).AbsoluteUri,
                ["type"] = changeType,
            };
            Dictionary<string, object>[] changes = [change];
            var notification = rpc.NotifyWithParameterObjectAsync(
                targetName: "workspace/didChangeWatchedFiles",
                argument: new Dictionary<string, object> { ["changes"] = changes });

            // The send completes asynchronously: observe it so a late failure is reported
            // instead of vanishing with a discarded task
            _ = notification.ContinueWith(
                continuationAction: task =>
                    ReportForwardFailure(task.Exception?.InnerException),
                cancellationToken: CancellationToken.None,
                continuationOptions: TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously,
                scheduler: TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            // The notification could not be sent at all
            ReportForwardFailure(ex);
        }
    }

    /// <summary>
    /// Records a failed didChangeWatchedFiles forward, unless the connection is simply gone.
    /// </summary>
    /// <remarks>
    /// A lost or disposed rpc means a server restart is in flight; the new server rescans the
    /// workspace, so the dropped change is moot and not worth an activity-log entry.
    /// </remarks>
    private static void ReportForwardFailure(Exception? exception)
    {
        if (exception is not (null or ConnectionLostException or ObjectDisposedException))
        {
            // gopls may keep serving a module graph that no longer matches the files on disk
            ExtensionLog.Error("didChangeWatchedFiles forward failed: " + exception);
        }
    }

    private static bool IsExcludedPath(string fullPath)
    {
        // Split on both Windows separators: a path may use either
        foreach (var segment in fullPath.Split(["\\", "/"], StringSplitOptions.None))
        {
            if (string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "node_modules", StringComparison.OrdinalIgnoreCase))
            {
                // The path runs through a build-output or node_modules tree
                return true;
            }
        }

        return false;
    }

    private void DisposeWatchers()
    {
        if (Interlocked.Exchange(ref _watchers, null) is not { } watchers)
        {
            // No watchers are running
            return;
        }

        foreach (var watcher in watchers)
        {
            try
            {
                watcher.Dispose();
            }
            catch (Exception)
            {
                // Disposing a watcher whose directory vanished can throw; ignore.
            }
        }
    }

    /// <summary>
    /// Command line for the gopls server process.
    /// </summary>
    /// <remarks>
    /// RPC tracing is verbose and costs throughput, so it is opt-in: set the environment
    /// variable ORIKAGO_GOPLS_RPCTRACE to <c>1</c>/<c>true</c>/<c>yes</c> before launching
    /// Visual Studio to have gopls log every LSP message to stderr (surfaced in the debug
    /// output).
    /// </remarks>
    private static string BuildGoplsArguments()
    {
        return IsEnvironmentFlagEnabled("ORIKAGO_GOPLS_RPCTRACE")
            ? "serve -rpc.trace"
            : "serve";
    }

    /// <summary>
    /// Treats an environment variable as a boolean switch.
    /// </summary>
    /// <remarks>
    /// Only explicit affirmative values enable it; anything unset, empty or unrecognised is
    /// treated as off.
    /// </remarks>
    private static bool IsEnvironmentFlagEnabled(string variableName)
    {
        var raw = default(string);
        try
        {
            raw = Environment.GetEnvironmentVariable(variableName);
        }
        catch (SecurityException ex)
        {
            // The security policy forbids reading the environment: the switch is off
            ExtensionLog.Warning("Could not read " + variableName + ": " + ex.Message);
            return false;
        }

        if (raw is not { Length: > 0 })
        {
            // Unset or empty: the switch is off
            return false;
        }

        var value = raw.Trim();
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Locates <c>gopls.exe</c> via the shared Go tool probe (see <see cref="GoToolLocator"/>).
    /// </summary>
    private static string? FindGopls()
    {
        return GoToolLocator.Find(GoToolExecutables.Gopls);
    }

    /// <summary>
    /// Returns the root directory of the currently opened solution or folder, when available.
    /// </summary>
    /// <remarks>
    /// Works for both solution mode and Open Folder mode (IVsSolution reports the folder root).
    /// </remarks>
    private async Task<string?> GetWorkspaceRootAsync(CancellationToken token)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);

            if ((_serviceProvider?.GetService(typeof(SVsSolution)) is IVsSolution solution) &&
                (solution.GetSolutionInfo(out var solutionDirectory, out _, out _) == 0) &&
                (solutionDirectory is { Length: > 0 }))
            {
                // A solution (or an Open Folder root) is loaded and reports its directory
                return solutionDirectory;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The solution service could not be queried; gopls falls back to the user profile
            ExtensionLog.Error("Could not determine workspace root: " + ex);
        }
        finally
        {
            await TaskScheduler.Default;
        }

        return null;
    }
}
