namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

/// <summary>
/// 「Modernize」: the .NET upgrade assistant.
/// </summary>
[ExportCommandGroup("31760A92-B75C-472D-B977-7CAEAB0AF122")]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoHiddenModernizeCommandsHandler : GoHiddenCommandsBase
{
    protected override bool IsHidden(long commandId)
    {
        return (commandId == 1280) || (commandId == 1296);
    }
}
