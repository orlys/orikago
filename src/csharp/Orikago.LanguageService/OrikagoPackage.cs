namespace Orikago.LanguageService;

using EnvDTE;

using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

using Orikago.LanguageService.Definitions;

using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using DiagnosticsProcess = System.Diagnostics.Process;
using Project = EnvDTE.Project;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Package hosting the "加入 Go 模組參考..." project-context-menu command.
/// </summary>
/// <remarks>
/// It writes a &lt;GoModuleReference&gt; item into the selected .goproj; the SDK's
/// GoRestoreModules target materializes it with <c>go get</c> on the next build. The project
/// declares HandlesOwnReload, so CPS picks up the edit without prompting.
/// </remarks>
// RegisterUsing=CodeBase is load-bearing: the default emits only an
// assembly display name ("Assembly"="Orikago.LanguageService, ...,
// PublicKeyToken=null") into the pkgdef, which the shell cannot resolve
// for a non-GAC extension assembly - the package never loads and the
// CTMENU merge silently reads nothing. CodeBase pins
// "$PackageFolder$\Orikago.LanguageService.dll".
[PackageRegistration(
    UseManagedResourcesOnly = true,
    AllowsBackgroundLoading = true,
    RegisterUsing = RegistrationMethod.CodeBase)]
[Guid(PackageGuidString)]
// The shell caches the ctmenu merge PER VERSION and never re-reads a version
// it has already processed: version 1 was once installed while the resource
// was missing from the assembly (no VSPackage.resx/MergeWithCTO yet), and the
// fixed resource stayed invisible until the version changed. Bump this
// whenever the vsct changes.
[ProvideMenuResource("Menus.ctmenu", 8)]
// Lights up the vsct's uiContextGoProject when the ACTIVE project carries
// the Orikago capability (declared by Orikago.Sdk), so the command only
// appears on .goproj project nodes - evaluated by the shell without
// loading this package.
[ProvideUIContextRule(
    contextGuid: OrikagoCommandTable.GoProjectUiContextGuidString,
    name: "OrikagoProjectActive",
    expression: "Orikago",
    termNames: ["Orikago"],
    termValues: ["ActiveProjectCapability:" + ProjectCapabilityNames.Orikago])]
// Autoload on that same context so the priority command target is in
// place as soon as a Go project is active - the Tools menu is built
// without ever touching a project node, so waiting for the command to be
// invoked would be too late.
[ProvideAutoLoad(
    cmdUiContextGuid: OrikagoCommandTable.GoProjectUiContextGuidString,
    flags: PackageAutoLoadFlags.BackgroundLoad)]
public sealed class OrikagoPackage : AsyncPackage
{
    /// <summary>
    /// The package GUID (<c>guidOrikagoPackage</c> in OrikagoPackage.vsct).
    /// </summary>
    public const string PackageGuidString = "9C4E9A2B-7D31-4F5C-A1E8-52B60D3F8E74";

    private static readonly Guid s_commandSet = new(OrikagoCommandTable.CommandSetGuidString);

