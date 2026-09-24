namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;

/// <summary>
/// Swaps the context menu of the Go project's Dependencies ROOT node for a private one.
/// </summary>
/// <remarks>
/// The shell's shared IDM_VS_CTXT_REFERENCEROOT carries "Add Project Reference...", "Manage
/// NuGet Packages..." and other .NET-only placements; the private menu defined in
/// OrikagoPackage.vsct only contains "Add Go Module Reference...". Same extension point the
/// managed project system itself uses (its DependenciesContextMenuProvider does this mapping
/// at a lower Order); child nodes fall through to the default providers untouched.
/// </remarks>
[Export(typeof(IProjectItemContextMenuProvider))]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoDependenciesContextMenuProvider : IProjectItemContextMenuProvider
{
    private static readonly Guid s_commandSet = new(OrikagoCommandTable.CommandSetGuidString);

    public bool TryGetContextMenu(
        IProjectTree projectItem,
        out Guid menuCommandGuid,
        out int menuCommandId)
    {
        if ((projectItem is not null) && projectItem.Flags.Contains("DependenciesRootNode"))
        {
            // Only the dependencies root is remapped. The project ROOT node's
            // menu does NOT come through this extension point (tried: the
            // shared menu kept showing), so NuGet's command is hidden with a
            // command-group handler instead - see GoHiddenNuGetCommandsHandler.
            menuCommandGuid = s_commandSet;
            menuCommandId = OrikagoCommandTable.GoDependenciesContextMenuId;
            return true;
        }

        // Every other node keeps the menu the default providers give it
        menuCommandGuid = default;
        menuCommandId = 0;
        return false;
    }

    public bool TryGetMixedItemsContextMenu(
        IEnumerable<IProjectTree> projectItems,
        out Guid menuCommandGuid,
        out int menuCommandId)
    {
        // Multi-selections keep the default menu
        menuCommandGuid = default;
        menuCommandId = 0;
        return false;
    }
}
