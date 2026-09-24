namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// symbol 指令中 declaredAt 的位置
/// </summary>
internal sealed record DeclaredAtDto
{
    /// <summary>宣告所在的檔案路徑</summary>
    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? File { get; set; }

    /// <summary>1-based 行號</summary>
    [JsonPropertyName("line")]
    public int Line { get; set; }

    /// <summary>1-based 欄號（UTF-16 字碼單位）</summary>
    [JsonPropertyName("col")]
    public int Column { get; set; }
}