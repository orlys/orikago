namespace Orikago.LanguageService;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A one-connection DAP relay that sits between Visual Studio's Debug Adapter Host and
/// <c>dlv dap</c>, for the sole purpose of silencing delve's "unknown memoryReference"
/// rejections.
/// </summary>
/// <remarks>
/// <para>
/// Why it exists: on every stop, the host probes readMemory with the
/// frame's instruction pointer (<c>count=0</c>). delve's readMemory only accepts
/// references it handed out itself - isAddressable() covers just strings
/// and slices (service/dap/server.go) - so a raw PC address is always
/// rejected, and the failure surfaces to the user as an error on every
/// single breakpoint hit. The engine metric <c>MemoryReferencesAreAddresses=0</c>
/// does not stop the probe (verified).
/// </para>
/// <para>
/// What it does: forwards both directions byte-for-byte, except that a
/// FAILED readMemory response whose message says "unknown memoryReference"
/// is rewritten into a successful empty read - which is exactly what delve
/// itself answers for a <c>count=0</c> read of a reference it does know. Real
/// reads (a string or slice variable's own reference) still go through
/// delve untouched, and every other message is passed along verbatim.
/// ponytail: single client, no pooling - dlv dap serves exactly one
/// session anyway.
/// </para>
/// </remarks>
internal static class DelveProxy
{
    private static readonly Regex s_readMemoryCommand =
        new("\"command\"\\s*:\\s*\"readMemory\"", RegexOptions.CultureInvariant);

