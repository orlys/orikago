namespace Orikago.CodeAnalysis;

/// <summary>
/// Go 語法樹中的一個節點，對應 go/ast 的節點型別
/// </summary>
/// <remarks>
/// <see cref="Kind"/> 即 go/ast 的型別名稱，例如 <c>FuncDecl</c>、<c>Ident</c>、<c>CallExpr</c>
/// </remarks>
public sealed class GoSyntaxNode
{
    /// <summary>
    /// 建立語法節點
    /// </summary>
    /// <param name="kind">go/ast 節點型別名稱</param>
    /// <param name="start">節點起始位置（1-based 行／欄，欄號為 UTF-16 字碼單位）</param>
    /// <param name="end">節點結束位置（1-based 行／欄，欄號為 UTF-16 字碼單位）</param>
    /// <param name="text">
    /// 節點的語彙基元文字；僅 <c>Ident</c> 與 <c>BasicLit</c> 有值，其餘為空字串
    /// </param>
    /// <param name="children">依原始碼順序排列的子節點</param>
    public GoSyntaxNode(
        string kind,
        GoLocation start,
        GoLocation end,
        string text,
        IReadOnlyList<GoSyntaxNode> children)
    {
        Kind = kind;
        Start = start;
        End = end;
        Text = text;
        Children = children;
    }

    /// <summary>取得 go/ast 節點型別名稱（例如 <c>FuncDecl</c>、<c>Ident</c>、<c>CallExpr</c>）</summary>
    public string Kind { get; }

    /// <summary>取得節點的起始位置</summary>
    public GoLocation Start { get; }

    /// <summary>取得節點的結束位置（緊接在節點之後的位置，與 go/ast 的 End 語意相同）</summary>
    public GoLocation End { get; }

    /// <summary>
    /// 取得節點的語彙基元文字
    /// </summary>
    /// <remarks>
    /// 僅 <c>Ident</c> 與 <c>BasicLit</c> 節點有值；其他節點為空字串
    /// </remarks>
    public string Text { get; }

    /// <summary>取得依原始碼順序排列的子節點清單</summary>
    public IReadOnlyList<GoSyntaxNode> Children { get; }

    /// <summary>
    /// 以深度優先（前序）方式列舉此節點之下的所有子孫節點（不含此節點本身）
    /// </summary>
    /// <returns>子孫節點的延遲列舉</returns>
    public IEnumerable<GoSyntaxNode> DescendantNodes()
    {
        // 子節點反序推入堆疊，彈出順序即為原始碼順序
        Stack<GoSyntaxNode> pending = [];
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            pending.Push(Children[i]);
        }

        // 每彈出一個節點就交出它，再把它的子節點反序推入
        while (pending.TryPop(out var node))
        {
            yield return node;

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(node.Children[i]);
            }
        }
    }

    /// <summary>
    /// 傳回第一個 <see cref="Kind"/> 符合的直接子節點
    /// </summary>
    /// <remarks>
    /// 找不到時傳回 <see langword="null"/>
    /// </remarks>
    /// <param name="kind">要尋找的 go/ast 節點型別名稱（區分大小寫）</param>
    /// <returns>符合的第一個直接子節點，或 <see langword="null"/></returns>
    public GoSyntaxNode? FirstChild(string kind)
    {
        foreach (var child in Children)
        {
            if (string.Equals(child.Kind, kind, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>以 <c>Kind [start..end)</c> 形式傳回節點的文字表示</summary>
    /// <returns>節點的文字表示；有語彙基元文字時一併附上</returns>
    public override string ToString()
    {
        if (Text is { Length: > 0 })
        {
            // Ident／BasicLit 附上語彙基元文字
            return $"{Kind}({Text}) {Start}..{End}";
        }

        return $"{Kind} {Start}..{End}";
    }
}