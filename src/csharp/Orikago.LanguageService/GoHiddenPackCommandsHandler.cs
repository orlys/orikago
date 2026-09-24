namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

/// <summary>
/// 「Pack」: NuGet packaging has no Go equivalent.
/// </summary>
[ExportCommandGroup("568ABDF7-D522-474D-9EED-34B5E5095BA5")]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoHiddenPackCommandsHandler : GoHiddenCommandsBase
{
    protected override bool IsHidden(long commandId)
    {
        return (commandId == 8192) || (commandId == 8193);
    }
}
