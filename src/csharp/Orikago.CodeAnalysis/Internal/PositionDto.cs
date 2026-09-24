namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// 協定中的位置物件：<c>{"line":L,"col":C,"offset":O}</c>
/// </summary>
internal sealed record PositionDto
{
    /// <summary>1-based 行號</summary>
    [JsonPropertyName("line")]
    public int Line { get; set; }

    /// <summary>1-based 欄號（UTF-16 字碼單位，邊車已轉換）</summary>
    [JsonPropertyName("col")]
    public int Column { get; set; }

    /// <summary>自檔案開頭起算的位元組位移</summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}