namespace Orikago.CodeAnalysis.Internal;

using System.Runtime.ExceptionServices;

/// <summary>
/// 以專用執行緒把一個 <see cref="StreamReader"/> 讀到結尾（內部使用）
/// </summary>
/// <remarks>
/// 重導向的標準輸出與標準錯誤必須同時排空，否則任一管道的緩衝區塞滿就會死結
/// （見 docs/pitfalls.md 第 20 條）；公開 API 是同步的，所以用專用執行緒同步讀取，
/// 不在同步呼叫裡等待非同步讀取的結果
/// </remarks>
internal sealed class StreamDrainer
{
    private readonly StreamReader _reader;
    private readonly Thread _thread;
    private string _text = string.Empty;
    private ExceptionDispatchInfo? _failure;

    /// <summary>
    /// 建立並立即開始排空指定的讀取器
    /// </summary>
    /// <param name="reader">要讀到結尾的讀取器</param>
    public StreamDrainer(StreamReader reader)
    {
        _reader = reader;
        _thread = new Thread(Drain)
        {
            IsBackground = true,
        };
        _thread.Start();
    }

    /// <summary>
    /// 等待讀取完成並傳回讀到的全部文字
    /// </summary>
    /// <returns>讀取器從開始排空時的位置到結尾的全部文字</returns>
    /// <exception cref="IOException">讀取時發生 I/O 錯誤</exception>
    public string WaitForText()
    {
        _thread.Join();
        _failure?.Throw();
        return _text;
    }

    private void Drain()
    {
        try
        {
            _text = _reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            // 讀取執行緒上未攔下的例外狀況會終止整個處理序，改由 WaitForText 在呼叫端重新擲出
            _failure = ExceptionDispatchInfo.Capture(ex);
        }
    }
}