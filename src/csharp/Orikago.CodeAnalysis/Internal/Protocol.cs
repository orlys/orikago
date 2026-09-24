namespace Orikago.CodeAnalysis.Internal;

using System.Text.Json;

/// <summary>
/// orikagoc JSON 協定的共用序列化設定與反序列化輔助方法（內部使用）
/// </summary>
/// <remarks>
/// 各傳輸物件以 <c>[JsonPropertyName]</c> 明確對應協定欄位；比對仍不分大小寫，與既有行為相容
/// </remarks>
internal static class Protocol
{
    /// <summary>共用的 <see cref="JsonSerializerOptions"/>：屬性名稱比對不分大小寫</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 將邊車輸出的 JSON 反序列化為協定物件
    /// </summary>
    /// <typeparam name="T">協定物件型別</typeparam>
    /// <param name="json">邊車的標準輸出</param>
    /// <param name="toolDescription">
    /// 錯誤訊息中用來指出是哪個子命令的描述，例如 <c>orikagoc parse</c>
    /// </param>
    /// <returns>反序列化後的協定物件</returns>
    /// <exception cref="InvalidOperationException">輸出不是有效的 JSON，或為 JSON <c>null</c></exception>
    public static T Deserialize<T>(string json, string toolDescription)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"{toolDescription} 輸出了空的 JSON（null）。原始輸出：{Truncate(json)}");
        }
        catch (JsonException ex)
        {
            // 邊車輸出不是有效的 JSON，屬基礎結構錯誤
            throw new InvalidOperationException(
                message: $"{toolDescription} 輸出的 JSON 無法解析：{ex.Message}{Environment.NewLine}" +
                    $"原始輸出：{Truncate(json)}",
                innerException: ex);
        }
    }

    private static string Truncate(string text)
    {
        const int MAX_LENGTH = 2000;

        if (text is { Length: <= MAX_LENGTH })
        {
            return text;
        }

        return text[..MAX_LENGTH] + "…（截斷）";
    }
}