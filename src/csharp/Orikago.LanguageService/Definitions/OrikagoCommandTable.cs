namespace Orikago.LanguageService.Definitions;

/// <summary>
/// Identifiers of the Orikago command table, shared by the code that registers and routes it.
/// </summary>
/// <remarks>
/// Every value mirrors a symbol in the Symbols section of OrikagoPackage.vsct. The shell
/// resolves commands, menus and visibility constraints by these exact GUID/ID pairs, so a
/// value here and its vsct symbol must always change together.
/// </remarks>
internal static class OrikagoCommandTable
{
    /// <summary>
    /// The command set GUID (<c>guidOrikagoCmdSet</c> in the vsct).
    /// </summary>
    public const string CommandSetGuidString = "1F6D3B85-42A9-4E0C-9B7D-E85C2A94F316";

    /// <summary>
    /// The UI context that is active while a Go project is the active project
    /// (<c>uiContextGoProject</c> in the vsct).
    /// </summary>
    /// <remarks>
    /// Turned on by the <c>ProvideUIContextRule</c> on <see cref="OrikagoPackage"/> when the
    /// active project carries <see cref="ProjectCapabilityNames.Orikago"/>; the vsct visibility
    /// constraints and <see cref="GoNuGetCommandFilter"/> both key off it.
    /// </remarks>
    public const string GoProjectUiContextGuidString = "A7B54C29-8E13-4D6F-92A5-3D1E7F60C8B2";

    /// <summary>
    /// "Add Go Module Reference..." (<c>cmdidAddGoModuleReference</c>).
    /// </summary>
    public const int AddGoModuleReferenceCommandId = 0x0100;

    /// <summary>
    /// "Tidy Go Modules" (<c>cmdidGoModTidy</c>).
    /// </summary>
    public const int GoModTidyCommandId = 0x0101;

    /// <summary>
    /// "Run Go Generators" (<c>cmdidGoGenerate</c>).
    /// </summary>
    public const int GoGenerateCommandId = 0x0102;

    /// <summary>
    /// "Run Go Vet" (<c>cmdidGoVet</c>).
    /// </summary>
    public const int GoVetCommandId = 0x0103;

    /// <summary>
    /// The private context menu of a Go project's Dependencies root
    /// (<c>menuGoDependenciesContext</c>).
    /// </summary>
    public const int GoDependenciesContextMenuId = 0x2000;
}
