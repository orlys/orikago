# 踩坑記錄：在 Visual Studio 上做 Go 開發平台

> Written in Traditional Chinese. English readers: this file is the project's engineering log of
> pitfalls — symptom, root cause, fix, and how each was diagnosed. Machine translation carries
> it reasonably well; the code, GUIDs, registry keys and command names in it are
> language-neutral.

這份文件記錄實際踩過的坑——**症狀、根因、解法、以及當初是怎麼查出來的**。目的不是描述現在的架構（那在 README），而是讓下一次不要重走同樣的死路。

規則：每一條都是真的踩過並解決（或明確卡住）的，不是理論上的注意事項。

---

## 一、VSIX 命令表與套件註冊

### 1. 命令永遠不出現，而且幾乎沒有錯誤訊息

一個右鍵命令從「寫完」到「真的出現在選單」，中間有**三道獨立關卡**，少任何一道都是靜默失敗：

| 關卡 | 症狀 | 解法 |
|---|---|---|
| vsct 沒嵌進組件 | ActivityLog 只有一行 `Error loading UI library ... 0x800a006f` | 必須有 `VSPackage.resx`，並以 `MergeWithCTO=true` + `ManifestResourceName=VSPackage` 的 `EmbeddedResource` 宣告；VSSDK 才會把編譯後的 `.cto` 併進 `VSPackage.resources` 的 `Menus.ctmenu` |
| 套件本身載不起來 | 完全無訊息；命令不在命令表中 | csproj 加 `<RegisterWithCodebase>true</RegisterWithCodebase>`。預設產生的 pkgdef 只寫組件顯示名稱（`PublicKeyToken=null`），非 GAC 的擴充組件無從解析。**注意：C# 屬性上的 `RegisterUsing = RegistrationMethod.CodeBase` 不會改變 CreatePkgDef 的輸出**，只有 csproj 屬性有效 |
| 選單合併結果被快取 | 修好了前兩項仍看不到命令 | 殼層依 `ProvideMenuResource("Menus.ctmenu", N)` 的**版本號**快取合併結果，同版本不重讀。改 vsct 後把 N +1；`install-vsix.ps1` 另外每次安裝都跑 `devenv /updateconfiguration` |

**驗證方式**（不要靠肉眼看選單）：
```powershell
# 解析得到就代表在命令表裡
$dte.Commands.Item('{命令集 GUID}', 命令 ID)
```
成功時會傳回像 `OtherContextMenus.GoDependencies.AddGoModuleReference` 這樣的完整名稱。

### 2. 內建命令無法逐項從共用選單移除

相依性節點的右鍵選單其實是殼層的 `IDM_VS_CTXT_REFERENCEROOT`（0x0450），「加入專案參考」「管理 NuGet
套件」都是**別人放在同一個共用選單上的放置項目**，無法一個個拿掉。

解法：用 `IProjectItemContextMenuProvider`（`AppliesTo("Orikago")` + 較高
`[Order]`）把該節點**整個換成自己的私有右鍵選單**——這正是受控專案系統自己的做法（`DependenciesContextMenuProvider`，它以較低的 Order
把樹節點映射到殼層選單）。子節點不處理就會自然落回預設的提供者。

### 3. VSCT 多語系

屬性名是**小寫** `language`，同一個 Button 下可以有多個 `<Strings>` 區塊：

```xml
<Strings><ButtonText>Add Go &amp;Module Reference...</ButtonText></Strings>
<Strings language="zh-TW"><ButtonText>加入 Go 模組參考(&amp;M)...</ButtonText></Strings>
```
無 `language` 的那組是後援，必須存在。程式碼側的字串則跟隨 `CultureInfo.CurrentUICulture`。

---

## 二、CPS / MSBuild SDK 專案能力

### 4. 方案總管一片空白，要按「顯示所有檔案」才看得到

