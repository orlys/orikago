namespace Orikago.LanguageService.Definitions;

/// <summary>
/// Editor content type names this extension exports and keys its editor features off.
/// </summary>
internal static class ContentTypeNames
{
    /// <summary>
    /// The content type of Go source files.
    /// </summary>
    /// <remarks>
    /// Exported by <see cref="GoContentTypeDefinitions"/>, which maps the <c>.go</c> extension
    /// onto it; <see cref="GoLanguageClient"/> activates gopls for buffers of this type. The
    /// name is deliberately not <c>go</c> - see <see cref="GoContentTypeDefinitions"/>.
    /// </remarks>
    public const string Orikago = "Orikago";
}
