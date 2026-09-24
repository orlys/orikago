namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

/// <summary>
/// 「加入 → 連線服務」: Azure/WCF/REST service references.
/// </summary>
[ExportCommandGroup("A114CF9C-BD45-4A48-92EF-D9BBBC0B3DF0")]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoHiddenConnectedServiceCommandsHandler : GoHiddenCommandsBase
{
    protected override bool IsHidden(long commandId)
    {
        return (commandId == 17) || (commandId == 19);
    }
}