**根因極度反直覺**：`Microsoft.NET.Sdk.DefaultItems.props` 先 `None Include="**/*"`，再
`None Remove="**/*$(DefaultLanguageSourceExtension)"` 想移除語言原始碼。非語言專案（`.goproj`）沒有語言的 props
檔，該屬性是**空字串**，於是那行 Remove 變成 `Remove="**/*"`——把剛建好的 None 清單整個抹掉，專案評估後**零個項目**。

解法：在巢狀匯入之後（抹除發生之後）以相同 Exclude 重跑一次 None glob。

驗證：`dotnet msbuild x.goproj -getItem:None` 應該回完整清單而非 `[]`。

### 5. 移除專案能力的連鎖後果

| 移除的專案能力 | 後果 | 對策 |
|---|---|---|
| `PackageReferences` | VS 不再對專案執行 NuGet 還原 → 每次建置 `NETSDK1004: Assets file ... not found` | 設 `SkipResolvePackageAssets=true`（官方開關）；Go 專案本來就不需要 `project.assets.json` |
| `LaunchProfiles` | **`Debug.Start` 命令整個消失**（F5 的底層設施就在那個子系統） | 不要移除。改用 `IDebugProfileLaunchTargetsProvider` 掛進它的管道 |
| `AssemblyReferences`／`COMReferences`／`WinRTReferences` | 相依性節點下的 .NET 參考子節點消失（想要的效果） | 安全。但 `ProjectReferences` 要保留，goproj 之間的專案參考是支援的 |

**教訓**：每移除一個專案能力，先在真實 VS 裡開一次專案並建置，再往下做。這裡曾因為連續改動而誤以為專案載入壞了。

### 6. F5 永遠走不到自訂的啟動提供者

有 `LaunchProfiles` 專案能力時，受控管道**只諮詢 `IDebugProfileLaunchTargetsProvider`**；純
`IDebugLaunchProvider` 匯出永遠不會被問到，結果就是「F5 有反應但跑的是預設引擎、中斷點不繫結」。

解法：同時實作並匯出兩者，`[Order]` 要蓋過內建的
`ProjectLaunchTargetsProvider`。所需組件（`Microsoft.VisualStudio.ProjectSystem.Managed.VS`）**NuGet
上沒有可用版本**（停在 2017 年的 2.x），要從 VS 安裝目錄直接參考（`Private=false`）。

---

## 三、delve / DAP 偵錯

### 7. `dlv dap` 只講 TCP

沒有 stdio 模式。所以 **不能**在引擎註冊裡寫 `"Adapter"` 讓 Debug Adapter Host 去啟動它（會在交握時卡死）。正確做法：自己啟動
`dlv dap --listen=127.0.0.1:<port>`，再用啟動設定的 `$debugServer` 把連接埠交給 DAH，DAH 會改為「連線」而非「啟動」。

### 8. 不要用 TCP 試連來偵測 dlv 就緒

`dlv dap` 只接受**單一**用戶端，試連會把工作階段吃掉。用 OS 的接聽程式表判斷：

```csharp
IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
```

另外：連接埠要**自己先挑**（繫結 :0 拿到後釋放），因為 dlv 必須在不重新導向 stdio 的情況下啟動，沒有管道可讀它印出的連接埠。

### 9. F5 沒有主控台視窗

`CreateNoWindow = true` + 重新導向管道會讓偵錯項目完全無 stdio，程式的 `fmt.Println` 只會被塞進輸出視窗、`fmt.Scan`
無從輸入。偵錯項目繼承 dlv 的主控台，所以 **F5 時 dlv 必須帶可見主控台啟動**（附加則相反，目標處理序已有自己的主控台）。

### 10. delve 硬拒較舊 Go 工具鏈建置的二進位

症狀：F5 跳出強制回應的錯誤對話方塊把 VS 卡住，訊息是
`Go version go1.21.5 is too old for this version of Delve (minimum supported version 1.25)`。

雙管齊下：
- dlv 啟動帶 `--check-go-version=false`（版本相符時無作用）
- 真正的解法是把工具鏈升上去：`go env -w GOTOOLCHAIN=go1.25.12+auto`（官方機制，不必重裝 Go）

