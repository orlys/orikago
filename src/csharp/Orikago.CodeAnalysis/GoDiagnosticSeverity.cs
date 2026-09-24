namespace Orikago.CodeAnalysis;

/// <summary>
/// 診斷嚴重性等級
/// </summary>
public enum GoDiagnosticSeverity
{
    /// <summary>資訊訊息，不影響編譯結果</summary>
    Info,

    /// <summary>警告，編譯仍可完成</summary>
    Warning,

    /// <summary>錯誤，編譯失敗或語意不正確</summary>
    Error,
}