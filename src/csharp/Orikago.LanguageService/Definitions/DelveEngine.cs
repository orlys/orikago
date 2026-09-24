namespace Orikago.LanguageService.Definitions;

/// <summary>
/// Identity of the Go (Delve) debug engine registered in goproj.pkgdef.
/// </summary>
internal static class DelveEngine
{
    /// <summary>
    /// The engine GUID, registered under <c>AD7Metrics\Engine</c> in goproj.pkgdef.
    /// </summary>
    /// <remarks>
    /// F5 launches name this engine explicitly and the attach program provider reports it as
    /// the owner of a Go process; both must agree with the pkgdef key or the Debug Adapter
    /// Host never reaches delve.
    /// </remarks>
    public const string GuidString = "2A5D6E81-4C9B-45E2-B8F3-9D0C7A1E6F24";

    /// <summary>
    /// The engine's display name, identical to the <c>Name</c> value in goproj.pkgdef.
    /// </summary>
    public const string Name = "Go Debugger (Delve)";
}
