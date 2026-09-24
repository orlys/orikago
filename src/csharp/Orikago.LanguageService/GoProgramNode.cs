namespace Orikago.LanguageService;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

using System;

/// <summary>
/// The single program <see cref="GoProgramProvider"/> reports for a Go process.
/// </summary>
/// <remarks>
/// Only the engine identity and the host PID are meaningful; the _V7 members are legacy and
/// stay unimplemented, as in the in-box providers.
/// </remarks>
internal sealed class GoProgramNode : IDebugProgramNode2
{
    private readonly int _processId;
    private readonly Guid _engineGuid;
    private readonly string _engineName;

    public GoProgramNode(int processId, Guid engineGuid, string engineName)
    {
        _processId = processId;
        _engineGuid = engineGuid;
        _engineName = engineName;
    }

    int IDebugProgramNode2.GetEngineInfo(out string engineName, out Guid engineGuid)
    {
        engineName = _engineName;
        engineGuid = _engineGuid;
        return VSConstants.S_OK;
    }

    int IDebugProgramNode2.GetHostPid(AD_PROCESS_ID[] hostProcessId)
    {
        if (hostProcessId is not { Length: > 0 })
        {
            // The debugger passed no slot to fill: a contract violation on the caller's side
            ExtensionLog.Error("GetHostPid was called without a result slot.");
            return VSConstants.E_FAIL;
        }

        // AD_PROCESS_ID_SYSTEM: a plain operating-system process ID
        hostProcessId[0].ProcessIdType = 0u;
        hostProcessId[0].dwProcessId = (uint)_processId;
        return VSConstants.S_OK;
    }

    int IDebugProgramNode2.GetHostName(
        enum_GETHOSTNAME_TYPE hostNameType,
        out string? processName)
    {
        processName = null;
        return VSConstants.E_FAIL;
    }

    int IDebugProgramNode2.GetProgramName(out string? programName)
    {
        programName = null;
        return VSConstants.E_FAIL;
    }

    int IDebugProgramNode2.Attach_V7(
        IDebugProgram2 machineDebugManagerProgram,
        IDebugEventCallback2 callback,
        uint reason)
    {
        return VSConstants.E_FAIL;
    }

    int IDebugProgramNode2.DetachDebugger_V7()
    {
        return VSConstants.E_FAIL;
    }

    int IDebugProgramNode2.GetHostMachineName_V7(out string? hostMachineName)
    {
        hostMachineName = null;
        return VSConstants.E_FAIL;
    }
}
