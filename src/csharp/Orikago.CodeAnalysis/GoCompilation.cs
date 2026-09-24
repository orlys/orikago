namespace Orikago.CodeAnalysis;

using Orikago.CodeAnalysis.Definitions;
using Orikago.CodeAnalysis.Internal;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 表示一個 Go 模組的編譯
/// </summary>
/// <remarks>
/// 型別檢查透過 <c>orikagoc check</c>（go/types），產出二進位檔則呼叫真實的 <c>go build -o</c>
/// </remarks>
public sealed partial class GoCompilation
{
    private GoCompilation(string moduleDirectory)
    {
        ModuleDirectory = moduleDirectory;
    }

    /// <summary>取得此編譯對應的模組目錄（包含 go.mod 的目錄）</summary>
    public string ModuleDirectory { get; }

    /// <summary>
    /// 為指定的模組目錄建立編譯
    /// </summary>
    /// <param name="moduleDirectory">Go 模組目錄（包含 go.mod）</param>
    /// <returns>新的 <see cref="GoCompilation"/></returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="moduleDirectory"/> 為 <see langword="null"/>、空字串或無效路徑
    /// </exception>
    /// <exception cref="PathTooLongException">路徑超過系統定義的長度上限</exception>
    /// <exception cref="DirectoryNotFoundException">目錄不存在時擲出</exception>
    public static GoCompilation Create(string moduleDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleDirectory);

        var fullPath = Path.GetFullPath(moduleDirectory);
        if (!Directory.Exists(fullPath))
        {
            // 模組目錄不存在
            throw new DirectoryNotFoundException($"找不到模組目錄：{fullPath}");
        }

