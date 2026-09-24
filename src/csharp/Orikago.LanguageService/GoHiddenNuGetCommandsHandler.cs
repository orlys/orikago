namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

/// <summary>
/// 「管理 NuGet 套件」: Go dependencies come from go.mod.
/// </summary>
[ExportCommandGroup(NuGetCommands.DialogCommandSetGuidString)]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoHiddenNuGetCommandsHandler : GoHiddenCommandsBase
{
    protected override bool IsHidden(long commandId)
    {
        return (commandId == NuGetCommands.AddPackageDialog) ||
            (commandId == NuGetCommands.AddPackageDialogForSolution);
    }
}
