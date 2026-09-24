namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// check 指令的單筆診斷
/// </summary>
internal sealed record CheckDiagnosticDto
{
    /// <summary>診斷所在的檔案路徑</summary>
    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? File { get; set; }

    /// <summary>1-based 行號</summary>
    [JsonPropertyName("line")]
    public int Line { get; set; }

    /// <summary>1-based 欄號（UTF-16 字碼單位）</summary>
    [JsonPropertyName("col")]
    public int Column { get; set; }

    /// <summary>嚴重性（<c>error</c>、<c>warning</c> 或 <c>info</c>）</summary>
    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; set; }

    /// <summary>go/types 的診斷訊息原文</summary>
    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;
}