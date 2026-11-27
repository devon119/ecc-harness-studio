# SPEC_20261126_ECCSELECTION_MCP — 本機 `.NET 10` Stdio MCP Server 規格書

- **文件編號**：`SPEC_20261126_ECCSELECTION_MCP`
- **建立日期**：2026-11-26
- **撰寫角色**：SA / 規格 Agent
- **目標專案**：`ECCSELECTION`（`ECCSELECTION.slnx`）
- **狀態**：待人類確認（Human-in-the-Loop Gate）

---

## 1. 背景與目標

ECCSELECTION 日常開發與發佈流程包含多項重複性高且具風險的固定作業（例如：執行品質閘門前需停用佔用檔案鎖的 `ECCSELECTION.Web.exe`、從 `main` 打包並透過 `git worktree` 推送至 `gh-pages`、從本機 SQLite 匯出 `catalog.json`、依規範建立獨立規格檔、檢查前端與控制器靜態契約）。

本規格旨在於專案中新增一套**本機 `.NET 10` Stdio MCP Server**（`tools/ECCSELECTION.Mcp/`），將上述 5 項固定作業封裝為標準化、可測試且具備防呆與安全邊界檢查的 MCP Tools，供 Antigravity Agent 透過 `mcp_config.json` 直接呼叫。

---

## 2. 專案結構與方案整合

### 2.1 目錄與檔案配置

```text
ECCSELECTION.slnx
├── src/
│   └── ECCSELECTION.Web/
├── tests/
│   └── ECCSELECTION.Tests/
│       └── Mcp/                                # 新增：MCP 單元測試目錄
│           ├── WorkspaceGuardTests.cs
│           ├── QualityGateServiceTests.cs
│           ├── PagesSyncServiceTests.cs
│           ├── CatalogExportServiceTests.cs
│           ├── SpecScaffolderServiceTests.cs
│           └── UiContractCheckerServiceTests.cs
└── tools/
    └── ECCSELECTION.Mcp/                       # 新增：.NET 10 Stdio MCP Server
        ├── ECCSELECTION.Mcp.csproj
        ├── Program.cs
        ├── Options/
        │   └── McpWorkspaceOptions.cs
        ├── Security/
        │   ├── WorkspaceGuard.cs
        │   ├── IProcessRunner.cs
        │   └── ProcessRunner.cs
        ├── Services/
        │   ├── QualityGateService.cs
        │   ├── PagesSyncService.cs
        │   ├── CatalogExportService.cs
        │   ├── SpecScaffolderService.cs
        │   └── UiContractCheckerService.cs
        └── Tools/
            └── EccSelectionTools.cs
```

### 2.2 `ECCSELECTION.slnx` 整合

於 `ECCSELECTION.slnx` 新增 `/tools/` 資料夾節點：

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/ECCSELECTION.Web/ECCSELECTION.Web.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ECCSELECTION.Tests/ECCSELECTION.Tests.csproj" />
  </Folder>
  <Folder Name="/tools/">
    <Project Path="tools/ECCSELECTION.Mcp/ECCSELECTION.Mcp.csproj" />
  </Folder>
