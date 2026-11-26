# ECCSELECTION — 專案開發規範與工作流程（Antigravity 自動載入）

本工作區已建立專屬 ECC Harness（位於 `.ecc/`）。所有在此專案進行的需求變更、程式修改與審查，必須遵守以下規範與流程：

## 1. 最高專案規範（來自 `.ecc/rules/project/eccselection-mvc.md`）
- **方案與技術棧**：`ECCSELECTION.slnx`、`src/ECCSELECTION.Web`（ASP.NET Core MVC `.NET 10`, EF Core + SQLite）、`tests/ECCSELECTION.Tests`（xUnit + SQLite `:memory:`）。嚴禁未經同意改為 SPA 前後端分離或替換 ORM。
- **分層紀律**：Controller 僅負責 HTTP 驗證與 ViewModel 映射；商業邏輯與共用規則必須放在 `Services/`。
- **核心資料契約**：
  - `EccComponents.Path` 為唯一鍵、`Sha` 為變更比對依據。
  - `IsManuallyEdited == true` 之人工欄位在同步時不得覆蓋。
  - 未設定 `LlmEnrichment:ApiKey` 時必須非阻塞降級為規則式分類。
  - 嚴禁空的 `catch` 區塊；外部 HTTP 失敗必須記錄日誌並寫入 `SyncLogs.ErrorMessage`。
  - 問券推薦（`RecommendationService`）維持確定性初篩與收斂規則，並支援可選 Gemini LLM 精選層（未設定 `LlmEnrichment:ApiKey` 時非阻塞降級為確定性收斂推薦）。

## 2. 多代理分工與嚴格執行流程（協調員監控 ＋ 唯讀邊界）
本專案採**「專責協調員監控 ＋ 專業 Agent 職責與寫入邊界隔離」**模式執行，**禁止任何 AI 自行判斷略過任何階段**：

### 2.1 協調員與專責 Agent 寫入邊界（對其他範圍一律唯讀）
- **專責協調員（Coordinator，主 Agent）**：只負責接收需求、拆解與派發子代理（`define_subagent` / `invoke_subagent`）、監控流程進度、驗收交付物與最終彙報；**不越權直接修改業務程式碼、測試或規格**。
- **SA / 規格 Agent（`planner` / `architect`）**：僅可於 `docs/specs/` 建立獨立規格檔（如 `docs/specs/SPEC_<YYYYMMDD>_<主題>.md` 或 `SPEC_v<X.Y>_<主題>.md`）；**嚴禁繼續累加修改同一份 `SPEC_ECCSELECTION.md`**，對既有核心規格、程式碼與測試一律唯讀。
- **後端實作 Agent**：僅可寫入 `src/ECCSELECTION.Web/Services/`、`Models/`、`Data/`、`Program.cs`（對規格、前端與測試唯讀）。
- **前端 / MVC Agent**：僅可寫入 `src/ECCSELECTION.Web/Controllers/`、`Views/`、`wwwroot/`（對規格、後端核心與測試唯讀）。
- **測試 Agent（`tdd-guide`）**：僅可寫入 `tests/ECCSELECTION.Tests/`（對規格與正式程式碼唯讀）。
- **審查與驗證 Agent（`csharp-reviewer` / `security-reviewer` / `e2e-runner` / `browser`）**：**全專案唯讀**，僅負責審查、執行品質閘門與 `http://localhost:5109` 實機驗證。
- **文件同步 Agent**：僅可寫入 `DEVELOPMENT_STATUS.md`（與必要時之 `VALIDATION_REPORT_ECCSELECTION.md`）。

### 2.2 強制完整流程（嚴禁跳步）
1. **SA / 規格先行（`plan`）**：由 SA / 規格 Agent 分析需求並於 `docs/specs/` 建立**獨立的新規格文件**（不可累加修改同一份 `SPEC_ECCSELECTION.md`）。
2. **人類確認閘門（Human-in-the-Loop，強制暫停點）**：SA / 規格產出或更新完成後，協調員**必須暫停並向人類呈報規格摘要，取得人類明確確認後才可進入下一階段**，不得自動往下實作。
3. **分層實作**：依規格由後端實作 Agent 與前端 / MVC Agent 分別在其寫入邊界內進行開發。
4. **TDD 與測試覆蓋（`tdd-guide` / `test-coverage`）**：由測試 Agent 於 `tests/ECCSELECTION.Tests/` 補齊對應 xUnit 單元測試。
5. **程式碼與安全審查（`code-review` / `csharp-reviewer` / `security-scan`）**：由唯讀審查 Agent 檢查輸入驗證、`[ValidateAntiForgeryToken]`、無空 `catch`、無硬編碼密鑰、方法長度與命名規範。
6. **強制品質閘門（`quality-gate`，未跑完禁止宣告完成）**：
   - 任何程式碼異動後，**必須執行** `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1`，一次通過以下 4 項檢查：
     1. `dotnet build ECCSELECTION.slnx -warnaserror`（0 警告、0 錯誤）
     2. `dotnet format ECCSELECTION.slnx --verify-no-changes --no-restore`
     3. `dotnet test ECCSELECTION.slnx --no-build --verbosity normal --collect:"XPlat Code Coverage"`
     4. `npx --yes ecc-agentshield scan --path . --format text`
   - 若修改涉及 `Views/`、`Controllers/` 或前端 JS，須由 `browser` 子代理於 `http://localhost:5109` 實機驗證。
   - **證據輸出契約**：最終回覆必須逐項列出上述 4 項（及 UI 實機驗證）的實測結果，不得只口頭聲稱已檢查。
7. **文件同步**：完成所有驗證後，由文件同步 Agent 更新 `DEVELOPMENT_STATUS.md`（與必要時之 `VALIDATION_REPORT_ECCSELECTION.md`）。

## 3. 詳細參考目錄
- 最高規範：`.ecc/rules/project/eccselection-mvc.md`
- 流程指令：`.ecc/commands/`（`plan`, `code-review`, `security-scan`, `quality-gate`, `test-coverage`, `build-fix`）
- 角色檢查清單：`.ecc/agents/`（`csharp-reviewer.md`, `database-reviewer.md`, `tdd-guide.md`, `e2e-runner.md`, `security-reviewer.md`）
- 長期記憶：`AI_MEMORY.md`

## 4. 雙版本網站架構（不可忘記）
本專案（`devon119/ecc-harness-studio`）同時維護兩個版本的網站：
- **本機完整版（`main` 分支）**：ASP.NET Core MVC（`.NET 10`）+ SQLite，執行於 `http://localhost:5109`，含完整 3D Dashboard、`/Catalog`（瀏覽與編輯）、`/Sync`（GitHub 同步）與 `/Wizard`。
- **線上簡易版（`gh-pages` 分支）**：純靜態版（`index.html`、`app.js`、`css/icon3d.css`、`data/catalog.json`），發佈於 `https://devon119.github.io/ecc-harness-studio/`，含 3D Dashboard、唯讀 3D 知識庫瀏覽、3D 問券精靈與前端 `.zip` 打包，並提供完整版一鍵安裝包（`downloads/eccselection-full.zip`、`install-eccselection.ps1/.sh`）。發佈時嚴禁混淆或覆蓋兩個分支的定位。
