namespace Orikago.LanguageService.Definitions;

/// <summary>
/// Project capability names this extension keys its behaviour off.
/// </summary>
internal static class ProjectCapabilityNames
{
    /// <summary>
    /// The capability every .goproj carries.
    /// </summary>
    /// <remarks>
    /// Declared by Orikago.Sdk as a <c>ProjectCapability</c> item. Every CPS export in this
    /// assembly is scoped to it through <c>[AppliesTo]</c>, and the UI context rule on
    /// <see cref="OrikagoPackage"/> activates on it, so it must stay identical to the value
    /// the SDK declares.
    /// </remarks>
    public const string Orikago = "Orikago";
}
