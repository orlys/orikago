namespace Orikago.LanguageService;

using Microsoft.VisualStudio.Shell;

using System.Diagnostics;

/// <summary>
/// Records this extension's failures in the Visual Studio activity log.
/// </summary>
/// <remarks>
/// Every entry is also written to the debugger output with an <c>[Orikago]</c> prefix. The
/// activity log has no structured-logging surface, so messages are plain English sentences.
/// The level follows who can fix the failure: <see cref="Warning"/> for conditions the user or
/// their environment can correct (a missing tool, an unreadable setting), <see cref="Error"/>
/// for faults in the extension or in the IDE services it depends on.
/// </remarks>
internal static class ExtensionLog
{
    private const string SOURCE = "Orikago.LanguageService";

    /// <summary>
    /// Records a failure the user or their environment can correct.
    /// </summary>
    /// <param name="message">What failed, including the identifiers needed to trace it.</param>
    public static void Warning(string message)
    {
        Debug.WriteLine("[Orikago] " + message);
        ActivityLog.TryLogWarning(SOURCE, message);
    }

    /// <summary>
    /// Records a fault in the extension or in the IDE services it depends on.
    /// </summary>
    /// <param name="message">What failed, including the identifiers needed to trace it.</param>
    public static void Error(string message)
    {
        Debug.WriteLine("[Orikago] " + message);
        ActivityLog.TryLogError(SOURCE, message);
    }
}
