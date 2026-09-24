namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// 協定中的 AST 節點
/// </summary>
internal sealed record AstNodeDto
{
    /// <summary>go/ast 節點型別名稱</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>節點起始位置</summary>
    [JsonPropertyName("pos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PositionDto? Position { get; set; }

    /// <summary>節點結束位置</summary>
    [JsonPropertyName("end")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PositionDto? End { get; set; }

    /// <summary>語彙基元文字</summary>
    /// <remarks>
    /// 僅 <c>Ident</c> 與 <c>BasicLit</c> 有值
    /// </remarks>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>依原始碼順序排列的子節點</summary>
    [JsonPropertyName("children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AstNodeDto>? Children { get; set; }
}