    private Guid _outputPaneGuid = new("6B7A1E4C-9D53-4A08-B2F7-1C5E8D3A9042");
    private uint _priorityCommandTargetCookie;

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // The two registrations are independent: one failing must not skip the other
        await RegisterCommandsAsync(cancellationToken);
        await RegisterNuGetCommandFilterAsync(cancellationToken);
    }

    /// <summary>
    /// Binds every command of the vsct to its handler.
    /// </summary>
    private async Task RegisterCommandsAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (await GetServiceAsync(typeof(IMenuCommandService))
                is not OleMenuCommandService commandService)
        {
            // Without the menu command service no Orikago command can ever run
            ExtensionLog.Error(
                "The menu command service is unavailable; Orikago commands are not bound.");
            return;
        }

        AddCommand(
            commandService,
            handler: ExecuteAddGoModuleReference,
            commandId: OrikagoCommandTable.AddGoModuleReferenceCommandId);
        AddCommand(
            commandService,
            handler: ExecuteGoModTidy,
            commandId: OrikagoCommandTable.GoModTidyCommandId);
        AddCommand(
            commandService,
            handler: ExecuteGoGenerate,
            commandId: OrikagoCommandTable.GoGenerateCommandId);
        AddCommand(
            commandService,
            handler: ExecuteGoVet,
            commandId: OrikagoCommandTable.GoVetCommandId);
    }

    /// <summary>
    /// Registers <see cref="GoNuGetCommandFilter"/>, which disables NuGet's Tools-menu entries
    /// while a Go project is active.
    /// </summary>
    /// <remarks>
    /// See <see cref="GoNuGetCommandFilter"/> for why this cannot be done with the
    /// project-scoped command handler.
    /// </remarks>
    private async Task RegisterNuGetCommandFilterAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if ((await GetServiceAsync(typeof(SVsRegisterPriorityCommandTarget))
                is not IVsRegisterPriorityCommandTarget priorityTarget) ||
            (await GetServiceAsync(typeof(SVsShellMonitorSelection))
                is not IVsMonitorSelection monitorSelection))
        {
            // Without these shell services NuGet's Tools-menu entries stay enabled for Go
            ExtensionLog.Error(
                "The priority command target or selection monitor service is unavailable; " +
                "NuGet's Tools-menu entries are not filtered.");
            return;
        }

        var filter = new GoNuGetCommandFilter(monitorSelection);
        priorityTarget.RegisterPriorityCommandTarget(
            dwReserved: 0,
            pCmdTrgt: filter,
            pdwCookie: out _priorityCommandTargetCookie);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (_priorityCommandTargetCookie != 0))
        {
            // Withdraw the NuGet menu filter registered during initialization
            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                if (await GetServiceAsync(typeof(SVsRegisterPriorityCommandTarget))
                        is IVsRegisterPriorityCommandTarget target)
                {
                    target.UnregisterPriorityCommandTarget(_priorityCommandTargetCookie);
                }

                _priorityCommandTargetCookie = 0;
            });
        }

        base.Dispose(disposing);
    }

    private static void AddCommand(
        OleMenuCommandService commandService,
        EventHandler handler,
        int commandId)
    {
        var id = new CommandID(s_commandSet, commandId);
        commandService.AddCommand(new OleMenuCommand(handler, id));
    }

    private void ExecuteAddGoModuleReference(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            if (GetService(typeof(SDTE)) is not DTE dte)
            {
                // Without the DTE service there is no selected project to act on
                ExtensionLog.Error("Add Go Module Reference: the DTE service is unavailable.");
                return;
            }

            if (GetActiveGoProjectPath(dte) is not { } projectPath)
            {
                // The UI context should have hidden the command; fail quiet toward the user
                ExtensionLog.Error("Add Go Module Reference: no Go project is selected.");
                return;
            }

            // Ask the user which module (and optionally which version) to reference
            var dialog = new AddModuleReferenceDialog();
            if (dialog.ShowModal() is not true)
            {
                // The user cancelled the dialog
                return;
            }

            // Write the reference into the project file; the next build restores it
            AddOrUpdateReference(projectPath, dialog.ModulePath, dialog.ModuleVersion);

            // Confirm in the status bar what was written
            dte.StatusBar.Text = (dialog.ModuleVersion is { Length: > 0 })
                ? GoStrings.ReferenceAddedPinned(dialog.ModulePath, dialog.ModuleVersion)
                : GoStrings.ReferenceAddedLatest(dialog.ModulePath);
        }
        catch (Exception ex)
        {
            // The project file could not be read or written
            ExtensionLog.Error("Add Go Module Reference failed: " + ex);
            ShowError(GoStrings.AddReferenceFailed(ex.Message));
        }
    }

    /// <summary>
    /// Runs <c>go mod tidy</c> in the selected project's directory.
    /// </summary>
    /// <remarks>
    /// This is the dependency-list counterpart of Code Cleanup (hidden for Go, since it only
    /// runs Roslyn C#/VB fixers); source-level tidying - gofmt and import organizing - is
    /// already handled by gopls.
    /// </remarks>
    private void ExecuteGoModTidy(object sender, EventArgs e)
    {
        RunGoCommand(
            arguments: "mod tidy",
            runningText: GoStrings.TidyRunning,
            successText: GoStrings.TidySucceeded,
            failureText: GoStrings.TidyFailed);
    }

    /// <summary>
    /// go generate: runs the //go:generate directives.
    /// </summary>
    /// <remarks>
    /// Nothing else invokes it - by Go's design the build never does - so this is its only
    /// entry point in the IDE.
    /// </remarks>
    private void ExecuteGoGenerate(object sender, EventArgs e)
    {
        RunGoCommand(
            arguments: "generate ./...",
            runningText: GoStrings.GenerateRunning,
            successText: GoStrings.GenerateSucceeded,
            failureText: GoStrings.GenerateFailed);
    }

    /// <summary>
    /// Runs <c>go vet ./...</c> in the selected project's directory.
    /// </summary>
    /// <remarks>
    /// Also available during build via <c>-p:RunGoVet=true</c>, but a one-shot run without
    /// rebuilding is what is usually wanted.
    /// </remarks>
    private void ExecuteGoVet(object sender, EventArgs e)
    {
        RunGoCommand(
            arguments: "vet ./...",
            runningText: GoStrings.VetRunning,
            successText: GoStrings.VetSucceeded,
            failureText: GoStrings.VetFailed);
    }

    /// <summary>
    /// Runs a go subcommand in the selected project's directory, mirroring its output into
    /// the "Orikago" output pane.
    /// </summary>
    /// <remarks>
    /// Failures surface both in the pane and as a message box, because a silent non-zero exit
    /// is the worst possible outcome for a one-click tool.
    /// </remarks>
    private void RunGoCommand(
        string arguments,
        string runningText,
        string successText,
        Func<string, string> failureText)
    {
        _ = JoinableTaskFactory.RunAsync(delegate
        {
            return RunGoCommandAsync(
                arguments: arguments,
                runningText: runningText,
                successText: successText,
                failureText: failureText,
                cancellationToken: DisposalToken);
        });
    }

    /// <summary>
    /// Runs the go command on a background thread and reports the outcome on the UI thread.
    /// </summary>
    /// <remarks>
    /// <c>go mod tidy</c> on a project with uncached modules takes tens of seconds, and doing
    /// that inline on the UI thread freezes the IDE for the duration. Only the DTE and
    /// output-pane calls need the main thread.
    /// </remarks>
    private async Task RunGoCommandAsync(
        string arguments,
        string runningText,
        string successText,
        Func<string, string> failureText,
        CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (await GetServiceAsync(typeof(SDTE)) is not DTE dte)
        {
            // Without the DTE service there is no selected project to act on
            ExtensionLog.Error("go " + arguments + ": the DTE service is unavailable.");
            return;
        }

        if (GetActiveGoProjectPath(dte) is not { } projectPath)
        {
            // The UI context should have hidden the command; fail quiet toward the user
            ExtensionLog.Error("go " + arguments + ": no Go project is selected.");
            return;
        }

        // Announce the run in the status bar and the Orikago output pane
        dte.StatusBar.Text = runningText;
        WriteToOutputPane("> go " + arguments + Environment.NewLine, activate: true);
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = GoToolExecutables.Go,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(projectPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            await TaskScheduler.Default;
            using var process = DiagnosticsProcess.Start(startInfo);

            // Both pipes must be drained CONCURRENTLY. The go tools put
            // their progress on stderr ("go: downloading ..." from go mod
            // tidy is the common case), and reading stdout to the end
            // first means nobody drains stderr until the process exits -
            // so once stderr's pipe buffer fills, go blocks writing it
            // and this thread blocks reading stdout: a deadlock neither
            // side can break.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            process.WaitForExit();
            var exitCode = process.ExitCode;

            // Mirror what the tool printed into the output pane
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (output is { Length: > 0 })
            {
                WriteToOutputPane(output);
            }

            if (error is { Length: > 0 })
            {
                WriteToOutputPane(error);
            }

            if (exitCode != 0)
            {
                // go reported a failure (or vet findings); its own output is the message
                dte.StatusBar.Text = string.Empty;
                ExtensionLog.Warning(
                    "go " + arguments + " exited with code " +
                    exitCode.ToString(CultureInfo.InvariantCulture) + ".");
                var message = string.IsNullOrWhiteSpace(error) ? output : error;
                ShowError(failureText(message.Trim()));
                return;
            }

            dte.StatusBar.Text = successText;
            WriteToOutputPane(successText + Environment.NewLine);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // The go executable could not be started: the toolchain is not on PATH
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            dte.StatusBar.Text = string.Empty;
            ExtensionLog.Warning("go " + arguments + " could not be started: " + ex.Message);
            ShowError(GoStrings.GoCommandMissing);
        }
        catch (OperationCanceledException)
        {
            // The package is being disposed (VS is shutting down): nobody is left to tell
            throw;
        }
        catch (Exception ex)
        {
            // Running the go command failed for any other reason
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            dte.StatusBar.Text = string.Empty;
            ExtensionLog.Error("go " + arguments + " failed: " + ex);
            ShowError(failureText(ex.Message));
        }
    }

    private void WriteToOutputPane(string text, bool activate = false)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (GetService(typeof(SVsOutputWindow)) is not IVsOutputWindow outputWindow)
        {
            // Without the output window service the text has nowhere to go
            ExtensionLog.Error("The output window service is unavailable.");
            return;
        }

        if ((outputWindow.GetPane(ref _outputPaneGuid, out var pane) != 0) || (pane is null))
        {
            // The Orikago pane does not exist yet: create it, then fetch it again
            outputWindow.CreatePane(ref _outputPaneGuid, "Orikago", 1, 0);
            outputWindow.GetPane(ref _outputPaneGuid, out pane);
        }

        if (pane is null)
        {
            // The pane could not be created, so the text has nowhere to go
            ExtensionLog.Error("The Orikago output pane could not be created.");
            return;
        }

        pane.OutputStringThreadSafe(text);
        if (activate)
        {
            pane.Activate();
        }
    }

    private void ShowError(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(
            serviceProvider: this,
            message: message,
            title: GoStrings.MessageBoxTitle,
            icon: OLEMSGICON.OLEMSGICON_CRITICAL,
            msgButton: OLEMSGBUTTON.OLEMSGBUTTON_OK,
            defaultButton: OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    /// <summary>
    /// Full path of the selected project when it is a .goproj; otherwise
    /// <see langword="null"/>.
    /// </summary>
    private static string? GetActiveGoProjectPath(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if ((dte.ActiveSolutionProjects is not Array projects) ||
            (projects.Cast<object>().FirstOrDefault() is not Project { FullName: { } fullName }))
        {
            // Nothing is selected, or the selection is not a project
            return null;
        }

        var isGoProject = fullName.EndsWith(".goproj", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(fullName);
        return isGoProject ? fullName : null;
    }

    /// <summary>
    /// Inserts or updates &lt;GoModuleReference Include="module" Version="v..." /&gt;
    /// in the project file.
    /// </summary>
    /// <remarks>
    /// The MSBuild construction model is used (isolated ProjectCollection, so VS's own loaded
    /// copy is untouched) because it preserves the file's formatting and reuses an existing
    /// ItemGroup. An empty version means "latest": the Version attribute is omitted/removed.
    /// </remarks>
    private static void AddOrUpdateReference(
        string projectPath,
        string modulePath,
        string version)
    {
        using var collection = new ProjectCollection();
        var root = ProjectRootElement.Open(projectPath, collection);

        ApplyReference(root, modulePath, version);
        root.Save();
    }

    /// <summary>
    /// Adds, re-pins or un-pins the &lt;GoModuleReference&gt; item of one module.
    /// </summary>
    private static void ApplyReference(
        ProjectRootElement root,
        string modulePath,
        string version)
    {
        const string ITEM_TYPE = "GoModuleReference";
        const string VERSION_METADATA = "Version";

        if (root.Items
                .Where(i => string.Equals(i.ItemType, ITEM_TYPE, StringComparison.Ordinal))
                .Where(i => string.Equals(i.Include, modulePath, StringComparison.Ordinal))
                .FirstOrDefault() is not { } existing)
        {
            // The module is not referenced yet: add it beside the other module references
            var firstReference = root.Items
                .Where(i => string.Equals(i.ItemType, ITEM_TYPE, StringComparison.Ordinal))
                .FirstOrDefault();
            var group = (firstReference?.Parent is ProjectItemGroupElement existingGroup)
                ? existingGroup
                : root.AddItemGroup();
            var item = group.AddItem(ITEM_TYPE, modulePath);
            if (version is { Length: > 0 })
            {
                // A pinned version is written as a Version attribute
                item.AddMetadata(VERSION_METADATA, version, expressAsAttribute: true);
            }

            return;
        }

        var versionMetadata = existing.Metadata
            .Where(m => string.Equals(m.Name, VERSION_METADATA, StringComparison.Ordinal))
            .FirstOrDefault();

        if (version is not { Length: > 0 })
        {
            // "Latest" is expressed by the absence of a Version attribute
            if (versionMetadata is not null)
            {
                existing.RemoveChild(versionMetadata);
            }

            return;
        }

        if (versionMetadata is not null)
        {
            // Re-pin the existing reference to the requested version
            versionMetadata.Value = version;
            return;
        }

        // Pin a reference that previously tracked the latest version
        existing.AddMetadata(VERSION_METADATA, version, expressAsAttribute: true);
    }
}
