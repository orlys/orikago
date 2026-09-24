namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;

/// <summary>
/// Base for the handlers that hide .NET-only commands on Go projects.
/// </summary>
/// <remarks>
/// <para>
/// Project context-menu commands DO route through CPS command-group
/// handlers, and here <see cref="CommandStatus.Invisible"/> is honoured -
/// the items genuinely disappear (unlike main-menu commands, which can
/// only be greyed out; see <see cref="GoNuGetCommandFilter"/>).
/// </para>
/// <para>
/// Command set GUIDs and ids were read off the live IDE via
/// DTE.Commands (name | Guid | ID), not guessed.
/// </para>
/// </remarks>
internal abstract class GoHiddenCommandsBase : IAsyncCommandGroupHandler
{
    public Task<CommandStatusResult> GetCommandStatusAsync(
        IImmutableSet<IProjectTree> nodes,
        long commandId,
        bool focused,
        string? commandText,
        CommandStatus progressiveStatus)
    {
        if (IsHidden(commandId))
        {
            // One of this handler's commands: report it handled and invisible
            return Task.FromResult(new CommandStatusResult(
                handled: true,
                commandText,
                status: progressiveStatus | CommandStatus.Invisible));
        }

        return CommandStatusResult.Unhandled.AsTask();
    }

    public Task<bool> TryHandleCommandAsync(
        IImmutableSet<IProjectTree> nodes,
        long commandId,
        bool focused,
        long commandExecuteOptions,
        IntPtr variantArgIn,
        IntPtr variantArgOut)
    {
        // Visibility only - nothing is reimplemented.
        return Task.FromResult(false);
    }

    /// <summary>
    /// Decides whether a command of this handler's command group is hidden.
    /// </summary>
    /// <param name="commandId">The command ID within the handler's command group.</param>
    /// <returns>
    /// <see langword="true"/> for commands that should not appear on a Go project.
    /// </returns>
    protected abstract bool IsHidden(long commandId);
}