### 11. GOTOOLCHAIN 切換不會觸發重建

切換工具鏈**不碰任何輸入檔**，MSBuild 判定最新 → 靜默沿用舊工具鏈建置的二進位。解法：把 `go version` 的輸出寫入
`go.build.args`，讓它參與最新性判斷。雙向切換都要驗證（切過去、切回來，各自都應重建）。

### 12. 升級工具鏈的連鎖反應

`golang.org/x/tools v0.24.0` 在 go1.25 下**無法編譯**（`tokeninternal: invalid array length`——go/token
內部布局改變）。升到 v0.48.0 解決。同理 gopls 0.14.2 與 1.25 不匹配，要一起升。

### 13. 每次中斷都跳 `unknown memoryReference` 錯誤

這是本次工作階段最難纏的一個，**表面症狀完全誤導**。

- 表面：看起來像記憶體視窗的問題
- 實際：VS 在**每一次中斷**都會用當下的**指令指標位址**送一個 `readMemory`（`count=0`）例行探查。協定記錄的因果鏈：`stopped` → `threads`
  → `stackTrace` ×2 → `readMemory(PC, count=0)` → 失敗 → `ERROR: Unexpected error`
- 根因：delve 的 `readMemory` **只接受它自己發出過的參考**（`referencesCollection.get`），而 `isAddressable()` 只對
  `reflect.Slice` 與 `reflect.String` 回 true。VS 自算的原始位址不在表中，必然被拒
- 無效的嘗試：引擎計量 `MemoryReferencesAreAddresses=0`（探查照送，實測）
- 解法：`DelveProxy`——在 DAH 與 dlv 之間插一層單連線 DAP 轉送層，雙向逐位元組轉發，**只**把「`readMemory` 失敗且訊息為 unknown
  memoryReference」改寫成 delve 自己對合法零長度讀取所回的成功空回應。真實讀取仍原封交給 delve
- 效果：同情境工作階段 ERROR `5 → 0`

**教訓**：使用者回報的錯誤訊息本身就是最好的診斷起點；而「我證明了 dlv 能讀字串」跟「VS 送的東西 dlv
收得到」是**兩件不同的事**，不要用前者推論後者（這個錯誤結論被使用者當場抓到）。

### 14. 設定下一個陳述式（拖移黃箭頭）不可能

缺口 100% 在 delve：`goto`／`gotoTargets` 在任何版本都回 unsupported，delve 連 CLI 都沒有 jump 命令。**就算 pkgdef 硬寫
`SetNextStatement=1` 也沒用**——DAH 會依 initialize 回應在執行期把計量覆寫回 0。唯一路徑是上游貢獻 delve。

### 15. 附加至處理序的 `0x8971001E`：缺的是 **ProgramProvider**

症狀：透過自訂引擎附加一律 `HRESULT 0x8971001E`，且**失敗發生在介面卡啟動器（adapter launcher）被呼叫之前**——dlv 從未啟動、ActivityLog
無記錄、DAH 協定零流量。對照組：同一處理序用 Native 引擎附加成功。

**根因**：附加流程需要 `IDebugProgramProvider2`
回答「這個處理序裡有沒有屬於你這個引擎的程式（program）」。沒註冊程式提供者，殼層找不到可附加的程式，在任何自訂程式碼執行前就放棄。

**曾經浪費時間的三個錯誤方向**（都與根因無關）：
1. `"AdapterLauncher"` 計量值 → 它只是已被取代的拼法，換成 `ExtensibilityObjects` 也一樣失敗
2. `ExtensibilityObjects` 編號子鍵 → 正確的現行拼法，但不是缺口
3. 把 `PortSupplier` 從單值改成**編號子鍵列表** → **這是走錯方向**：子鍵形狀屬於遠端／vsdbg 註冊，本機附加用的是**單值**

