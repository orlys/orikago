namespace Orikago.CodeAnalysis;

/// <summary>
/// 找不到 <c>orikagoc</c> 邊車可執行檔時擲出的例外狀況
/// </summary>
/// <remarks>
/// 訊息中包含所有已嘗試的搜尋位置與可行的修復步驟
/// </remarks>
public sealed class OrikagocNotFoundException : Exception
{
    /// <summary>
    /// 使用預設訊息建立例外狀況
    /// </summary>
    public OrikagocNotFoundException()
        : base("找不到 orikagoc 邊車。")
    {
    }

    /// <summary>
    /// 使用指定訊息建立例外狀況
    /// </summary>
    /// <param name="message">說明搜尋過程與修復方式的訊息</param>
    public OrikagocNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用指定訊息與內部例外狀況建立例外狀況
    /// </summary>
    /// <param name="message">說明搜尋過程與修復方式的訊息</param>
    /// <param name="innerException">造成此例外狀況的內部例外狀況</param>
    public OrikagocNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}