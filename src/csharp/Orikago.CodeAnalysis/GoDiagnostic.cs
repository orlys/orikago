namespace Orikago.CodeAnalysis;

/// <summary>
/// 表示一筆由剖析（GOPARSE）、型別檢查（GOTYPE）或建置（GOBUILD）產生的診斷
/// </summary>
public sealed class GoDiagnostic
{
    /// <summary>
    /// 建立一筆診斷
    /// </summary>
    /// <param name="id">診斷識別碼，例如 <c>GOPARSE</c>、<c>GOTYPE</c> 或 <c>GOBUILD</c></param>
    /// <param name="severity">嚴重性等級</param>
    /// <param name="location">診斷所指向的原始碼位置</param>
    /// <param name="message">診斷訊息（來自 go/parser、go/types 或 go build 的原文）</param>
    public GoDiagnostic(
        string id,
        GoDiagnosticSeverity severity,
        GoLocation location,
        string message)
    {
        Id = id;
        Severity = severity;
        Location = location;
        Message = message;
    }

    /// <summary>取得診斷識別碼（<c>GOPARSE</c>／<c>GOTYPE</c>／<c>GOBUILD</c>）</summary>
    public string Id { get; }

    /// <summary>取得嚴重性等級</summary>
    public GoDiagnosticSeverity Severity { get; }

    /// <summary>取得診斷所指向的原始碼位置</summary>
    public GoLocation Location { get; }

    /// <summary>取得診斷訊息</summary>
    public string Message { get; }

    /// <summary>以 <c>ID severity file(line,col): message</c> 形式傳回診斷的文字表示</summary>
    /// <returns>診斷的文字表示</returns>
    public override string ToString()
    {
        return $"{Id} {Severity.ToString().ToLowerInvariant()} {Location}: {Message}";
    }
}