namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// parse 指令的剖析錯誤：<c>{"line":L,"col":C,"msg":"..."}</c>
/// </summary>
internal sealed record ParseErrorDto
{
    /// <summary>1-based 行號</summary>
    [JsonPropertyName("line")]
    public int Line { get; set; }

    /// <summary>1-based 欄號（UTF-16 字碼單位）</summary>
    [JsonPropertyName("col")]
    public int Column { get; set; }

    /// <summary>go/parser 的錯誤訊息原文</summary>
    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}