</Solution>
```

### 2.3 技術規格與品質閘門相容性

- **TargetFramework**：`net10.0`（`<OutputType>Exe</OutputType>`、`<Nullable>enable</Nullable>`、`<ImplicitUsings>enable</ImplicitUsings>`）。
- **建議 NuGet 套件**：
  - `ModelContextProtocol`（官方 .NET MCP SDK，支援 `[McpServerToolType]` 與 `[McpServerTool]`）
  - `Microsoft.Extensions.Hosting`（10.0.*）
  - `Microsoft.Data.Sqlite`（10.0.*，用於唯讀查詢本機 SQLite，避免耦合 `ECCSELECTION.Web` 的 WebHost/EF DI）
- **品質閘門相容性**：
  1. `dotnet build ECCSELECTION.slnx -warnaserror`：所有公開型別/方法與 XML 註解須 0 警告、0 錯誤。
  2. `dotnet format ECCSELECTION.slnx --verify-no-changes --no-restore`：遵循專案 `.editorconfig` 與程式碼風格。
  3. `dotnet test ECCSELECTION.slnx --no-build --verbosity normal --collect:"XPlat Code Coverage"`：`ECCSELECTION.Tests` 新增對 `ECCSELECTION.Mcp` 之 `ProjectReference` 並涵蓋核心服務測試。
  4. `npx --yes ecc-agentshield scan --path . --format text`：無硬編碼密鑰、無不安全 shell 執行、無空 `catch` 區塊。
- **Stdio 通道規範**：MCP 透過 `stdout` 傳輸 JSON-RPC 2.0 訊息；所有 `ILogger` 日誌必須強制導向 `stderr`（`LogToStandardErrorThreshold = LogLevel.Trace`），嚴禁在執行期間呼叫 `Console.WriteLine`。

---

## 3. 5 個 MCP Tools 詳細規格

所有 Tool 的錯誤處理均須回傳結構化 JSON（含 `success` / `passed` 狀態與 `errorMessage`），**嚴禁空 `catch` 區塊**，亦不得讓未處理例外導致 MCP Server 進程崩潰。

---

### 3.1 `run_quality_gate`

- **用途**：安全停用佔用 `ECCSELECTION.Web.exe` 的背景進程（避免 `MSB3021`/`MSB3027` 檔案鎖定）後，執行 `scripts/quality-gate.ps1` 並回傳 4 項檢查的結構化結果。
- **輸入參數 Schema**：
  | 參數名稱 | 型別 | 必填 | 預設值 | 驗證規則 |
  | :--- | :--- | :---: | :--- | :--- |
  | `stopRunningWebProcess` | `boolean` | 否 | `true` | 是否於執行前終止 `ECCSELECTION.Web` 進程 |
  | `timeoutSeconds` | `integer` | 否 | `300` | 允許範圍 `30..900`（秒） |

- **執行邏輯**：
  1. 若 `stopRunningWebProcess == true`：
     - 查詢進程名稱為 `ECCSELECTION.Web`（不含副檔名）的進程並呼叫 `Kill(entireProcessTree: true)` 與 `WaitForExit(5000)`，記錄已停止之 PID 清單。
     - 確認無鎖定後繼續（若無執行中進程則 `stoppedPids` 為空陣列，視為正常）。
  2. 透過 `IProcessRunner`（使用 `ArgumentList`，禁止 shell 字串拼接）執行：
     - 優先尋找 `pwsh`，若無則使用 `powershell`
     - 參數：`["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "<repoRoot>/scripts/quality-gate.ps1"]`
     - 工作目錄：`repoRoot`
  3. 解析輸出與 `ExitCode`，對應 4 個固定階段標記：
     - `[1/4]` → `build`
     - `[2/4]` → `format`
     - `[3/4]` → `test`
     - `[4/4]` → `agentshield`
     - 若前階段失敗導致腳本中斷，後續未執行階段標記為 `"skipped"`。
- **結構化回傳格式**：
  ```json
  {
    "passed": true,
    "timedOut": false,
    "exitCode": 0,
    "stoppedPids": [12345],
    "totalDurationMs": 18420,
    "checks": {
      "build": { "status": "passed", "summary": "0 warnings, 0 errors" },
      "format": { "status": "passed", "summary": "No formatting changes required" },
      "test": { "status": "passed", "summary": "Passed: 42, Failed: 0" },
      "agentshield": { "status": "passed", "summary": "No critical findings" }
    },
    "rawOutputTail": "..."
  }
  ```

---

### 3.2 `sync_and_publish_lite`

- **用途**：透過 `git worktree` + `git archive` 從 `main` 打包最新 `eccselection-full.zip`、同步 `icon3d.css`（及可選 `catalog.json`）至 `gh-pages` 並安全推送，從工具層徹底防堵誤蓋 `main`。
- **輸入參數 Schema**：
  | 參數名稱 | 型別 | 必填 | 預設值 | 驗證規則 |
  | :--- | :--- | :---: | :--- | :--- |
  | `dryRun` | `boolean` | 否 | `true` | **預設為 `true`（安全乾跑）**，僅傳入 `false` 才執行 `commit` 與 `push` |
  | `includeCatalogJson` | `boolean` | 否 | `false` | 是否同時從本機 SQLite 匯出最新 `data/catalog.json` 並同步 |
  | `commitMessage` | `string` | 否 | `"chore(gh-pages): sync full package and icon3d.css from main"` | 長度 `5..200`，禁止換行符號與控制字元 |

- **執行邏輯與防呆機制**：
  1. **分支與乾淨度檢查**：驗證本地存在 `main` 分支及 `gh-pages`（或 `origin/gh-pages`）參照，取得 `main` 最新 Commit SHA（`git rev-parse main`）。
  2. **安全打包 `eccselection-full.zip`**：
     - 於暫存目錄執行 `git archive --format=zip --output=<stagingRawZip> main`（僅打包已提交至 `main` 的追蹤檔案，天然排除本機 `eccselection.db`、`appsettings.Development.local.json`、`bin/`、`obj/`、`TestResults/`）。
     - 若需過濾 `docs/` 等非完整版內容，解壓至暫存 staging 資料夾並保留 `ECCSELECTION.slnx`、`AGENTS.md`、`SPEC_ECCSELECTION.md`、`DEVELOPMENT_STATUS.md`、`README.md`、`src/`、`tests/`、`scripts/`、`.ecc/`、`.agents/`（與 `scripts/build-pages-bundle.ps1` 一致）後重新壓縮為 `eccselection-full.zip`。
  3. **隔離 `git worktree`**：
     - 於 `%TEMP%/eccselection-ghpages-<guid>` 建立 worktree：`git worktree add <tempWorktree> gh-pages`。
     - **嚴禁**在主工作區執行 `git checkout gh-pages`。
  4. **檔案同步**：
     - 將 `eccselection-full.zip` 寫入 `<tempWorktree>/downloads/eccselection-full.zip`。
     - 透過 `git show main:src/ECCSELECTION.Web/wwwroot/css/icon3d.css` 取得最新樣式並寫入 `<tempWorktree>/css/icon3d.css`。
     - 若 `includeCatalogJson == true`，呼叫 `CatalogExportService` 寫入 `<tempWorktree>/data/catalog.json`。
  5. **雙重分支鎖定（防誤蓋 `main`）**：
     - 執行 `git -C <tempWorktree> rev-parse --abbrev-ref HEAD`，若回傳值不等於 `"gh-pages"`，立即中止並清理 worktree。
     - 若 `dryRun == false` 且 `git -C <tempWorktree> status --porcelain` 有變更：
       - `git -C <tempWorktree> add downloads/eccselection-full.zip css/icon3d.css [data/catalog.json]`
       - `git -C <tempWorktree> commit -m <commitMessage>`
       - `git -C <tempWorktree> push origin gh-pages:gh-pages`（寫死 refspec，禁止使用 `HEAD` 或 `main`）。
  6. **`finally` 強制清理**：無論成功或失敗，必執行 `git worktree remove --force <tempWorktree>` 並刪除暫存檔。
- **結構化回傳格式**：
  ```json
  {
    "success": true,
    "dryRun": true,
    "targetBranch": "gh-pages",
    "mainCommitSha": "abc1234...",
    "zipSizeBytes": 245760,
    "updatedFiles": [
      "downloads/eccselection-full.zip",
      "css/icon3d.css"
    ],
    "pushed": false,
    "ghPagesCommitSha": null,
    "warnings": [],
    "message": "Dry-run completed. Pass dryRun=false to commit and push to origin/gh-pages."
  }
  ```

---

### 3.3 `export_catalog_json`

- **用途**：從本機 SQLite 匯出最新啟用中（`IsActive == 1`）元件清單為 `catalog.json` 結構，供線上簡易版（`gh-pages`）使用。
- **輸入參數 Schema**：
  | 參數名稱 | 型別 | 必填 | 預設值 | 驗證規則 |
  | :--- | :--- | :---: | :--- | :--- |
  | `databasePath` | `string` | 否 | `"src/ECCSELECTION.Web/eccselection.db"` | 須通過 `WorkspaceGuard` 驗證位於工作區內 |
  | `outputPath` | `string` | 否 | `null` | 若 `writeFile == true` 則必填，且須位於工作區內 |
  | `writeFile` | `boolean` | 否 | `false` | 預設 `false`（唯讀預覽）；`true` 時以 UTF-8 無 BOM 寫入 `outputPath` |

- **執行邏輯**：
  1. 確認 `databasePath` 檔案存在；若不存在，回傳 `success: false` 與明確錯誤訊息（不自動建立空白 DB）。
  2. 以 `Data Source=<resolvedPath>;Mode=ReadOnly` 開啟 `SqliteConnection`。
  3. 查詢：
     ```sql
     SELECT Id, ComponentType, Name, Path, RawUrl, HtmlUrl,
            DescriptionEn, SummaryZh, SdlcRoles, TechStacks, ArchStyles, UsageGuide
     FROM EccComponents
     WHERE IsActive = 1
     ORDER BY ComponentType ASC, Name ASC
     ```
  4. 將 `ComponentType` 整數值映射為字串（`0 -> "Agent"`, `1 -> "Skill"`, `2 -> "Rule"`, `3 -> "Command"`），產出與 `gh-pages:data/catalog.json` 完全相容之 PascalCase JSON 陣列（排除 `Sha`、`LastSyncedAt`、`IsActive`、`IsManuallyEdited`）。
  5. 若 `writeFile == true`，寫入 `outputPath`；否則於回傳物件提供統計資訊與預覽/JSON 字串。
- **結構化回傳格式**：
  ```json
  {
    "success": true,
    "totalExported": 620,
    "countsByType": {
      "Agent": 35,
      "Skill": 510,
      "Rule": 45,
      "Command": 30
    },
    "wroteFile": false,
    "outputPath": null,
    "previewItems": [
      { "Id": 1, "ComponentType": "Agent", "Name": "a11y-architect", "Path": "agents/a11y-architect.md" }
    ]
  }
  ```

---

### 3.4 `create_spec_file`

- **用途**：依 `docs/specs/SPEC_<YYYYMMDD>_<主題>.md` 命名規範建立獨立規格檔骨架，從工具層強制落實「規格獨立成檔、嚴禁修改 `SPEC_ECCSELECTION.md`」之工作流程規範。
- **輸入參數 Schema**：
  | 參數名稱 | 型別 | 必填 | 預設值 | 驗證規則 |
  | :--- | :--- | :---: | :--- | :--- |
  | `topic` | `string` | 是 | - | 僅允許 `[A-Za-z0-9_-]`，長度 `2..64`，自動轉大寫且 `-` 轉 `_` |
  | `title` | `string` | 是 | - | 規格中文標題，長度 `2..120`，禁止控制字元 |
  | `author` | `string` | 否 | `"SA Agent"` | 撰寫者標記 |
  | `date` | `string` | 否 | 當日 `yyyyMMdd` | 須符合 `^\d{8}$` 且為合法日期 |
  | `overwrite` | `boolean` | 否 | `false` | 預設禁止覆寫已存在之規格檔 |

- **執行邏輯與防呆機制**：
  1. 嚴格驗證 `topic` 與 `date`，組合目標相對路徑：`docs/specs/SPEC_<date>_<TOPIC>.md`。
  2. 透過 `WorkspaceGuard` 鎖定寫入目錄必須嚴格位於 `<repoRoot>/docs/specs/` 之下，**絕對禁止**寫入根目錄或觸碰 `SPEC_ECCSELECTION.md`。
  3. 若目錄 `docs/specs/` 不存在則自動建立。
  4. 若目標檔案已存在且 `overwrite == false`，拒絕寫入並回傳 `success: false`。
  5. 寫入標準規格骨架（包含：文件資訊、背景與目標、影響範圍與寫入邊界、功能需求與資料契約、安全性設計、單元測試計畫、人類確認閘門 Checklist）。
- **結構化回傳格式**：
  ```json
  {
    "success": true,
    "relativePath": "docs/specs/SPEC_20261126_NEW_FEATURE.md",
    "fullPath": "D:\\T01_GIT\\ECCSELECTION\\docs\\specs\\SPEC_20261126_NEW_FEATURE.md",
    "bytesWritten": 1024
  }
  ```

---

### 3.5 `check_ui_contracts`

- **用途**：靜態掃描 `src/ECCSELECTION.Web/Views/`、`src/ECCSELECTION.Web/wwwroot/` 與 `src/ECCSELECTION.Web/` 程式碼，自動檢查 UI 與安全契約，取代人工肉眼巡檢。
- **輸入參數 Schema**：
  | 參數名稱 | 型別 | 必填 | 預設值 | 驗證規則 |
  | :--- | :--- | :---: | :--- | :--- |
  | `targetScope` | `string` | 否 | `"all"` | 允許值：`"all"`, `"views"`, `"wwwroot"`, `"csharp"` |

- **靜態檢查規則**：
  1. **`UI001`（Error）— 禁止 `<table>` 殘留**：
     - 掃描 `Views/**/*.cshtml` 與 `wwwroot/js/**/*.js`，偵測 `<table`（不分大小寫、排除註解）。
  2. **`CS001`（Error）— 禁止空 `catch` 區塊**：
     - 掃描 `src/ECCSELECTION.Web/**/*.cs`（排除 `bin/`、`obj/`），偵測 `catch` 區塊大括號內無任何實質陳述式（僅含空白或註解）。
  3. **`SEC001`（Error）— Controller POST/PUT/DELETE/PATCH 須具備 Antiforgery 保護**：
     - 掃描 `src/ECCSELECTION.Web/Controllers/**/*.cs`，若 Action 標註 `[HttpPost]`、`[HttpPut]`、`[HttpDelete]` 或 `[HttpPatch]`，則該方法或所屬 Controller 必須標註 `[ValidateAntiForgeryToken]`（若標註 `[IgnoreAntiforgeryToken]` 則降為 `warning` 供審查）。
  4. **`UI002`（Warning）— 前端 `innerHTML` 賦值提示**：
     - 掃描 `wwwroot/js/**/*.js` 中的 `.innerHTML =`，列為 `warning` 提醒確認無未消毒之動態 HTML 拼接。
- **結構化回傳格式**：
  ```json
  {
    "passed": true,
    "summary": {
      "scannedFiles": 38,
      "errorCount": 0,
      "warningCount": 1
    },
    "violations": [
      {
        "ruleId": "UI002",
        "severity": "warning",
        "file": "src/ECCSELECTION.Web/wwwroot/js/site.js",
        "line": 42,
        "snippet": "el.innerHTML = ...",
        "message": "Detected innerHTML assignment; verify content is sanitized or static."
      }
    ]
  }
  ```

---

## 4. 安全性與邊界設計

1. **路徑遍歷防護（Path Traversal Prevention）**：
   - `WorkspaceGuard.ResolveSafePath(string baseDir, string targetPath)` 一律先以 `Path.GetFullPath()` 解析絕對路徑，並驗證結果必須以 `Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar` 開頭（Windows 下使用 `StringComparison.OrdinalIgnoreCase`）。
   - 拒絕任何含 `..` 逃逸、UNC 網路路徑（`\\`）或跨磁碟機路徑之請求。
2. **禁止命令注入（No Command Injection）**：
   - 外部進程（`git`、`pwsh`、`powershell`）一律透過 `ProcessStartInfo` 設定 `UseShellExecute = false` 與 `ArgumentList.Add(...)` 傳參。
   - **嚴禁**使用 `cmd.exe /c`、字串插值命令列或 `Invoke-Expression`。
   - 對 `commitMessage`、`topic`、`date` 等文字參數實施嚴格白名單正規驗證。
3. **工作區與分支鎖定**：
   - `McpWorkspaceOptions` 於啟動時驗證根目錄必須存在 `ECCSELECTION.slnx`，否則拒絕執行寫入類工具。
   - `sync_and_publish_lite` 強制於暫存 `worktree` 操作並驗證 `HEAD == gh-pages` 與 `push origin gh-pages:gh-pages`，絕不影響主工作區 `main`。
4. **唯讀與寫入邊界隔離**：
   - `export_catalog_json` 預設 `writeFile = false` 且 SQLite 連線強制 `Mode=ReadOnly`。
   - `create_spec_file` 僅可寫入 `docs/specs/`，硬編碼拒絕 `SPEC_ECCSELECTION.md`。
   - `check_ui_contracts` 全程唯讀。
5. **敏感資訊保護**：
   - `eccselection-full.zip` 透過 `git archive main` 產出，確保本機 `appsettings.Development.local.json`、`eccselection.db` 與 API 金鑰永不進入發佈包或日誌。

---

## 5. Antigravity 註冊與使用方式（`mcp_config.json`）

依據 Antigravity MCP 指南（`mcp_config.json` Stdio Transport 規範），於全域設定檔 `~/.gemini/config/mcp_config.json`（Windows 路徑：`C:\Users\<使用者>\.gemini\config\mcp_config.json`）加入以下設定：

### 5.1 建議設定（使用預先建置執行檔，啟動最快且無編譯鎖定）

```json
{
  "mcpServers": {
    "eccselection-workflow": {
      "command": "D:/T01_GIT/ECCSELECTION/tools/ECCSELECTION.Mcp/bin/Release/net10.0/ECCSELECTION.Mcp.exe",
      "args": [],
      "env": {
        "ECCSELECTION_ROOT": "D:\\T01_GIT\\ECCSELECTION"
      }
    }
  }
}
```

### 5.2 開發期替代設定（`dotnet run --no-build`）

```json
{
  "mcpServers": {
    "eccselection-workflow": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:/T01_GIT/ECCSELECTION/tools/ECCSELECTION.Mcp/ECCSELECTION.Mcp.csproj",
        "--no-build"
      ],
      "env": {
        "ECCSELECTION_ROOT": "D:\\T01_GIT\\ECCSELECTION"
      }
    }
  }
}
```

> **驗證方式**：設定完成並先執行一次 `dotnet build tools/ECCSELECTION.Mcp/ECCSELECTION.Mcp.csproj -c Release` 後，於 Antigravity UI 點選 **Additional Options (...) > MCP Servers**，確認 `eccselection-workflow` 顯示連線成功且列出上述 5 個 Tools。

---

## 6. xUnit 單元測試設計（`tests/ECCSELECTION.Tests/Mcp/`）

為確保 MCP 工具行為穩定且可回歸驗證，於 `tests/ECCSELECTION.Tests/Mcp/` 建立對應測試（透過模擬 `IProcessRunner` 與暫存隔離目錄，不對真實 `origin` 或真實工作區造成副作用）：

| 測試檔案 | 測試重點與涵蓋案例 |
| :--- | :--- |
| `WorkspaceGuardTests.cs` | 路徑遍歷攻擊（`../`、絕對路徑逃逸、UNC 路徑）必拋 `SecurityException`/`ArgumentException`；合法子路徑正確解析；`topic` 與 `date` 白名單驗證。 |
| `QualityGateServiceTests.cs` | Mock `IProcessRunner` 驗證 4 階段輸出解析（全數通過、第 2 階段失敗導致後續 `skipped`、逾時中止處理）。 |
| `PagesSyncServiceTests.cs` | Mock `IProcessRunner` 驗證：`dryRun=true` 必不呼叫 `git push`；當 worktree `HEAD != gh-pages` 時強制中止；`finally` 必呼叫 `git worktree remove --force`。 |
| `CatalogExportServiceTests.cs` | 建立暫存 SQLite DB，驗證僅匯出 `IsActive = 1`、正確映射 `ComponentType` 名稱、不包含 `Sha`/`IsManuallyEdited`；DB 不存在時回傳結構化錯誤。 |
| `SpecScaffolderServiceTests.cs` | 驗證依 `SPEC_<YYYYMMDD>_<TOPIC>.md` 建立於 `docs/specs/`；已存在且 `overwrite=false` 時拒絕覆寫；不可寫入 `SPEC_ECCSELECTION.md`。 |
| `UiContractCheckerServiceTests.cs` | 建立暫存假專案目錄，驗證成功偵測 `<table>` 殘留、空 `catch`（單行/多行/僅註解）、缺少 `[ValidateAntiForgeryToken]` 之 `[HttpPost]` Action，以及合規目錄回傳 `passed=true`。 |

---

## 7. 人類確認閘門（Human-in-the-Loop Checklist）

- [ ] 確認 `tools/ECCSELECTION.Mcp/` 目錄位置與納入 `ECCSELECTION.slnx`（`/tools/` 資料夾）之設計。
- [ ] 確認 5 個 MCP Tools 名稱、參數預設值（特別是 `sync_and_publish_lite` 預設 `dryRun: true`、`export_catalog_json` 預設 `writeFile: false`）與回傳結構。
- [ ] 確認安全性與寫入邊界設計（路徑遍歷防護、`ArgumentList` 防命令注入、`gh-pages` worktree 隔離）。
- [ ] 核准進入下一階段：由後端實作 Agent 與測試 Agent 依本規格進行開發與 xUnit 測試。
