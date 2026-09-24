namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

/// <summary>
/// 「Code Cleanup」: Roslyn formatting/fixers for C# and VB.
/// </summary>
/// <remarks>
/// Every command in this set belongs to that feature (run default/custom, configure, and
/// their editor and solution variants), so the whole group is hidden - hiding only the known
/// ids left the submenu container behind.
/// </remarks>
[ExportCommandGroup("160961B3-909D-4B28-9353-A1BEF587B4A6")]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoHiddenCodeCleanupCommandsHandler : GoHiddenCommandsBase
{
    protected override bool IsHidden(long commandId)
    {
        return true;
    }
}
