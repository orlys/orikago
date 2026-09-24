namespace Orikago.LanguageService;

using System;
using System.Globalization;

/// <summary>
/// User-visible strings, selected by the IDE's UI culture.
/// </summary>
/// <remarks>
/// devenv sets the thread UI culture to the VS display language. Two languages only:
/// Traditional Chinese and an English fallback - a resx/satellite pipeline buys nothing at
/// this string count.
/// ponytail: table-of-two; switch to .resx satellites if a third language ever ships.
/// </remarks>
internal static class GoStrings
{
    public static string AddReferenceDialogTitle
    {
        get
        {
            return IsChinese ? "加入 Go 模組參考" : "Add Go Module Reference";
        }
    }

    public static string ModulePathLabel
    {
        get
        {
            return IsChinese
                ? "模組路徑（例如 github.com/google/uuid）："
                : "Module path (e.g. github.com/google/uuid):";
        }
    }

    public static string VersionLabel
    {
        get
        {
            return IsChinese
                ? "版本（例如 v1.6.0；留空表示最新版）："
                : "Version (e.g. v1.6.0; empty means latest):";
        }
    }

    public static string OkButton
    {
        get
        {
            return IsChinese ? "確定" : "OK";
        }
    }

    public static string CancelButton
    {
        get
        {
            return IsChinese ? "取消" : "Cancel";
        }
    }

    public static string InvalidModulePath
    {
        get
        {
            return IsChinese
                ? "請輸入合法的模組路徑（不可含空白或引號）。"
                : "Enter a valid module path (no whitespace or quotes).";
        }
    }

    public static string InvalidVersion
    {
        get
        {
            return IsChinese
                ? "版本不可含空白或引號。"
                : "The version must not contain whitespace or quotes.";
        }
    }

    public static string MessageBoxTitle
    {
        get
        {
            return "Orikago";
        }
    }

    public static string TidyRunning
    {
        get
        {
            return IsChinese ? "正在執行 go mod tidy…" : "Running go mod tidy...";
        }
    }

    public static string TidySucceeded
    {
        get
        {
            return IsChinese
                ? "go mod tidy 完成：go.mod／go.sum 已整理。"
                : "go mod tidy finished; go.mod/go.sum are tidy.";
        }
    }

    public static string GenerateRunning
    {
        get
        {
            return IsChinese ? "正在執行 go generate…" : "Running go generate...";
        }
    }

    public static string GenerateSucceeded
    {
        get
        {
            return IsChinese ? "go generate 完成。" : "go generate finished.";
        }
    }

    public static string VetRunning
    {
        get
        {
            return IsChinese ? "正在執行 go vet…" : "Running go vet...";
        }
    }

    public static string VetSucceeded
    {
        get
        {
            return IsChinese ? "go vet 完成：未發現問題。" : "go vet finished; no findings.";
        }
    }

    public static string GoCommandMissing
    {
        get
        {
            return IsChinese
                ? "找不到 go 可執行檔。請確認 Go 工具鏈已安裝且在 PATH 上。"
                : "The go command was not found. Make sure the Go toolchain is installed and on PATH.";
        }
    }

    public static string DlvMissing
    {
        get
        {
            // GoToolLocator's probe order, language-neutral; keep it in sync with that class
            const string PROBE_DESCRIPTION =
                "PATH / GOBIN / GOPATH\\bin (go env -w) / %USERPROFILE%\\go\\bin";

            return IsChinese
                ? "找不到 dlv.exe（delve 偵錯工具）。已探查 " + PROBE_DESCRIPTION +
                    "。請安裝：go install github.com/go-delve/delve/cmd/dlv@latest"
                : "dlv.exe (the delve debugger) was not found. Probed " +
                    PROBE_DESCRIPTION +
                    ". Install it with: go install github.com/go-delve/delve/cmd/dlv@latest";
        }
    }

    public static string DlvNotListening
    {
        get
        {
            return IsChinese
                ? "dlv dap 未在時限內於迴路位址上開始接聽任何連接埠。"
                : "dlv dap did not start listening on a loopback port in time.";
        }
    }

    private static bool IsChinese
    {
        get
        {
            var cultureName = CultureInfo.CurrentUICulture.Name;
            return cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string ReferenceAddedPinned(string module, string version)
    {
        return IsChinese
            ? $"已加入 Go 模組參考 {module}@{version}，將於下次建置時以 go get 解析。"
            : $"Added Go module reference {module}@{version}; it resolves via go get on the next build.";
    }

    public static string ReferenceAddedLatest(string module)
    {
        return IsChinese
            ? $"已加入 Go 模組參考 {module}（最新版），將於下次建置時以 go get 解析。"
            : $"Added Go module reference {module} (latest); it resolves via go get on the next build.";
    }

    public static string AddReferenceFailed(string message)
    {
        return IsChinese
            ? "無法加入 Go 模組參考：" + message
            : "Could not add the Go module reference: " + message;
    }

    public static string TidyFailed(string message)
    {
        return IsChinese
            ? "go mod tidy 失敗：" + message
            : "go mod tidy failed: " + message;
    }

    public static string GenerateFailed(string message)
    {
        return IsChinese
            ? "go generate 失敗：" + message
            : "go generate failed: " + message;
    }

    public static string VetFailed(string message)
    {
        return IsChinese
            ? "go vet 回報問題：" + message
            : "go vet reported findings: " + message;
    }

    public static string GoExecutableMissing(string path)
    {
        return IsChinese
            ? "找不到 Go 可執行檔：" + path + "。請先建置專案。"
            : "Go executable not found: " + path + ". Build the project first.";
    }

    public static string DlvExitedEarly(int exitCode)
    {
        var code = exitCode.ToString(CultureInfo.CurrentCulture);
        return IsChinese
            ? "dlv dap 啟動後立即結束（結束代碼 " + code + "）。"
            : "dlv dap exited immediately after starting (exit code " + code + ").";
    }
}