**正確做法**（全部照 JavaScript/TypeScript 偵錯介面卡抄——它是唯一做本機附加的內建 DAH 引擎）：

```
"Attach"=dword:00000001
"AutoSelectPriority"=dword:00000004
; 單值，殼層預設的本機連接埠供應者
"PortSupplier"="{708C1ECA-FF48-11D2-904F-00C04FA302A1}"
"ProgramProvider"="{你的提供者 CLSID}"
"AlwaysLoadProgramProviderLocal"=dword:00000001
```
再加上提供者的 COM CLSID
註冊（`Assembly`／`Class`／`InprocServer32`=mscoree／`CodeBase`／`ThreadingModel`=Free）。

實作只要兩個小類別：`IDebugProgramProvider2`（`GetProviderProcessData` 在
`PFLAG_GET_PROGRAM_NODES`（0x10）查詢時傳回一個程式節點，`Fields` 設 `PFIELD_PROGRAM_NODES`（0x1），其餘方法傳回
`E_FAIL`／`S_OK`）與 `IDebugProgramNode2`（只有 `GetEngineInfo` 傳回引擎 GUID、`GetHostPid`
傳回 PID 有意義，`_V7` 系列全部 `E_FAIL`）。

**找到答案的方法**：先在所有 pkgdef 中搜尋 DAH 的固定 CLSID `{DAB324E9-...}` 列出全部 DAH 引擎，比對誰有 `Attach=1`，發現 JS 的
`{3FBCC828}` 是唯一本機附加的例子，其註解直接寫著 `Debug attach and program provider`；再用
`ilspycmd -t ...AD7JSTSProgramProvider` 反向組譯拿到精確契約。**「找一個做同樣事情的內建元件並反向組譯它」比猜計量有效得多。**

附帶：讓提供者只對真正的 Go 處理序回報程式節點，做法是掃描目標執行檔裡 Go 連結器寫入的 build-info
魔術字串（`\xff Go buildinf:`），否則程式碼類型清單會對每個無關處理序都出現 Go 偵錯工具。

---

### 15b. NuGet 判斷專案「支援與否」的實際運算式

想讓 NuGet 的 UI 對自訂專案類型消失時，不必猜。`NuGet.VisualStudio.Common.dll` 的
`VsHierarchyUtility`（反向組譯可得）是唯一權威，其 `IsSupported(projectKind, hierarchy)` 的判斷如下：

```csharp
if (IsProjectCapabilityCompliant(hierarchy))
{
    // 專案能力齊全就直接視為支援
    return true;
}

if (projectKind is not null && ProjectType.IsSupported(projectKind))
{
    // 已知的專案類型 GUID，再排除共用資產專案
    return !HasUnsupportedProjectCapability(hierarchy);
}

return false;

// 運算式中的 + 是 AND
IsProjectCapabilityCompliant = IsCapabilityMatch(
    "(AssemblyReferences + DeclaredSourceItems + UserSourceItems) | PackageReferences");
HasUnsupportedProjectCapability = IsCapabilityMatch("SharedAssetsProject");
```

所以要讓 NuGet 認定「不支援」，必須同時**沒有** `PackageReferences`，而且
`AssemblyReferences`／`DeclaredSourceItems`／`UserSourceItems` 三者不齊（移除 `AssemblyReferences`
最無害——Go 專案本來就不參考 .NET 組件）。第二條分支對自訂專案類型 GUID 天然不成立。

驗證專案能力用 MSBuild 而非肉眼：
```powershell
msbuild x.goproj -getItem:ProjectCapability -restore
```

**但專案能力正確也不會讓「管理 NuGet 套件」從選單消失。** 反向組譯 `NuGetPackage.BeforeQueryStatusForAddPackageDialog`
可以看到原因：

```csharp
// 只要方案開著就顯示
val.Visible = GetIsSolutionOpen();

// 專案能力只影響這裡
val2.Enabled = IsSolutionExistsAndNotDebuggingAndNotBuilding() &&
    await HasActiveLoadedSupportedProjectAsync();
```

