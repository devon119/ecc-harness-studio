---
trigger: always_on
---
# ECCSELECTION Workspace Rules & .ecc Workflow

在 `D:\T01_GIT\ECCSELECTION` 進行任何開發任務時，必須遵循 `.ecc/rules/project/eccselection-mvc.md` 與 `AGENTS.md` 定義之流程：

1. **協調員監控與專責唯讀隔離**：主 Agent 僅擔任專責協調員（派發、監控、驗收），各階段必須由對應專責子代理執行（SA／後端／前端／測試／審查與驗證），每個子代理僅可修改其負責之檔案範圍，對其他 Agent 負責的內容一律唯讀。
2. **規格先行（獨立成檔）＋ 人類確認閘門**：功能或規則異動先由 SA Agent 於 `docs/specs/` 建立**獨立的規格文件**（如 `docs/specs/SPEC_<YYYYMMDD>_<主題>.md`），**嚴禁再一直累加更新同一份 `SPEC_ECCSELECTION.md`**；完成後**必須暫停等待人類明確確認**，確認無誤後方可進入實作。
3. **嚴禁略過任何流程**：每一項需求皆須完整走完「SA 規格 → 人類確認 → 實作 → 測試 → 審查與品質閘門 → 實機驗證 → 文件同步」全流程，禁止 AI 自行略過步驟。
4. **MVC 分層**：商業邏輯放 `Services/`，Controller 僅處理 HTTP 驗證（含 `[ValidateAntiForgeryToken]`）與 ViewModel 映射。
5. **嚴禁靜默吞錯**：禁止空 `catch`，外部 HTTP 失敗須記錄並寫入 `SyncLogs.ErrorMessage`。
6. **強制品質閘門與證據回報**：修改完成後，**必須執行** `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1`（涵蓋 `dotnet build -warnaserror`、`dotnet format --verify-no-changes`、`dotnet test` 含覆蓋率、`npx ecc-agentshield scan`），並在回覆中逐項附上 4 項實測結果；若有前端 `Views/` / `Controllers/` / JS 異動，須於 `http://localhost:5109` 實機驗證後再更新 `DEVELOPMENT_STATUS.md`。未跑完腳本前禁止宣告完成。
