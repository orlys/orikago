namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

/// <summary>
/// 「管理使用者祕密」: an ASP.NET Core feature.
/// </summary>
[ExportCommandGroup("9C5B3619-FD0B-467C-B06D-FBEB1496FB1A")]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoHiddenUserSecretsCommandsHandler : GoHiddenCommandsBase
{
    protected override bool IsHidden(long commandId)
    {
        return commandId == 1792;
    }
}
