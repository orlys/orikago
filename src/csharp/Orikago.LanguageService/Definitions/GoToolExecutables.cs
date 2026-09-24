namespace Orikago.LanguageService.Definitions;

/// <summary>
/// File names of the Go ecosystem executables this extension launches.
/// </summary>
/// <remarks>
/// <see cref="GoToolLocator"/> probes for them by exactly these names; the go command itself is
/// started by bare name and resolved through PATH by the operating system.
/// </remarks>
internal static class GoToolExecutables
{
    /// <summary>
    /// The go command, started by bare name so PATH resolution picks the active toolchain.
    /// </summary>
    public const string Go = "go";

    /// <summary>
    /// The Go language server, launched once per workspace by the LSP client.
    /// </summary>
    public const string Gopls = "gopls.exe";

    /// <summary>
    /// The delve debugger, started in DAP server mode for F5 launches and attach.
    /// </summary>
    public const string Delve = "dlv.exe";
}
