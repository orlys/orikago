namespace Orikago.LanguageService;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell.Interop;

using Orikago.LanguageService.Definitions;

using System;

/// <summary>
/// Hides NuGet's global entry points while a Go project is the active one.
/// </summary>
/// <remarks>
/// <para>
/// The entry points are Tools → NuGet Package Manager → Package Manager Console / Manage
/// NuGet Packages for Solution / Package Manager Settings.
/// </para>
/// <para>
/// Why a priority command target: the CPS command-group handler
/// (<see cref="GoHiddenNuGetCommandsHandler"/>) only participates in
/// command routing for PROJECT context menus. Main-menu commands never
/// reach it, so the Tools entries survived. A priority command target is
/// the one hook that sees every command's QueryStatus, main menu
/// included.
/// </para>
/// <para>
/// Scoped by the uiContextGoProject UI context - the same
/// ActiveProjectCapability rule the package already declares - so the
/// moment a C# (or any non-Go) project is active, NuGet's menu comes back
/// untouched. The GUID check runs first and rejects everything else in a
/// couple of comparisons, so the cost on unrelated commands is
/// negligible.
/// </para>
/// <para>
/// RESULT IS "disabled", NOT "hidden": OLECMDF_INVISIBLE is only honoured
/// for commands whose own .vsct definition carries the DynamicVisibility
/// flag, and NuGet's do not - so the shell keeps drawing them and only
/// the missing OLECMDF_ENABLED takes effect. Greying them out is
/// therefore the ceiling for a command we do not own; removing the items
/// outright would mean disabling the NuGet extension for the whole IDE.
/// </para>
/// </remarks>
internal sealed class GoNuGetCommandFilter : IOleCommandTarget
{
    private static readonly Guid s_dialogCommandSet =
        new(NuGetCommands.DialogCommandSetGuidString);
    private static readonly Guid s_consoleCommandSet =
        new(NuGetCommands.ConsoleCommandSetGuidString);

    private readonly IVsMonitorSelection? _monitorSelection;
    private readonly uint _goContextCookie;

    public GoNuGetCommandFilter(IVsMonitorSelection? monitorSelection)
    {
        _monitorSelection = monitorSelection;
        var contextGuid = new Guid(OrikagoCommandTable.GoProjectUiContextGuidString);
        if (_monitorSelection is not null)
        {
            // Resolve the Go-project UI context once; QueryStatus only checks the cookie
            _monitorSelection.GetCmdUIContextCookie(ref contextGuid, out _goContextCookie);
        }
    }

    public int QueryStatus(
        ref Guid pguidCmdGroup,
        uint cCmds,
        OLECMD[] prgCmds,
        IntPtr pCmdText)
    {
        if (!IsNuGetGlobalCommand(pguidCmdGroup, prgCmds) || !IsGoProjectActive())
        {
            // Not a NuGet entry point, or no Go project is active: the owner decides
            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        for (var i = 0; i < cCmds; i++)
        {
            if (IsHiddenCommand(pguidCmdGroup, prgCmds[i].cmdID))
            {
                prgCmds[i].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_INVISIBLE);
            }
        }

        return VSConstants.S_OK;
    }

    public int Exec(
        ref Guid pguidCmdGroup,
        uint nCmdID,
        uint nCmdexecopt,
        IntPtr pvaIn,
        IntPtr pvaOut)
    {
        // Only visibility is overridden; execution is left to NuGet.
        return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private static bool IsNuGetGlobalCommand(Guid commandGroup, OLECMD[]? commands)
    {
        if (((commandGroup != s_dialogCommandSet) && (commandGroup != s_consoleCommandSet)) ||
            (commands is null))
        {
            // Another command set, or no commands to inspect
            return false;
        }

        foreach (var command in commands)
        {
            if (IsHiddenCommand(commandGroup, command.cmdID))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHiddenCommand(Guid commandGroup, uint commandId)
    {
        if (commandGroup == s_dialogCommandSet)
        {
            return (commandId == NuGetCommands.AddPackageDialog) ||
                (commandId == NuGetCommands.AddPackageDialogForSolution);
        }

        if (commandGroup == s_consoleCommandSet)
        {
            return (commandId == NuGetCommands.PowerConsole) ||
                (commandId == NuGetCommands.SourceSettings);
        }

        return false;
    }

    private bool IsGoProjectActive()
    {
        if ((_monitorSelection is null) || (_goContextCookie == 0))
        {
            // The UI context was never resolved, so no Go project can be active
            return false;
        }

        var result = _monitorSelection.IsCmdUIContextActive(_goContextCookie, out var active);
        return ErrorHandler.Succeeded(result) && (active != 0);
    }
}
