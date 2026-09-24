namespace Orikago.LanguageService;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.VS;

using Orikago.LanguageService.Definitions;

/// <summary>
/// 「Publish…」: the wizard produces .NET publish profiles that mean nothing for a Go binary.
/// </summary>
/// <remarks>
/// The profiles are Azure, ClickOnce and folder targets. Cross-compiling still works from the
/// CLI - <c>dotnet publish -r linux-arm64</c> is wired to GOOS/GOARCH by the SDK.
/// </remarks>
[ExportCommandGroup("1496A755-94DE-11D0-8C3F-00C04FC2AAE2")]
[AppliesTo(ProjectCapabilityNames.Orikago)]
[Order(1000)]
internal sealed class GoHiddenPublishCommandsHandler : GoHiddenCommandsBase
{
    protected override bool IsHidden(long commandId)
    {
        return (commandId == 2005) || (commandId == 2006);
    }
}