    /// <summary>
    /// Starts listening on a free loopback port and relays the first
    /// connection to <paramref name="delvePort"/>.
    /// </summary>
    /// <param name="delvePort">The loopback port dlv dap is listening on.</param>
    /// <returns>The port the host should connect to.</returns>
    /// <exception cref="SocketException">
    /// No loopback port could be bound for the relay.
    /// </exception>
    public static int Start(int delvePort)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var proxyPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(async delegate
        {
            try
            {
                // Accept the host's single connection, then stop listening and dial delve
                using var host = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using var delve = new TcpClient();
                using var relayCancellation = new CancellationTokenSource();
                listener.Stop();
                try
                {
                    await delve
                        .ConnectAsync(IPAddress.Loopback, delvePort)
                        .ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    // dlv is not accepting on its port, so the debug session cannot start
                    ExtensionLog.Error(
                        "DAP relay could not connect to dlv on port " +
                        delvePort.ToString(CultureInfo.InvariantCulture) + ": " + ex.Message);
                    return;
                }

                var hostStream = host.GetStream();
                var delveStream = delve.GetStream();

                // seq -> memoryReference, so a rewritten response can
                // echo the address the request asked for. Concurrent:
                // the two pumps run at the same time, one writing it and
                // one reading/removing, and a plain Dictionary corrupts
                // (or throws) under that.
                var pendingReads = new ConcurrentDictionary<int, string>();

                // Relay both directions until either side closes
                var up = PumpAsync(
                    from: hostStream,
                    to: delveStream,
                    transform: message =>
                    {
                        RecordReadMemoryRequest(message, pendingReads);
                        return message;
                    },
                    cancellationToken: relayCancellation.Token);
                var down = PumpAsync(
                    from: delveStream,
                    to: hostStream,
                    transform: message => RewriteReadMemoryFailure(message, pendingReads),
                    cancellationToken: relayCancellation.Token);
                var finished = await Task.WhenAny(up, down).ConfigureAwait(false);

                // One side is done: stop the other pump before the sockets are disposed
                relayCancellation.Cancel();
                if (finished is { IsFaulted: true, Exception: { } failure })
                {
                    // The connection broke mid-session (dlv crashed or a socket was reset),
                    // as opposed to a side closing it cleanly
                    ExtensionLog.Error(
                        "DAP relay to dlv on port " +
                        delvePort.ToString(CultureInfo.InvariantCulture) + " failed: " +
                        failure.GetBaseException().Message);
                }
            }
            catch (Exception ex)
            {
                // The relay could not be set up (the host never connected, or its socket
                // failed before the pumps started), so the debug session cannot proceed
                ExtensionLog.Error(
                    "DAP relay for dlv on port " +
                    delvePort.ToString(CultureInfo.InvariantCulture) + " failed: " + ex);
            }
            finally
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception)
                {
                    // The listener is already stopped
                }
            }
        });

        return proxyPort;
    }

    /// <summary>
    /// Reads Content-Length framed messages from <paramref name="from"/>,
    /// passes each through <paramref name="transform"/>, and writes the
    /// result to <paramref name="to"/> with a corrected header.
    /// </summary>
    private static async Task PumpAsync(
        Stream from,
        Stream to,
        Func<string, string> transform,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        List<byte> pending = [];

        while (true)
        {
            var read = await from
                .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
            {
                // The source closed its side: this direction of the relay is done
                return;
            }

            for (var i = 0; i < read; i++)
            {
                pending.Add(buffer[i]);
            }

            // Forward every complete frame buffered so far
            while (true)
            {
                // Headers are ASCII; the body is UTF-8 and counted in bytes.
                // The scan covers everything buffered rather than a fixed
                // prefix: DAP allows headers beyond Content-Length, and a
                // cap smaller than the real header block would never find
                // the terminator, leaving the pump waiting forever on a
                // frame it had already received in full.
                var headerEnd = IndexOfHeaderEnd(pending);
                if (headerEnd < 0)
                {
                    // The header block is incomplete: wait for more bytes
                    break;
                }

                byte[] headBytes = [.. pending.GetRange(0, headerEnd)];
                var head = Encoding.ASCII.GetString(headBytes);
                if (Regex.Match(
                        input: head,
                        pattern: @"Content-Length:\s*(\d+)",
                        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    is not { Success: true } lengthMatch)
                {
                    // Unframeable stream: bail out rather than corrupt it.
                    ExtensionLog.Error("DAP relay received a frame without Content-Length.");
                    return;
                }

                var lengthText = lengthMatch.Groups[1].Value;
                var bodyLength = int.Parse(lengthText, CultureInfo.InvariantCulture);
                var total = headerEnd + 4 + bodyLength;
                if (pending.Count < total)
                {
                    // The body is incomplete: wait for more bytes
                    break;
                }

                byte[] bodyBytes = [.. pending.GetRange(headerEnd + 4, bodyLength)];
                pending.RemoveRange(0, total);

                // Transform the body and frame it again with a matching Content-Length
                var body = Encoding.UTF8.GetString(bodyBytes);
                var outBytes = Encoding.UTF8.GetBytes(transform(body));
                var header = Encoding.ASCII.GetBytes(
                    "Content-Length: " + outBytes.Length.ToString(CultureInfo.InvariantCulture) +
                    "\r\n\r\n");

                await to
                    .WriteAsync(header, 0, header.Length, cancellationToken)
                    .ConfigureAwait(false);
                await to
                    .WriteAsync(outBytes, 0, outBytes.Length, cancellationToken)
                    .ConfigureAwait(false);
                await to.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Byte offset of the CRLFCRLF that ends the header block, or <c>-1</c>.
    /// </summary>
    private static int IndexOfHeaderEnd(List<byte> pending)
    {
        for (var i = 0; (i + 3) < pending.Count; i++)
        {
            if ((pending[i] == (byte)'\r') && (pending[i + 1] == (byte)'\n') &&
                (pending[i + 2] == (byte)'\r') && (pending[i + 3] == (byte)'\n'))
            {
                // CRLFCRLF: the header block ends here
                return i;
            }
        }

        return -1;
    }

    private static void RecordReadMemoryRequest(
        string message,
        ConcurrentDictionary<int, string> pendingReads)
    {
        if ((message.IndexOf("\"readMemory\"", StringComparison.Ordinal) < 0) ||
            (message.IndexOf("\"request\"", StringComparison.Ordinal) < 0))
        {
            // Not a readMemory request: nothing to remember
            return;
        }

        if ((Regex.Match(message, "\"seq\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant)
                is { Success: true } seq) &&
            (Regex.Match(
                    input: message,
                    pattern: "\"memoryReference\"\\s*:\\s*\"([^\"]*)\"",
                    options: RegexOptions.CultureInvariant)
                is { Success: true } reference))
        {
            // Remember the address this request asked for, keyed by its sequence number
            var sequence = int.Parse(seq.Groups[1].Value, CultureInfo.InvariantCulture);
            pendingReads[sequence] = reference.Groups[1].Value;
        }
    }

    private static string RewriteReadMemoryFailure(
        string message,
        ConcurrentDictionary<int, string> pendingReads)
    {
        if ((message.IndexOf("unknown memoryReference", StringComparison.Ordinal) < 0) ||
            !s_readMemoryCommand.IsMatch(message))
        {
            // Not delve rejecting the host's probe: forward it verbatim
            return message;
        }

        if (Regex.Match(
                input: message,
                pattern: "\"request_seq\"\\s*:\\s*(\\d+)",
                options: RegexOptions.CultureInvariant)
            is not { Success: true } requestSeq)
        {
            // Without request_seq the response cannot be matched to its request
            return message;
        }

        var seq = Regex.Match(message, "\"seq\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant);
        var requestSeqText = requestSeq.Groups[1].Value;
        var requestSeqValue = int.Parse(requestSeqText, CultureInfo.InvariantCulture);
        if (!pendingReads.TryRemove(requestSeqValue, out var address) ||
            !IsAddressLiteral(address))
        {
            // The address is echoed into hand-built JSON below, so anything
            // that is not plainly an address is replaced rather than escaped
            // - a reference containing a quote or a backslash would produce
            // a malformed response and drop the session.
            address = "0x0";
        }

        // Same shape delve returns for a zero-length read it does accept:
        // success, the requested address, no data.
        return "{\"type\":\"response\"," +
            "\"request_seq\":" + requestSeqValue.ToString(CultureInfo.InvariantCulture) + "," +
            "\"success\":true," +
            "\"command\":\"readMemory\"," +
            "\"body\":{\"address\":\"" + address + "\",\"unreadableBytes\":0}," +
            "\"seq\":" + (seq.Success ? seq.Groups[1].Value : "0") + "}";
    }

    /// <summary>
    /// <see langword="true"/> for the hex addresses delve and the host actually exchange
    /// (<c>0x4a1c20</c>); <see langword="false"/> for anything needing JSON escaping.
    /// </summary>
    private static bool IsAddressLiteral(string? value)
    {
        if (value is not { Length: > 0 and <= 32 })
        {
            // Empty, or longer than any address delve hands out
            return false;
        }

        foreach (var c in value)
        {
            var isDigit = c is >= '0' and <= '9';
            var isHexLetter = c is (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isDigit && !isHexLetter && (c is not ('x' or 'X')))
            {
                // A character no hex address contains
                return false;
            }
        }

        return true;
    }
}
