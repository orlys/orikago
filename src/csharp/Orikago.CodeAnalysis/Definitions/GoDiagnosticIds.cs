namespace Orikago.CodeAnalysis.Definitions;

/// <summary>
/// <see cref="GoDiagnostic.Id"/> 使用的診斷識別碼
/// </summary>
/// <remarks>
/// 識別碼是對外契約：呼叫端以它分辨診斷來自剖析、型別檢查還是建置，值不得變更
/// </remarks>
public static class GoDiagnosticIds
{
    /// <summary>
    /// 剖析（go/parser）產生的語法錯誤
    /// </summary>
    public const string Parse = "GOPARSE";

    /// <summary>
    /// 型別檢查（go/types，經 <c>orikagoc check</c>）產生的診斷
    /// </summary>
    public const string TypeCheck = "GOTYPE";

    /// <summary>
    /// 建置（<c>go build</c>）失敗產生的診斷
    /// </summary>
    public const string Build = "GOBUILD";
}