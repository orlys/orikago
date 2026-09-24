namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// check 指令的外層包裝：<c>{"diagnostics":[...]}</c>
/// </summary>
internal sealed record CheckResultDto
{
    /// <summary>型別檢查診斷清單</summary>
    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CheckDiagnosticDto>? Diagnostics { get; set; }
}