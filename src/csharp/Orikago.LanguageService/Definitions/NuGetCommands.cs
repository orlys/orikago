namespace Orikago.LanguageService.Definitions;

/// <summary>
/// NuGet's command identifiers that this extension hides or disables on Go projects.
/// </summary>
/// <remarks>
/// These belong to the NuGet extension (its PkgCmdIDList), not to this one; they were read off
/// the live IDE through DTE.Commands. If NuGet ever renumbers them, the hidden commands simply
/// reappear on Go projects - nothing else breaks.
/// </remarks>
internal static class NuGetCommands
{
    /// <summary>
    /// The command set of the "Manage NuGet Packages" dialogs.
    /// </summary>
    /// <remarks>
    /// Kept in upper case: it is also the literal handed to <c>[ExportCommandGroup]</c>, which
    /// has always been registered in this exact spelling.
    /// </remarks>
    public const string DialogCommandSetGuidString = "25FD982B-8CAE-4CBD-A440-E03FFCCDE106";

    /// <summary>
    /// The command set of the Package Manager Console and the package source settings.
    /// </summary>
    public const string ConsoleCommandSetGuidString = "1E8A55F6-C18D-407F-91C8-94B02AE1CED6";

    /// <summary>
    /// "Manage NuGet Packages..." for a project, in <see cref="DialogCommandSetGuidString"/>.
    /// </summary>
    public const uint AddPackageDialog = 0x100;

    /// <summary>
    /// "Manage NuGet Packages for Solution...", in <see cref="DialogCommandSetGuidString"/>.
    /// </summary>
    public const uint AddPackageDialogForSolution = 0x200;

    /// <summary>
    /// "Package Manager Console", in <see cref="ConsoleCommandSetGuidString"/>.
    /// </summary>
    public const uint PowerConsole = 0x100;

    /// <summary>
    /// "Package Manager Settings", in <see cref="ConsoleCommandSetGuidString"/>.
    /// </summary>
    public const uint SourceSettings = 0x200;
}