        return new GoCompilation(fullPath);
    }

    /// <summary>
    /// 以預設建置內容對整個模組執行型別檢查（<c>orikagoc check</c>）並傳回診斷
    /// </summary>
    /// <remarks>
    /// 預設建置內容為目前的 <c>GOOS</c>／<c>GOARCH</c>，且無額外標籤
    /// </remarks>
    /// <returns>唯讀的診斷清單（識別碼 <c>GOTYPE</c>）；模組健全時為空清單</returns>
    /// <exception cref="OrikagocNotFoundException">找不到 orikagoc 邊車</exception>
    /// <exception cref="InvalidOperationException">
    /// 邊車發生基礎結構錯誤（無法啟動、非零結束代碼或輸出無法解析）
    /// </exception>
    /// <exception cref="IOException">讀取邊車輸出失敗</exception>
    public IReadOnlyList<GoDiagnostic> GetDiagnostics()
    {
        return GetDiagnostics(null);
    }

    /// <summary>
    /// 在指定的建置內容下對整個模組執行型別檢查（<c>orikagoc check</c>）並傳回診斷
    /// </summary>
    /// <remarks>
    /// <para>
    /// 型別檢查透過 <c>go list</c>（golang.org/x/tools/go/packages）載入套件，因此模組相依套件、
    /// go.work 工作區與 vendor 目錄的解析方式與 <c>go build</c> 完全一致
    /// </para>
    /// <para>
    /// 傳入與 <see cref="Emit(string, GoEmitOptions?)"/> 相同的選項，可確保檢查的檔案集合
    /// 與實際建置的檔案集合相同（例如 <c>//go:build linux</c> 的檔案）
    /// </para>
    /// </remarks>
    /// <param name="options">
    /// 建置內容選項（<see cref="GoAnalysisOptions.OS"/>／<see cref="GoAnalysisOptions.Arch"/>／
    /// <see cref="GoAnalysisOptions.Tags"/>）。<see langword="null"/> 表示使用預設建置內容
    /// </param>
    /// <returns>唯讀的診斷清單（識別碼 <c>GOTYPE</c>）；模組健全時為空清單</returns>
    /// <exception cref="OrikagocNotFoundException">找不到 orikagoc 邊車</exception>
    /// <exception cref="InvalidOperationException">
    /// 邊車發生基礎結構錯誤（無法啟動、非零結束代碼或輸出無法解析）
    /// </exception>
    /// <exception cref="IOException">讀取邊車輸出失敗</exception>
    public IReadOnlyList<GoDiagnostic> GetDiagnostics(GoAnalysisOptions? options)
    {
        // 組出 check 子命令，並附上建置內容選項
        var toolPath = OrikagoToolResolver.Resolve();
        List<string> arguments = ["check", ModuleDirectory];
        options?.AppendSidecarArguments(arguments);

        // 執行邊車並解析回應
        var standardOutput = OrikagocInvoker.Invoke(toolPath, arguments);
        if (Protocol
                .Deserialize<CheckResultDto>(standardOutput, "orikagoc check")
                .Diagnostics is not { Count: > 0 } reportedDiagnostics)
        {
            // 模組健全，沒有任何診斷
            return [];
        }

        // 邊車回報的每一筆都轉成 GOTYPE 診斷
        List<GoDiagnostic> diagnostics = [];
        foreach (var reported in reportedDiagnostics)
        {
            diagnostics.Add(new GoDiagnostic(
                id: GoDiagnosticIds.TypeCheck,
                severity: MapSeverity(reported.Severity),
                location: new GoLocation(reported.Line, reported.Column, 0, reported.File),
                message: reported.Message));
        }

        // 回傳唯讀視圖，呼叫端無法轉回 List 改寫
        return diagnostics.AsReadOnly();
    }

    /// <summary>
    /// 呼叫真實的 <c>go build -o</c> 產出二進位檔
    /// </summary>
    /// <param name="outputPath">輸出檔路徑（<c>go build -o</c> 的引數）</param>
    /// <param name="options">
    /// 建置選項：<see cref="GoAnalysisOptions.OS"/>／<see cref="GoAnalysisOptions.Arch"/> 對應
    /// <c>GOOS</c>／<c>GOARCH</c> 環境變數，另支援 <c>-tags</c>（<see cref="GoAnalysisOptions.Tags"/>）
    /// 與 <c>-trimpath</c>（<see cref="GoEmitOptions.TrimPath"/>）。
    /// <see langword="null"/> 表示全部使用預設值。
    /// 同一個實體也可以傳給 <see cref="GetDiagnostics(GoAnalysisOptions?)"/>，讓檢查與建置的檔案集合一致
    /// </param>
    /// <returns>
    /// 建置結果。建置錯誤（<c>go build</c> 結束代碼非零）不擲出例外狀況，而是化為
    /// <see cref="GoEmitResult.Diagnostics"/> 中識別碼 <c>GOBUILD</c> 的診斷
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="outputPath"/> 為 <see langword="null"/>、空字串或無效路徑
    /// </exception>
    /// <exception cref="PathTooLongException">輸出檔路徑超過系統定義的長度上限</exception>
    /// <exception cref="InvalidOperationException">找不到 <c>go</c> 工具鏈或無法啟動時擲出</exception>
    /// <exception cref="IOException">讀取 <c>go build</c> 的輸出失敗</exception>
    public GoEmitResult Emit(string outputPath, GoEmitOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        // 組出 go build 引數：-trimpath 與 -tags 走命令列
        List<string> arguments = ["build", "-o", Path.GetFullPath(outputPath)];
        if (options is { TrimPath: true })
        {
            arguments.Add("-trimpath");
        }

        if (options is { Tags.Count: > 0 })
        {
            arguments.Add("-tags");
            arguments.Add(string.Join(',', options.Tags));
        }

        arguments.Add(".");

        // GOOS／GOARCH 走環境變數，未指定時沿用目前環境
        Dictionary<string, string> environment = [];
        if (options?.OS is { Length: > 0 } operatingSystem)
        {
            environment["GOOS"] = operatingSystem;
        }

        if (options?.Arch is { Length: > 0 } architecture)
        {
            environment["GOARCH"] = architecture;
        }

        // 執行真實的 go 工具鏈
        var result = default(ProcessResult);
        try
        {
            result = ProcessRunner.Run(
                fileName: "go",
                arguments,
                workingDirectory: ModuleDirectory,
                environment: environment is { Count: > 0 } ? environment : null);
        }
        catch (InvalidOperationException ex)
        {
            // 找不到 go 工具鏈或無法啟動
            throw new InvalidOperationException(
                message: "無法執行 go 工具鏈。請確認已安裝 Go 並將其加入 PATH（https://go.dev/dl/）。",
                innerException: ex);
        }

        // 把標準錯誤解析成逐行診斷
        var diagnostics = ParseGoBuildOutput(result.StandardError);
        var success = result.ExitCode == 0;

        if (!success && (diagnostics is { Count: 0 }))
        {
            // 建置失敗但標準錯誤無法解析成逐行診斷，保留整段輸出當作單筆診斷，不吞掉錯誤
            diagnostics.Add(new GoDiagnostic(
                id: GoDiagnosticIds.Build,
                severity: GoDiagnosticSeverity.Error,
                location: new GoLocation(0, 0, 0, ModuleDirectory),
                message: string.IsNullOrWhiteSpace(result.StandardError)
                    ? FormattableString.Invariant(
                        $"go build 以結束代碼 {result.ExitCode} 失敗，且無標準錯誤輸出。")
                    : result.StandardError.Trim()));
        }

        return new GoEmitResult(success, diagnostics.AsReadOnly(), outputPath);
    }

    /// <summary>
    /// 以預設建置內容取得此模組的語意模型（符號查詢由 <c>orikagoc symbol</c> 支援）
    /// </summary>
    /// <returns>此模組的 <see cref="GoSemanticModel"/></returns>
    public GoSemanticModel GetSemanticModel()
    {
        return GetSemanticModel(null);
    }

    /// <summary>
    /// 在指定的建置內容下取得此模組的語意模型
    /// </summary>
    /// <remarks>
    /// 選項會決定符號查詢時哪些檔案屬於該套件（<c>GOOS</c>／<c>GOARCH</c>／建置標籤）
    /// </remarks>
    /// <param name="options">
    /// 建置內容選項；<see langword="null"/> 表示使用預設建置內容。
    /// 與 <see cref="GetDiagnostics(GoAnalysisOptions?)"/> 及
    /// <see cref="Emit(string, GoEmitOptions?)"/> 使用同一組選項即可保證三者看到相同的檔案集合
    /// </param>
    /// <returns>此模組的 <see cref="GoSemanticModel"/></returns>
    public GoSemanticModel GetSemanticModel(GoAnalysisOptions? options)
    {
        return new GoSemanticModel(ModuleDirectory, options);
    }

    private static GoDiagnosticSeverity MapSeverity(string? severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "warning" => GoDiagnosticSeverity.Warning,
            "info" => GoDiagnosticSeverity.Info,
            _ => GoDiagnosticSeverity.Error,
        };
    }

    // 副檔名集合與 SDK 的 @(GoNativeCompile) 一致：cgo 建置會原封轉發
    // C/C++/ObjC/Fortran 編譯器的「helper.cpp:3:5: error: ...」診斷行
    [GeneratedRegex(
        @"^(?<file>.+?\.(?:go|swigcxx|swig|sx|s|S|cxx|cpp|cc|c|" +
        @"hxx|hpp|hh|h|mm|m|f90|for|F|f))" +
        @":(?<line>\d+):(?:(?<column>\d+):)?\s*(?<message>.+)$")]
    private static partial Regex GoBuildErrorLine();

    private List<GoDiagnostic> ParseGoBuildOutput(string standardError)
    {
        List<GoDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(standardError))
        {
            // 沒有任何輸出可解析
            return diagnostics;
        }

        // go build 的欄號是位元組欄，而本組件的契約是所有欄號一律 UTF-16
        // 字碼單位（見 GoLocation）——邊車的 parse／check 已轉換，這裡是
        // 唯一直接消費 go 工具鏈標準錯誤的路徑，必須自己轉
        var lineCache = new Dictionary<string, byte[][]?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in standardError.Split("\n", StringSplitOptions.None))
        {
            var line = rawLine.TrimEnd('\r').TrimEnd();
            if ((line is { Length: 0 }) || line.StartsWith('#'))
            {
                // 空行與「# package」群組標頭
                continue;
            }

            if (GoBuildErrorLine().Match(line) is not { Success: true } match)
            {
                // 縮排的補充說明行等，附屬於前一筆錯誤
                continue;
            }

            var file = match.Groups["file"].Value;
            if (!Path.IsPathRooted(file))
            {
                // 相對路徑以模組目錄為基準
                file = Path.GetFullPath(Path.Combine(ModuleDirectory, file));
            }

            // 位元組欄號轉成 UTF-16 欄號；沒有欄號時為 0
            var lineNumber = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
            var byteColumn = match.Groups["column"] is { Success: true } columnGroup
                ? int.Parse(columnGroup.Value, CultureInfo.InvariantCulture)
                : 0;
            var column = ToUtf16Column(lineCache, file, lineNumber, byteColumn);

            diagnostics.Add(new GoDiagnostic(
                id: GoDiagnosticIds.Build,
                severity: GoDiagnosticSeverity.Error,
                location: new GoLocation(lineNumber, column, 0, file),
                message: match.Groups["message"].Value));
        }

        return diagnostics;
    }

    /// <summary>
    /// 把 go/token 的 1-based 位元組欄號轉成 1-based UTF-16 字碼單位欄號
    /// </summary>
    /// <remarks>
    /// 與 orikagoc 的 toUTF16Col 同一套規則：無法解析的欄號原樣傳回（純 ASCII 時本來就相等）；
    /// 行首 UTF-8 BOM 佔 3 個位元組但 0 個 UTF-16 單位（VS／.NET 的緩衝區會剝掉它）
    /// </remarks>
    private static int ToUtf16Column(
        Dictionary<string, byte[][]?> cache,
        string file,
        int line,
        int byteColumn)
    {
        if ((byteColumn <= 1) || (line < 1))
        {
            // 行首欄號與無效行號不需換算
            return byteColumn;
        }

        if (!cache.TryGetValue(file, out var lines))
        {
            // 第一次遇到此檔，讀入並依行切開
            try
            {
                lines = SplitLines(File.ReadAllBytes(file));
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 讀不到檔案時記住這個結果，不再重試
                lines = null;
            }

            cache[file] = lines;
        }

        if ((lines is null) || (line > lines.Length))
        {
            // 讀不到檔案或行號超出檔案範圍，無法換算時原樣傳回
            return byteColumn;
        }

        var source = lines[line - 1];
        var byteOffset = byteColumn - 1;
        if (byteOffset > source.Length)
        {
            // 超出行尾（例如指向最後一個語彙基元之後）：算整行再保留溢出量
            return Utf16Length(source) + 1 + (byteOffset - source.Length);
        }

        return Utf16Length(source.AsSpan(0, byteOffset)) + 1;
    }

    /// <summary>把位元組內容依行切開，各行不含結尾符</summary>
    /// <remarks>
    /// 與 orikagoc 的 splitLines 對齊：LF／CRLF 皆處理
    /// </remarks>
    private static byte[][] SplitLines(byte[] source)
    {
        List<byte[]> lines = [];
        var start = default(int);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != (byte)'\n')
            {
                continue;
            }

            // 行尾的 CR 不屬於該行內容
            var end = i;
            if ((end > start) && (source[end - 1] == (byte)'\r'))
            {
                end--;
            }

            lines.Add(source[start..end]);
            start = i + 1;
        }

        lines.Add(source[start..]);
        return [.. lines];
    }

    /// <summary>
    /// 計算位元組序列需要的 UTF-16 字碼單位數
    /// </summary>
    /// <remarks>
    /// 無效 UTF-8 以取代字元計數；開頭的 U+FEFF（BOM）計為 0 單位，理由同 orikagoc 的 utf16Len
    /// </remarks>
    private static int Utf16Length(ReadOnlySpan<byte> bytes)
    {
        var length = default(int);
        var atStart = true;
        while (!bytes.IsEmpty)
        {
            Rune.DecodeFromUtf8(bytes, out var rune, out var consumed);
            if (consumed <= 0)
            {
                // 保證前進一個位元組，避免無效序列造成無窮迴圈
                consumed = 1;
            }

            if (!(atStart && (rune.Value == 0xFEFF)))
            {
                // 開頭的 BOM 以外，一律依字元實際的 UTF-16 長度計數
                length += rune.Utf16SequenceLength;
            }

            atStart = false;
            bytes = bytes[consumed..];
        }

        return length;
    }
}