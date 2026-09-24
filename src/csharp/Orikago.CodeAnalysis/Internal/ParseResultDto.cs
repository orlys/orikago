namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// parse 指令的外層包裝：<c>{"ast":&lt;root or null&gt;,"errors":[...]}</c>
/// </summary>
internal sealed record ParseResultDto
{
    /// <summary>語法樹根節點</summary>
    /// <remarks>
    /// 完全無法剖析時為 <see langword="null"/>
    /// </remarks>
    [JsonPropertyName("ast")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AstNodeDto? Ast { get; set; }

    /// <summary>剖析錯誤清單</summary>
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ParseErrorDto>? Errors { get; set; }
}