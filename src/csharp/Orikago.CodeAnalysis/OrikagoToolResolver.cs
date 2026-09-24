namespace Orikago.CodeAnalysis;

using System.Globalization;

/// <summary>
/// 負責尋找 <c>orikagoc</c> 邊車可執行檔
/// </summary>
/// <remarks>
/// 搜尋順序：明確指定的路徑 → <c>ORIKAGO_GOC</c> 環境變數 →
/// 與 <c>Orikago.CodeAnalysis</c> 組件同目錄 → 系統 <c>PATH</c>
/// </remarks>
public static class OrikagoToolResolver
{
    private const string ENVIRONMENT_VARIABLE_NAME = "ORIKAGO_GOC";

    private static string ExecutableName
    {
        get
        {
            return OperatingSystem.IsWindows() ? "orikagoc.exe" : "orikagoc";
        }
    }

    /// <summary>
    /// 取得或設定整個處理序層級的預設邊車路徑
    /// </summary>
    /// <remarks>
    /// 設定後等同於每次呼叫 <see cref="Resolve(string?)"/> 都傳入該路徑
    /// </remarks>
    public static string? DefaultToolPath { get; set; }

    /// <summary>
    /// 解析 <c>orikagoc</c> 可執行檔的完整路徑
    /// </summary>
    /// <param name="explicitPath">明確指定的工具路徑；優先於所有其他來源</param>
    /// <returns><c>orikagoc</c> 的完整路徑</returns>
    /// <exception cref="OrikagocNotFoundException">在所有搜尋位置皆找不到工具時擲出</exception>
    public static string Resolve(string? explicitPath = null)
    {
        List<string> attempted = [];

        // 1. 明確指定的路徑（參數 > DefaultToolPath）
        string?[] explicitCandidates = [explicitPath, DefaultToolPath];
        foreach (var candidate in explicitCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                // 未指定此來源，換下一個
                continue;
            }

            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            attempted.Add($"明確指定的路徑：{candidate}");
        }

        // 2. ORIKAGO_GOC 環境變數
        var environmentPath = Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE_NAME);
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return Path.GetFullPath(environmentPath);
        }

        var environmentDescription = string.IsNullOrWhiteSpace(environmentPath)
            ? "（未設定）"
            : environmentPath;
        attempted.Add($"環境變數 {ENVIRONMENT_VARIABLE_NAME}：{environmentDescription}");

        // 3. 與本組件同目錄
        var assemblyLocation = typeof(OrikagoToolResolver).Assembly.Location;
        if (Path.GetDirectoryName(assemblyLocation) is { Length: > 0 } assemblyDirectory)
        {
            var beside = Path.Combine(assemblyDirectory, ExecutableName);
            if (File.Exists(beside))
            {
                return beside;
            }

            attempted.Add($"組件目錄：{beside}");
        }

        // 4. 系統 PATH
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathSeparator = Path.PathSeparator.ToString(CultureInfo.InvariantCulture);
        var entries = pathVariable.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in entries)
        {
            var candidate = default(string);
            try
            {
                candidate = Path.Combine(directory.Trim(), ExecutableName);
            }
            catch (ArgumentException)
            {
                // PATH 中含無效字元的項目，略過
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        attempted.Add($"系統 PATH 中的 {ExecutableName}");

        // 所有來源都找不到：列出嘗試過的位置與修復方式
        var attemptedLines = attempted.Select(attempt => $"  - {attempt}");
        throw new OrikagocNotFoundException(
            "找不到 orikagoc 邊車可執行檔。已嘗試以下位置：" + Environment.NewLine +
            string.Join(Environment.NewLine, attemptedLines) + Environment.NewLine +
            "修復方式（擇一）：" + Environment.NewLine +
            "  1. 在 src/go/orikagoc 目錄執行「go build -o orikagoc.exe .」" +
            "（或使用 Orikago.Sdk 建置該 .goproj），" +
            "並將產出的可執行檔複製到 Orikago.CodeAnalysis.dll 所在目錄；" + Environment.NewLine +
            $"  2. 設定環境變數 {ENVIRONMENT_VARIABLE_NAME} 指向 orikagoc 可執行檔的完整路徑；" +
            Environment.NewLine +
            "  3. 將 orikagoc 所在目錄加入 PATH；" + Environment.NewLine +
            "  4. 於 API 呼叫時明確傳入工具路徑，或設定 OrikagoToolResolver.DefaultToolPath。");
    }
}