`Visible` 與專案型別完全無關，所以專案能力做對的結果是「命令仍在，點下去回報 *The project ... is unsupported*」。

**換掉專案節點選單的做法行不通**（試過並確認失敗）：相依性節點可以用 `IProjectItemContextMenuProvider`
改掛私有選單，但**專案根節點的選單不走這個擴充點**——即使提供者對 `ProjectTreeFlags.Common.ProjectRoot`
傳回私有選單，右鍵出來的仍是殼層的共用選單（NuGet 與 Manage User Secrets 都還在）。連帶地，為此準備的 `<CommandPlacements>` 重掛 11
個標準群組也白做了。

**正解是 CPS 的命令狀態覆寫擴充點**——不動選單，直接把命令標成不可見：

```csharp
// NuGet 的 guidNuGetDialogCmdSet
[ExportCommandGroup("25fd982b-8cae-4cbd-a440-e03ffccde106")]
[AppliesTo("Orikago")]
[Order(1000)]
internal sealed class GoHiddenNuGetCommandsHandler : IAsyncCommandGroupHandler
{
    public Task<CommandStatusResult> GetCommandStatusAsync(
        IImmutableSet<IProjectTree> items,
        long commandId,
        bool focused,
        string commandText,
        CommandStatus progressiveStatus)
    {
        // cmdidAddPackageDialog 與 cmdidAddPackageDialogForSolution
        var isAddPackageDialog = commandId is 0x100 or 0x200;
        if (isAddPackageDialog)
        {
            // 管理 NuGet 套件的命令：沿用既有狀態並加上不可見旗標
            return Task.FromResult(new CommandStatusResult(
                handled: true,
                commandText,
                status: progressiveStatus | CommandStatus.Invisible));
        }

        return CommandStatusResult.Unhandled.AsTask();
    }
    ...
}
```

`AppliesTo` 讓它只作用在 Go 專案，其他專案型別的 NuGet 完全不受影響。實測結果：「管理 NuGet
套件」從專案右鍵選單消失，其餘命令（建置／發行／加入／偵錯／卸載／屬性…）一個不少。**這個擴充點可以隱藏任何別人放上去的命令，只要知道它的命令集 GUID 與
ID（反向組譯對方的套件即可取得）。**

### 15b-2. 盤點並隱藏不適用的內建命令（通用配方）

不必逐一反向組譯各家套件找命令集 GUID/ID——**用 DTE 列舉即可**：

```powershell
foreach ($command in $dte.Commands) { "$($command.Name) | $($command.Guid) | $($command.ID)" }
```

名稱是 `情境.位置.命令` 格式（如
`ProjectandSolutionContextMenus.Project.ManageUserSecrets`），用關鍵字過濾就能一次拿到所有目標。實測取得並隱藏的項目：

| 命令 | 命令集 | ID |
|---|---|---|
| 管理 NuGet 套件 | `{25FD982B-8CAE-4CBD-A440-E03FFCCDE106}` | 0x100 / 0x200 |
| Pack | `{568ABDF7-D522-474D-9EED-34B5E5095BA5}` | 8192 / 8193 |
| Publish… | `{1496A755-94DE-11D0-8C3F-00C04FC2AAE2}` | 2005 / 2006 |
| Modernize | `{31760A92-B75C-472D-B977-7CAEAB0AF122}` | 1280 / 1296 |
| 程式碼清除 | `{160961B3-909D-4B28-9353-A1BEF587B4A6}` | 全組 |
| 管理使用者祕密 | `{9C5B3619-FD0B-467C-B06D-FBEB1496FB1A}` | 1792 |
| 加入 → 連線服務 | `{A114CF9C-BD45-4A48-92EF-D9BBBC0B3DF0}` | 17 / 19 |

**子選單容器要整組隱藏**：程式碼清除是個子選單，只隱藏已知的幾個 ID 會留下空的容器；把該命令集的所有 ID 都回報
Invisible（該命令集專屬於這個功能，不會誤傷）之後容器才一併消失。

