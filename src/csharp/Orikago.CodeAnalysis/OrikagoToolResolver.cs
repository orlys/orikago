using System.Runtime.InteropServices;

namespace Orikago.CodeAnalysis;

/// <summary>
/// 負責尋找 <c>orikagoc</c> sidecar 可執行檔。
/// 搜尋順序：明確指定的路徑 → <c>ORIKAGO_GOC</c> 環境變數 →
/// 與 <c>Orikago.CodeAnalysis</c> 組件同目錄 → 系統 <c>PATH</c>。
/// </summary>
public static class OrikagoToolResolver
{
    private const string EnvVarName = "ORIKAGO_GOC";

    private static string ExeName
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "orikagoc.exe" : "orikagoc";

    /// <summary>
    /// 取得或設定整個處理序層級的預設 sidecar 路徑。
    /// 設定後等同於每次呼叫 <see cref="Resolve(string?)"/> 都傳入該路徑。
    /// </summary>
    public static string? DefaultToolPath { get; set; }

    /// <summary>
    /// 解析 <c>orikagoc</c> 可執行檔的完整路徑。
    /// </summary>
    /// <param name="explicitPath">明確指定的工具路徑；優先於所有其他來源。</param>
    /// <returns><c>orikagoc</c> 的完整路徑。</returns>
    /// <exception cref="OrikagocNotFoundException">在所有搜尋位置皆找不到工具時擲出。</exception>
    public static string Resolve(string? explicitPath = null)
    {
        var attempted = new List<string>();

        // 1. 明確指定的路徑（參數 > DefaultToolPath）。
        foreach (var candidate in new[] { explicitPath, DefaultToolPath })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                attempted.Add($"明確指定的路徑: {candidate}");
            }
        }

        // 2. ORIKAGO_GOC 環境變數。
        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            if (File.Exists(envPath))
            {
                return Path.GetFullPath(envPath);
            }

            attempted.Add($"環境變數 {EnvVarName}: {envPath}");
        }
        else
        {
            attempted.Add($"環境變數 {EnvVarName}: (未設定)");
        }

        // 3. 與本組件同目錄。
        var assemblyDir = Path.GetDirectoryName(typeof(OrikagoToolResolver).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            var beside = Path.Combine(assemblyDir, ExeName);
            if (File.Exists(beside))
            {
                return beside;
            }

            attempted.Add($"組件目錄: {beside}");
        }

        // 4. 系統 PATH。
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim(), ExeName);
            }
            catch (ArgumentException)
            {
                continue; // PATH 中含無效字元的項目，略過。
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        attempted.Add($"系統 PATH 中的 {ExeName}");

        throw new OrikagocNotFoundException(
            "找不到 orikagoc sidecar 可執行檔。已嘗試以下位置：" + Environment.NewLine +
            string.Join(Environment.NewLine, attempted.Select(a => "  - " + a)) + Environment.NewLine +
            "修復方式（擇一）：" + Environment.NewLine +
            "  1. 在 src/go/orikagoc 目錄執行「go build -o orikagoc.exe .」（或使用 Orikago.Sdk 建置該 .goproj），" +
            "並將產出的可執行檔複製到 Orikago.CodeAnalysis.dll 所在目錄；" + Environment.NewLine +
            $"  2. 設定環境變數 {EnvVarName} 指向 orikagoc 可執行檔的完整路徑；" + Environment.NewLine +
            "  3. 將 orikagoc 所在目錄加入 PATH；" + Environment.NewLine +
            "  4. 於 API 呼叫時明確傳入工具路徑，或設定 OrikagoToolResolver.DefaultToolPath。");
    }
}

