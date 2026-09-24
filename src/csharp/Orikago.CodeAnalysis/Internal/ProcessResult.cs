namespace Orikago.CodeAnalysis.Internal;

/// <summary>
/// 外部處理序的執行結果（內部使用）
/// </summary>
internal sealed record ProcessResult
{
    /// <summary>取得或設定處理序的結束代碼</summary>
    public required int ExitCode { get; set; } = default!;

    /// <summary>取得或設定擷取到的標準輸出（UTF-8 解碼）</summary>
    public required string StandardOutput { get; set; } = default!;

    /// <summary>取得或設定擷取到的標準錯誤（UTF-8 解碼）</summary>
    public required string StandardError { get; set; } = default!;
}