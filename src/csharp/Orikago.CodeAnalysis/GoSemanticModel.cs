namespace Orikago.CodeAnalysis;

using Orikago.CodeAnalysis.Internal;

using System.Globalization;

/// <summary>
/// 模組的語意模型，透過 <c>orikagoc symbol</c>（go/types 的 <c>Info.Uses</c>／<c>Info.Defs</c>）
/// 提供符號查詢
/// </summary>
/// <remarks>
/// 套件的檔案集合由建立此模型時指定的建置內容（<see cref="GoAnalysisOptions"/>）決定
/// </remarks>
public sealed class GoSemanticModel
{
    private readonly string _moduleDirectory;
    private readonly GoAnalysisOptions? _options;

    internal GoSemanticModel(string moduleDirectory, GoAnalysisOptions? options = null)
    {
        _moduleDirectory = moduleDirectory;
        _options = options;
    }

    /// <summary>
    /// 查詢指定檔案位置上的符號
    /// </summary>
    /// <param name="file">.go 檔路徑（必須屬於此編譯的模組目錄）</param>
    /// <param name="line">1-based 行號</param>
    /// <param name="column">
    /// 1-based 欄號，單位為 <b>UTF-16 字碼單位</b>（即 .NET 的 <see cref="string"/> 索引 + 1，
    /// 也是 Visual Studio 與 LSP 使用的單位）——<em>不是</em> go/token 的位元組欄號。
    /// 例如 <c>fmt.Println("你好", value)</c> 這一行中的 <c>value</c>，
    /// 位元組欄號為 20 但 UTF-16 欄號為 16；此處要傳 16
    /// </param>
    /// <returns>
    /// 該位置的符號資訊；該位置沒有符號（orikagoc 回傳 <c>{"name":null}</c>）時為
    /// <see langword="null"/>
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="file"/> 為 <see langword="null"/> 或空字串
    /// </exception>
    /// <exception cref="OrikagocNotFoundException">找不到 orikagoc 邊車</exception>
    /// <exception cref="InvalidOperationException">
    /// 邊車發生基礎結構錯誤（無法啟動、非零結束代碼或輸出無法解析）
    /// </exception>
    /// <exception cref="IOException">讀取邊車輸出失敗</exception>
    public GoSymbolInfo? GetSymbolAt(string file, int line, int column)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);

        // 組出 symbol 子命令，並附上建置內容選項
        var toolPath = OrikagoToolResolver.Resolve();
        List<string> arguments = [
            "symbol",
            file,
            line.ToString(CultureInfo.InvariantCulture),
            column.ToString(CultureInfo.InvariantCulture),
            "-dir",
            _moduleDirectory
        ];
        _options?.AppendSidecarArguments(arguments);

        // 執行邊車並解析回應
        var standardOutput = OrikagocInvoker.Invoke(toolPath, arguments);
        var symbol = Protocol.Deserialize<SymbolResultDto>(standardOutput, "orikagoc symbol");

        if (symbol.Name is not { } name)
        {
            // 該位置沒有符號
            return null;
        }

        var declaredAt = symbol.DeclaredAt is { } at
            ? new GoLocation(at.Line, at.Column, 0, at.File)
            : null;

        return new GoSymbolInfo(
            name,
            kind: symbol.Kind ?? string.Empty,
            type: symbol.Type ?? string.Empty,
            declaredAt);
    }
}