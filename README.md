# ECCSELECTION — ECC 知識庫與 Harness 選型系統

本地可運行之 ASP.NET Core MVC (`.NET 10`) + SQLite 知識庫系統，可從 GitHub `affaan-m/ECC` 增量同步 `agents`、`skills`、`rules` 與 `commands`，並透過互動式 SDLC 問券產出可解釋的 Harness 推薦清單與 Markdown 匯出文件。

## 執行方式

```powershell
cd D:\T01_GIT\ECCSELECTION
dotnet build
dotnet test
dotnet run --project src\ECCSELECTION.Web\ECCSELECTION.Web.csproj
```

## 雙版本網站架構

本專案同時維護兩個版本的網站，嚴禁混淆或覆蓋：
- **本機完整版（`main` 分支）**：ASP.NET Core MVC（`.NET 10`）+ SQLite，執行於 `http://localhost:5109`，含完整 3D Dashboard、`/Catalog`（瀏覽與人工編輯）、`/Sync`（GitHub 同步）與 `/Wizard`（SDLC 問券精靈與 Markdown 匯出）。
- **線上簡易版（`gh-pages` 分支）**：純靜態 3D 版（`index.html`、`app.js`、`css/icon3d.css`、`data/catalog.json`），發佈於 `https://devon119.github.io/ecc-harness-studio/`，提供唯讀 3D 知識庫瀏覽、3D 問券精靈、前端 `.zip` 打包及完整版一鍵安裝包（`downloads/eccselection-full.zip`）。

## 專案目錄與工程工具

- **`docs/specs/`**：獨立功能規格文件目錄（遵循「規格獨立成檔、不累加修改 `SPEC_ECCSELECTION.md`」原則）。
- **`scripts/quality-gate.ps1`**：強制 4 階段品質閘門腳本，依序驗證 `dotnet build -warnaserror`、`dotnet format --verify-no-changes`、`dotnet test`（含覆蓋率）與 `npx --yes ecc-agentshield scan`。
- **`tools/ECCSELECTION.Mcp/`**：本機 `.NET 10` Stdio MCP Server（註冊名稱 `eccselection`），封裝 5 項工作流程工具：
  - `run_quality_gate`：安全清理鎖定進程並執行 `scripts/quality-gate.ps1`
  - `sync_and_publish_lite`：透過隔離 `git worktree` 安全打包與同步 `gh-pages`（預設 `dryRun: true`）
  - `export_catalog_json`：唯讀匯出 SQLite 啟用中元件為 `catalog.json` 格式
  - `create_spec_file`：依規範於 `docs/specs/` 建立獨立規格骨架
  - `check_ui_contracts`：靜態掃描 `<table>` 殘留、空 `catch`、Antiforgery 屬性與 `innerHTML`

## 可選設定（建議使用 User Secrets）

- `GitHubSync:PersonalAccessToken`：可選，避免匿名 GitHub API 每小時 60 次限流。
- `LlmEnrichment:Enabled` / `Endpoint` / `ApiKey` / `Model`：可選，未設定時自動使用確定性規則式繁體中文摘要。
