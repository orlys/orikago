namespace Orikago.CodeAnalysis;

using Orikago.CodeAnalysis.Definitions;
using Orikago.CodeAnalysis.Internal;

/// <summary>
/// 一份 Go 原始檔的語法樹，由 orikagoc 邊車（go/parser，模式 <c>ParseComments|AllErrors</c>）產生
/// </summary>
public sealed class GoSyntaxTree
{
    private readonly IReadOnlyList<GoDiagnostic> _diagnostics;

    private GoSyntaxTree(
        GoSyntaxNode? root,
        string filePath,
        IReadOnlyList<GoDiagnostic> diagnostics)
    {
        Root = root;
        FilePath = filePath;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// 取得語法樹的根節點（<c>File</c>）
    /// </summary>
    /// <remarks>
    /// 僅在原始碼完全無法剖析（orikagoc 回傳 <c>"ast": null</c>）時為 <see langword="null"/>
    /// </remarks>
    public GoSyntaxNode? Root { get; }

    /// <summary>
    /// 取得此語法樹對應的檔案路徑
    /// </summary>
    /// <remarks>
    /// 以 <see cref="ParseText"/> 剖析純文字且未指定路徑時為 <c>&lt;source&gt;</c>
    /// </remarks>
    public string FilePath { get; }

    /// <summary>
    /// 取得剖析期間收集到的診斷（識別碼 <c>GOPARSE</c>，嚴重性一律為錯誤）
    /// </summary>
    /// <returns>唯讀的診斷清單；無錯誤時為空清單</returns>
    public IReadOnlyList<GoDiagnostic> GetDiagnostics()
    {
        return _diagnostics;
    }

    /// <summary>
    /// 剖析一段 Go 原始碼文字
    /// </summary>
    /// <remarks>
    /// 原始碼經由標準輸入送給 <c>orikagoc parse -</c>，不落地暫存檔
    /// </remarks>
    /// <param name="source">Go 原始碼</param>
    /// <param name="path">
    /// 回報位置時使用的檔案路徑；空字串時 orikagoc 以 <c>&lt;source&gt;</c> 回報
    /// </param>
    /// <returns>
    /// 剖析結果語法樹（即使有語法錯誤也會傳回，錯誤見 <see cref="GetDiagnostics"/>）
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> 為 <see langword="null"/>
    /// </exception>
    /// <exception cref="OrikagocNotFoundException">找不到 orikagoc 邊車</exception>
    /// <exception cref="InvalidOperationException">
    /// 邊車發生基礎結構錯誤（無法啟動、非零結束代碼或輸出無法解析）
    /// </exception>
    /// <exception cref="IOException">
    /// 寫入邊車的標準輸入或讀取其輸出失敗（例如邊車提前結束）
    /// </exception>
    public static GoSyntaxTree ParseText(string source, string path = "")
    {
        ArgumentNullException.ThrowIfNull(source);

        // 組出 parse 子命令；有指定路徑時讓邊車以該路徑回報位置
        var toolPath = OrikagoToolResolver.Resolve();
        List<string> arguments = ["parse", "-"];
        if (path is { Length: > 0 })
        {
            arguments.Add("--path");
            arguments.Add(path);
        }

        // 原始碼經標準輸入送出，不落地暫存檔
        var standardOutput = OrikagocInvoker.Invoke(toolPath, arguments, standardInput: source);
        var reportedPath = path is { Length: > 0 } ? path : "<source>";
        return FromParseJson(standardOutput, reportedPath);
    }

    /// <summary>
    /// 剖析磁碟上的 Go 原始檔（<c>orikagoc parse &lt;file.go&gt;</c>）
    /// </summary>
    /// <param name="path">要剖析的 .go 檔路徑</param>
    /// <returns>
    /// 剖析結果語法樹（即使有語法錯誤也會傳回，錯誤見 <see cref="GetDiagnostics"/>）
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> 為 <see langword="null"/> 或空字串
    /// </exception>
    /// <exception cref="OrikagocNotFoundException">找不到 orikagoc 邊車</exception>
    /// <exception cref="InvalidOperationException">
    /// 邊車發生基礎結構錯誤（無法啟動、非零結束代碼或輸出無法解析）
    /// </exception>
    /// <exception cref="IOException">讀取邊車輸出失敗</exception>
    public static GoSyntaxTree ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var toolPath = OrikagoToolResolver.Resolve();
        var standardOutput = OrikagocInvoker.Invoke(toolPath, ["parse", path]);
        return FromParseJson(standardOutput, path);
    }

    private static GoSyntaxTree FromParseJson(string json, string filePath)
    {
        var parseResult = Protocol.Deserialize<ParseResultDto>(json, "orikagoc parse");

        // 完全無法剖析時邊車不回傳語法樹
        var root = parseResult.Ast is { } ast ? ToNode(ast, filePath) : null;

        // 剖析錯誤一律轉成錯誤等級的 GOPARSE 診斷
        List<GoDiagnostic> diagnostics = [];
        foreach (var error in parseResult.Errors ?? [])
        {
            diagnostics.Add(new GoDiagnostic(
                id: GoDiagnosticIds.Parse,
                severity: GoDiagnosticSeverity.Error,
                location: new GoLocation(error.Line, error.Column, 0, filePath),
                message: error.Message));
        }

        // 語法樹保存唯讀視圖，GetDiagnostics 的呼叫端無法轉回 List 改寫樹的狀態
        return new GoSyntaxTree(root, filePath, diagnostics.AsReadOnly());
    }

    private static GoSyntaxNode ToNode(AstNodeDto node, string filePath)
    {
        IReadOnlyList<GoSyntaxNode> children =
            [.. (node.Children ?? []).Select(child => ToNode(child, filePath))];

        return new GoSyntaxNode(
            kind: node.Kind,
            start: ToLocation(node.Position, filePath),
            end: ToLocation(node.End, filePath),
            text: node.Text ?? string.Empty,
            children);
    }

    private static GoLocation ToLocation(PositionDto? position, string filePath)
    {
        if (position is null)
        {
            // 協定未帶位置時以 0 行 0 欄表示不明
            return new GoLocation(0, 0, 0, filePath);
        }

        return new GoLocation(position.Line, position.Column, position.Offset, filePath);
    }
}