### 15c. 主選單（Tools）的命令：只能「停用」，不能「隱藏」

CPS 的命令群組處理器只參與**專案右鍵選單**的命令路由，主選單命令根本不會經過它。要攔截主選單命令，唯一的鉤子是 `IVsRegisterPriorityCommandTarget`
註冊的優先權命令目標（它看得到每一個命令的 QueryStatus），搭配 `ProvideAutoLoad` 讓套件在 UIContext
成立時就載入（等到命令被叫用才載入已經太遲——選單早就畫好了）。

**但結果只能是灰色**：`OLECMDF_INVISIBLE` 只有在**該命令自己的 .vsct 定義帶 `DynamicVisibility` 旗標**時才會被採納，而 NuGet
的命令沒有。於是殼層照樣繪製該項目，只有「缺少 `OLECMDF_ENABLED`」這件事生效 → 呈現為停用。

實測：工具 → NuGet 套件管理員底下的「套件管理器主控台」與「管理方案的 NuGet
套件」變灰，子選單容器與「套件管理器設定」仍在。對於**不屬於自己的命令**，灰化就是天花板；真要讓項目消失，只剩「停用整個 NuGet 擴充」這種影響全 IDE 的手段。用
`IVsMonitorSelection.IsCmdUIContextActive` 繫結 UIContext，可確保切換到 C# 專案時 NuGet 立刻恢復正常。

## 四、工具與環境

### 16. VSIXInstaller 結束代碼 2004（BlockingProcesses）

不是只有 `devenv` 會擋。實際擋過的處理序：
- `copilot-language-server.exe`（**最常見且最意外**，VS 關掉後仍殘留）
- `MSBuild.exe`、`VBCSCompiler.exe`、`PerfWatson2.exe`

診斷：`%TEMP%\dd_VSIXInstaller_*.log` 會逐行列出 `Blocking processes:`。

另外：`VSIXInstaller /quiet` 在**版本號相同**時會靜默地什麼都不做（回報成功但沒換 DLL）——所以安裝腳本一律「先解除安裝再安裝」，並以檔案雜湊驗證部署結果。

### 17. gopls / dlv 執行檔換不掉

被執行中的 VS 鎖住。要先關閉 VS **並**終止殘留的 `gopls.exe` 處理序。還要注意 **PATH 順序**決定哪一份生效——本機 `D:\Go\bin` 排在
`%USERPROFILE%\go\bin` 之前，所以 `go install` 裝到後者不會生效，必須覆蓋前者。

### 18. DTE 自動化的陷阱

- COM 呼叫會**間歇性**
  `RPC_E_CALL_REJECTED (0x80010001)`，任何一次呼叫都要包重試迴圈（這曾造成「中斷點沒被叫用」的誤判——其實有叫用，是輪詢讀取失敗）
- 直接開 `.goproj`（非 `.sln`）時，`Solution.Projects` 可能回報空／無名專案，那是**假的**「載入失敗」；用 `Build.BuildSolution`
  加建置成品是否更新來實證
- `$args` 是 PowerShell 保留變數，拿來當參數名會炸
- UI Automation 找 VS 的子視窗常常撲空；`#32770` 對話方塊可以用 Win32 `EnumChildWindows` + `WM_GETTEXT`
  讀出內容（這招救回過一次被強制回應對話方塊卡死的工作階段，直接讀出了錯誤訊息）

### 19. 驗證方法論（最重要的一條）

**寫一個直接的 DAP 用戶端去問 delve，比操作 VS UI 可靠一個數量級。** 本次工作階段中 VS 自動化反覆失敗（COM 拒接、視窗找不到、狀態誤判），而一支 200 行的
PowerShell DAP 用戶端一次就給出確定答案：哪些變數有 `memoryReference`、`readMemory` 對原始位址傳回什麼、`disassemble`
是否真的傳回組合語言。

