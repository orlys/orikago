namespace Orikago.CodeAnalysis.Internal;

/// <summary>
/// 呼叫 orikagoc sidecar 並處理結束代碼慣例的輔助類別（內部使用）。
/// 依協定：即使存在診斷，orikagoc 仍以結束代碼 0 結束；非零代碼一律視為基礎架構錯誤。
/// </summary>
internal static class GocInvoker
{
    /// <summary>
    /// 執行 orikagoc 並傳回 stdout。結束代碼非零時擲出 <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <param name="toolPath">已解析的 orikagoc 完整路徑。</param>
    /// <param name="arguments">子命令與引數。</param>
    /// <param name="stdin">要寫入標準輸入的內容（parse - 用）；無則為 <see langword="null"/>。</param>
    public static string Invoke(string toolPath, IReadOnlyList<string> arguments, string? stdin = null)
    {
        var result = ProcessRunner.Run(toolPath, arguments, stdin);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"orikagoc {string.Join(' ', arguments)} 以結束代碼 {result.ExitCode} 失敗（基礎架構錯誤）。" +
                Environment.NewLine + "stderr: " + result.StandardError.Trim());
        }

        return result.StandardOutput;
    }
}
