namespace Orikago.LanguageService;

using System;
using System.Net;
using System.Runtime.InteropServices;

/// <summary>
/// Answers "which loopback port is process N listening on?".
/// </summary>
/// <remarks>
/// <see cref="System.Net.NetworkInformation.IPGlobalProperties.GetActiveTcpListeners"/>
/// reports endpoints but not their owners, which is not enough: a readiness
/// check that only asks "is anything listening on this port?" cannot tell
/// the process it started apart from whatever else happened to bind the
/// same port. GetExtendedTcpTable carries the owning PID, so dlv can be let
/// pick its own port (:0) and be asked afterwards which one it got - there
/// is then no window in which the port could belong to someone else.
/// </remarks>
internal static class TcpListenerTable
{
    /// <summary>
    /// Returns the loopback port <paramref name="processId"/> is listening on.
    /// </summary>
    /// <remarks>
    /// Only IPv4 loopback is considered - that is what dlv is told to bind.
    /// </remarks>
    /// <param name="processId">The process whose listener is wanted.</param>
    /// <returns>
    /// The port, or <c>0</c> when the process is not listening yet or the table is unreadable.
    /// </returns>
    public static int FindLoopbackListenerPort(int processId)
    {
        const int ERROR_INSUFFICIENT_BUFFER = 122;
        const uint MIB_TCP_STATE_LISTEN = 2;

        // A first call with no buffer reports the size the table needs
        var size = default(int);
        var result = QueryListenerTable(IntPtr.Zero, ref size);
        if ((result != ERROR_INSUFFICIENT_BUFFER) && (result != 0))
        {
            // The table size cannot be queried. This runs in a 100 ms readiness poll, so it is
            // reported as "not listening"; a persistent failure surfaces once, as the
            // DlvNotListening timeout of DelveServer.
            return 0;
        }

        var table = Marshal.AllocHGlobal(size);
        try
        {
            if (QueryListenerTable(table, ref size) != 0)
            {
                // The table could not be read this round (it may have grown meanwhile); the
                // readiness poll simply asks again
                return 0;
            }

            // Walk the rows for a LISTEN socket on 127.0.0.1 owned by the process
            var rowCount = Marshal.ReadInt32(table);
            var row = table + sizeof(int);
            var rowSize = Marshal.SizeOf<TcpOwnerProcessRow>();
            var loopback = (uint)IPAddress.HostToNetworkOrder(unchecked((int)0x7F000001));

            for (var i = 0; i < rowCount; i++)
            {
                var entry = Marshal.PtrToStructure<TcpOwnerProcessRow>(row);
                row += rowSize;

                if ((entry.OwningProcessId != (uint)processId) ||
                    (entry.State != MIB_TCP_STATE_LISTEN) ||
                    (entry.LocalAddress != loopback))
                {
                    // Another process, another state, or another address
                    continue;
                }

                return (entry.LocalPort1 << 8) | entry.LocalPort2;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }

        return 0;
    }

    private static int QueryListenerTable(IntPtr table, ref int size)
    {
        const int AF_INET = 2;
        const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        return GetExtendedTcpTable(
            tcpTable: table,
            tableSize: ref size,
            sorted: false,
            addressFamily: AF_INET,
            tableClass: TCP_TABLE_OWNER_PID_LISTENER,
            reserved: 0);
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tableSize,
        bool sorted,
        int addressFamily,
        int tableClass,
        int reserved);

    /// <summary>
    /// Managed layout of the Win32 <c>MIB_TCPROW_OWNER_PID</c> row.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TcpOwnerProcessRow
    {
        public readonly uint State;
        public readonly uint LocalAddress;

        // Stored network-byte-order in the low two bytes, per MSDN.
        public readonly byte LocalPort1;
        public readonly byte LocalPort2;
        public readonly byte LocalPort3;
        public readonly byte LocalPort4;
        public readonly uint RemoteAddress;
        public readonly byte RemotePort1;
        public readonly byte RemotePort2;
        public readonly byte RemotePort3;
        public readonly byte RemotePort4;
        public readonly uint OwningProcessId;
    }
}
