namespace Orikago.CodeAnalysis.Internal;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

/// <summary>
/// 以避免死結的方式執行外部處理序，並擷取 UTF-8 標準輸出與標準錯誤（內部使用）
/// </summary>
internal static class ProcessRunner
{
    private static readonly UTF8Encoding s_utf8NoByteOrderMark = new(
        encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 執行指定的可執行檔並等待結束
    /// </summary>
    /// <remarks>
    /// 標準輸出與標準錯誤先由 <see cref="StreamDrainer"/> 同時排空，之後才寫入標準輸入並呼叫
    /// <see cref="Process.WaitForExit()"/>，避免管道緩衝區塞滿造成的死結
    /// </remarks>
    /// <param name="fileName">可執行檔路徑</param>
    /// <param name="arguments">
    /// 命令列引數（逐一加入 <see cref="ProcessStartInfo.ArgumentList"/>，不做殼層逸出）
    /// </param>
    /// <param name="standardInput">若非 <see langword="null"/>，以 UTF-8（無 BOM）寫入標準輸入後關閉</param>
    /// <param name="workingDirectory">工作目錄；<see langword="null"/> 表示沿用目前目錄</param>
    /// <param name="environment">要附加／覆寫的環境變數</param>
    /// <returns>結束代碼與擷取到的輸出</returns>
    /// <exception cref="InvalidOperationException">無法啟動外部工具</exception>
    /// <exception cref="IOException">寫入標準輸入或讀取輸出失敗</exception>
    public static ProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        // 重導向三條標準資料流，一律以 UTF-8 解碼
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (standardInput is not null)
        {
            // 需要寫入標準輸入時才重導向，且不輸出 BOM
            startInfo.RedirectStandardInput = true;
            startInfo.StandardInputEncoding = s_utf8NoByteOrderMark;
        }

        if (workingDirectory is not null)
        {
            // 指定了工作目錄
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            // 附加或覆寫環境變數
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            // 可執行檔不存在或無法執行
            throw new InvalidOperationException(
                message: $"無法啟動外部工具「{fileName}」：{ex.Message}",
                innerException: ex);
        }

        // 先同時排空標準輸出與標準錯誤，再寫標準輸入，最後才等待結束——標準的防死結順序
        var standardOutput = new StreamDrainer(process.StandardOutput);
        var standardError = new StreamDrainer(process.StandardError);

        if (standardInput is not null)
        {
            // 寫完即關閉標準輸入，讓子處理序讀到結尾
            using var writer = process.StandardInput;
            writer.Write(standardInput);
        }

        process.WaitForExit();

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput.WaitForText(),
            StandardError = standardError.WaitForText(),
        };
    }
}