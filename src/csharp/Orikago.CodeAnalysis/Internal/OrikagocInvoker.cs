namespace Orikago.CodeAnalysis.Internal;

/// <summary>
/// 呼叫 orikagoc 邊車並處理結束代碼慣例的輔助類別（內部使用）
/// </summary>
/// <remarks>
/// 依協定：即使存在診斷，orikagoc 仍以結束代碼 0 結束；非零代碼一律視為基礎結構錯誤
/// </remarks>
internal static class OrikagocInvoker
{
    /// <summary>
    /// 執行 orikagoc 並傳回標準輸出
    /// </summary>
    /// <param name="toolPath">已解析的 orikagoc 完整路徑</param>
    /// <param name="arguments">子命令與引數</param>
    /// <param name="standardInput">
    /// 要寫入標準輸入的內容（<c>parse -</c> 用）；無則為 <see langword="null"/>
    /// </param>
    /// <returns>邊車的標準輸出</returns>
    /// <exception cref="InvalidOperationException">邊車無法啟動，或以非零結束代碼結束</exception>
    /// <exception cref="IOException">寫入標準輸入或讀取輸出失敗</exception>
    public static string Invoke(
        string toolPath,
        IReadOnlyList<string> arguments,
        string? standardInput = null)
    {
        // rule 090 exception: one-shot CLI invocation, not a long-running service
        var result = ProcessRunner.Run(toolPath, arguments, standardInput);

        if (result.ExitCode != 0)
        {
            // 非零結束代碼屬基礎結構錯誤，不是診斷
            var failure = FormattableString.Invariant(
                $"orikagoc {string.Join(' ', arguments)} 以結束代碼 {result.ExitCode} 失敗（基礎結構錯誤）。");
            throw new InvalidOperationException(
                failure + Environment.NewLine + "stderr: " + result.StandardError.Trim());
        }

        return result.StandardOutput;
    }
}