其次：**Debug Adapter Host 協定記錄是裁判**。在 VS 命令視窗或以 `DTE.ExecuteCommand` 執行：
```
DebugAdapterHost.Logging /On:<檔案路徑>
```
它會逐筆記錄 DAP 往返，能直接看出「VS 送了什麼」「delve 回了什麼」。注意這個檔案是**累加**的，分析時要先用 `DebugAdapterHost version:`
這行切出最新的工作階段，否則會把舊工作階段的警告當成新問題（這個坑踩過）。

### 20. 循序讀 stdout/stderr = 死結（而且註解會騙人）

```csharp
// 卡在這裡
var output = process.StandardOutput.ReadToEnd();

// 永遠到不了
var error = process.StandardError.ReadToEnd();
```

兩條管道都重新導向時**必須同時排空**。子處理序往 stderr 寫滿管道緩衝區（Windows 預設 4KB）後就阻塞，而讀取端還在等 stdout 關閉——雙方互等，誰也醒不過來。

Go 工具鏈特別容易踩：進度訊息全走 stderr。`go mod tidy` 下載模組時印的 `go: downloading ...`、`GOTOOLCHAIN=...+auto` 觸發的
`go: downloading go1.x`，都是 stdout 幾乎沒有東西、stderr 一直噴的形狀。

實測（`cmd /c for /L ... 1>&2`，約 100KB stderr）：循序寫法
6 秒不傳回、子處理序要強制終止；`ReadToEndAsync()` 兩條同時等，335ms 完成。

兩個附帶教訓：

- **`WaitForExit(timeout)` 排在 `ReadToEnd()` 之後等於沒有逾時**——執行不到那一行。`GoToolLocator.RunGoEnv`
  原本就是這個形狀，5 秒上限形同虛設。
- **註解寫了不代表程式碼做了。** 原本的 `OrikagoPackage.RunGoCommand` 上面明明白白寫著「read both so a full pipe can
  never block the process」，底下卻是循序的兩行。審查時要看程式碼，別看註解。

順帶：這段原本整個跑在 UI 執行緒上，`go mod tidy` 幾十秒就是 IDE 凍幾十秒。現在只有 DTE 與輸出窗格的呼叫留在主執行緒，處理序等待走
`await TaskScheduler.Default`。

### 21. 「有人在接聽這個連接埠」不等於「是我啟動的那個處理序在接聽」

原本的做法：自己繫結 `:0` 拿一個空的連接埠、關掉、把連接埠號碼傳給 `dlv --listen=127.0.0.1:<port>`，然後輪詢
`IPGlobalProperties.GetActiveTcpListeners()` 等它出現。

漏洞在於釋放與 dlv 繫結之間的空窗。若這時別的處理序搶走該連接埠，輪詢會看到「有人在接聽」→ 判定就緒 → `DelveProxy`
把整個偵錯工作階段轉給那個不相干的服務。程式碼裡原本的註解說「dlv 會快速檢錯，輪詢會報出來」——**不成立**，因為迴圈先看到在接聽就 `break` 了，dlv 那時還沒退出。

`GetActiveTcpListeners()` 不給擁有者 PID，是這個 API 的天花板。正解是
`GetExtendedTcpTable`（`iphlpapi.dll`，`TCP_TABLE_OWNER_PID_LISTENER`），它每列帶 `OwningPid`：讓 dlv 自己繫結
`:0`，再用它的 PID 反查拿到連接埠。空窗直接消失，而不是被縮小。見 `TcpListenerTable.cs`。

P/Invoke 兩個容易靜默寫錯的地方，務必實測（開一個接聽程式反查自己的 PID 就能驗）：
- `MIB_TCPROW_OWNER_PID.LocalPort` 是**以網路位元組順序存在低兩個位元組**，要 `(b1 << 8) | b2`，不是直接讀 `uint`
- `LocalAddr` 也是網路位元組順序，比對回送位址要 `IPAddress.HostToNetworkOrder(0x7F000001)`