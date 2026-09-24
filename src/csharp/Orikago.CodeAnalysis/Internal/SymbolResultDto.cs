namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json.Serialization;

/// <summary>
/// symbol 指令的回應
/// </summary>
/// <remarks>
/// 查無符號時 name 為 <see langword="null"/>
/// </remarks>
internal sealed record SymbolResultDto
{
    /// <summary>符號名稱</summary>
    /// <remarks>
    /// 該位置沒有符號時為 <see langword="null"/>
    /// </remarks>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>符號種類</summary>
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>符號的型別字串</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    /// <summary>符號宣告處的位置</summary>
    [JsonPropertyName("declaredAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeclaredAtDto? DeclaredAt { get; set; }
}