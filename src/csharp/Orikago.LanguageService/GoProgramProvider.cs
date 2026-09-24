namespace Orikago.LanguageService;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

using Orikago.LanguageService.Definitions;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Reports "this process contains a program my engine can debug" to the attach pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Registered as the engine's <c>ProgramProvider</c> in goproj.pkgdef.
/// </para>
/// <para>
/// This is the piece whose absence made every attach fail with
/// HRESULT <c>0x8971001E</c> long before the adapter launcher was ever consulted:
/// with no program provider, the shell finds no program belonging to this
/// engine inside the target process and gives up. Modelled on the
/// JavaScript/TypeScript debug adapter's provider, which is the one
/// in-box example of a Debug Adapter Host engine doing LOCAL attach.
/// </para>
/// </remarks>
[ComVisible(true)]
[Guid(ClsidString)]
public sealed class GoProgramProvider : IDebugProgramProvider2
{
    /// <summary>
    /// The COM class ID of this provider.
    /// </summary>
    /// <remarks>
    /// Must match both the engine's <c>ProgramProvider</c> value and the <c>CLSID</c>
    /// registration of this class in goproj.pkgdef.
    /// </remarks>
    public const string ClsidString = "3E8C1A47-9D26-4B85-BF03-71A5E9C4D682";

    int IDebugProgramProvider2.GetProviderProcessData(
        enum_PROVIDER_FLAGS flags,
        IDebugDefaultPort2 port,
        AD_PROCESS_ID processId,
        CONST_GUID_ARRAY engineFilter,
        PROVIDER_PROCESS_DATA[] processArray)
    {
        // Values of enum_PROVIDER_FLAGS.PFLAG_GET_PROGRAM_NODES and
        // enum_PROVIDER_FIELDS.PFIELD_PROGRAM_NODES
        const uint PFLAG_GET_PROGRAM_NODES = 0x10;
        const uint PFIELD_PROGRAM_NODES = 0x1;

        if (processArray is not { Length: > 0 })
        {
            // The shell passed no slot to fill: a contract violation on the caller's side
            ExtensionLog.Error("GetProviderProcessData was called without a result slot.");
            return VSConstants.E_FAIL;
        }

        processArray[0] = default;

        if (((uint)flags & PFLAG_GET_PROGRAM_NODES) == 0)
        {
            // Not a program-node query: nothing to contribute
            return VSConstants.S_FALSE;
        }

        var pid = (int)processId.dwProcessId;
        if ((TryGetProcessImagePath(pid) is not { } executablePath) ||
            !IsGoBinary(executablePath))
        {
            // The image cannot be read or is not a Go binary: no Go program lives here
            return VSConstants.S_FALSE;
        }

        // Hand the shell one program node for this process, in a CoTaskMem array it frees
        var node = (IDebugProgramNode2)new GoProgramNode(
            processId: pid,
            engineGuid: new Guid(DelveEngine.GuidString),
            engineName: DelveEngine.Name);
        IntPtr[] nodes = [Marshal.GetComInterfaceForObject(node, typeof(IDebugProgramNode2))];
        var members = Marshal.AllocCoTaskMem(IntPtr.Size * nodes.Length);
        Marshal.Copy(nodes, 0, members, nodes.Length);

        processArray[0].Fields = (enum_PROVIDER_FIELDS)PFIELD_PROGRAM_NODES;
        processArray[0].ProgramNodes.Members = members;
        processArray[0].ProgramNodes.dwCount = (uint)nodes.Length;
        return VSConstants.S_OK;
    }

    int IDebugProgramProvider2.GetProviderProgramNode(
        enum_PROVIDER_FLAGS flags,
        IDebugDefaultPort2 port,
        AD_PROCESS_ID processId,
        ref Guid engineGuid,
        ulong programId,
        out IDebugProgramNode2? programNode)
    {
        programNode = null;
        return VSConstants.E_FAIL;
    }

    int IDebugProgramProvider2.WatchForProviderEvents(
        enum_PROVIDER_FLAGS flags,
        IDebugDefaultPort2 port,
        AD_PROCESS_ID processId,
        CONST_GUID_ARRAY engineFilter,
        ref Guid launchingEngineGuid,
        IDebugPortNotify2 eventCallback)
    {
        return VSConstants.S_OK;
    }

    int IDebugProgramProvider2.SetLocale(ushort languageId)
    {
        return VSConstants.S_OK;
    }

    /// <summary>
    /// A Go binary carries the build info magic that <c>go version &lt;exe&gt;</c> reads.
    /// </summary>
    /// <remarks>
    /// Matching on it keeps "Go Debugger (Delve)" out of the code-type list for every
    /// unrelated process in the attach dialog.
    /// ponytail: scans the file once per query; the attach dialog asks for a handful of
    /// processes at a time.
    /// </remarks>
    private static bool IsGoBinary(string executablePath)
    {
        // "\xff Go buildinf:" - the header the Go linker writes into every
        // binary (runtime/debug.ReadBuildInfo / cmd/go's version command).
        byte[] magic = [0xFF, .. Encoding.ASCII.GetBytes(" Go buildinf:")];
        try
        {
            using var stream = new FileStream(
                path: executablePath,
                mode: FileMode.Open,
                access: FileAccess.Read,
                share: FileShare.ReadWrite | FileShare.Delete);

            // Scan the file window by window for the magic
            var window = new byte[64 * 1024];
            var carry = magic.Length - 1;
            var offset = default(int);
            while (true)
            {
                var read = stream.Read(window, offset, window.Length - offset);
                if (read <= 0)
                {
                    // End of file without the magic: not a Go binary
                    return false;
                }

                var available = offset + read;
                for (var i = 0; (i + magic.Length) <= available; i++)
                {
                    var hit = true;
                    for (var j = 0; j < magic.Length; j++)
                    {
                        if (window[i + j] != magic[j])
                        {
                            hit = false;
                            break;
                        }
                    }

                    if (hit)
                    {
                        return true;
                    }
                }

                // Keep the tail so a match spanning two windows is not missed.
                Buffer.BlockCopy(window, available - carry, window, 0, carry);
                offset = carry;
            }
        }
        catch (Exception)
        {
            // The image cannot be read (locked, access denied, vanished): treat it as not Go.
            // The attach dialog queries every process, so this is routine, not a failure.
            return false;
        }
    }

    private static string? TryGetProcessImagePath(int pid)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        var handle = NativeMethods.OpenProcess(
            desiredAccess: PROCESS_QUERY_LIMITED_INFORMATION,
            inheritHandle: false,
            processId: (uint)pid);
        if (handle == IntPtr.Zero)
        {
            // Protected, elevated or already gone: routine for the attach dialog's process list
            return null;
        }

        try
        {
            var builder = new StringBuilder(4096);
            var size = builder.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, builder, ref size)
                ? builder.ToString(0, size)
                : null;
        }
        catch (Exception)
        {
            // The image name cannot be queried: treat the process as unreadable
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageName(
            IntPtr process,
            uint flags,
            StringBuilder executableName,
            ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
