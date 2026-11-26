(() => {
    "use strict";

    const LS_KEY = "eccselection_gemini_api_key";
    const LS_MODEL = "eccselection_gemini_model";

    const COMPATIBILITY_RULES = {
        Web: { defaultArch: "MVC", allowedArch: ["MVC", "Clean"], defaultStacks: ["dotnet"], allowedStacks: ["dotnet", "react", "vue", "python", "go", "java", "sqlserver", "sqlite", "postgres", "mysql"], allowedRoles: ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"] },
        Mobile: { defaultArch: "MVVM", allowedArch: ["MVVM", "Clean"], defaultStacks: ["kotlin", "sqlite"], allowedStacks: ["kotlin", "sqlite"], allowedRoles: ["SA", "SD", "PG_FE", "PG_DB", "PG_TEST", "QA"] },
        API: { defaultArch: "Clean", allowedArch: ["Clean", "MVC"], defaultStacks: ["dotnet"], allowedStacks: ["dotnet", "python", "go", "java", "sqlserver", "sqlite", "postgres", "mysql"], allowedRoles: ["SA", "SD", "PG_BE", "PG_DB", "PG_TEST", "QA"] },
        Fullstack: { defaultArch: "Clean", allowedArch: ["Clean", "MVC"], defaultStacks: ["dotnet", "react"], allowedStacks: ["dotnet", "react", "vue", "python", "go", "java", "sqlserver", "sqlite", "postgres", "mysql"], allowedRoles: ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"] },
        Game: { defaultArch: "ECS", allowedArch: ["ECS", "Clean", "MVC"], defaultStacks: ["luau"], allowedStacks: ["luau", "godot", "dotnet", "cpp", "sqlite"], allowedRoles: ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"] }
    };

    const TECH_STACKS = [
        ["dotnet", ".NET / C#"], ["kotlin", "Kotlin / Android"], ["react", "React"], ["vue", "Vue.js"],
        ["python", "Python"], ["go", "Go (Golang)"], ["java", "Java / Spring"], ["luau", "Roblox Luau / Lua"],
        ["godot", "Godot / GDScript"], ["cpp", "C++"], ["sqlserver", "SQL Server"],
        ["sqlite", "SQLite"], ["postgres", "PostgreSQL"], ["mysql", "MySQL"]
    ];

    const SDLC_ROLES = [
        ["SA", "SA (系統分析)"], ["SD", "SD (系統設計)"], ["PG_FE", "PG-前端"],
        ["PG_BE", "PG-後端"], ["PG_DB", "PG-資料庫"], ["PG_TEST", "PG-測試"], ["QA", "QA (品保審查)"]
    ];

    const ROLE_LABELS = Object.fromEntries(SDLC_ROLES);
    const TYPE_LABELS = { 1: "Agent", 2: "Skill", 3: "Rule", 4: "Command", Agent: "Agent", Skill: "Skill", Rule: "Rule", Command: "Command" };

    let catalog = [];
    let extractedKeywords = [];
    let currentResult = null;

    const $ = (id) => document.getElementById(id);
    const splitTokens = (v) => (v || "").split(",").map(s => s.trim().toLowerCase()).filter(Boolean);
    const normType = (t) => TYPE_LABELS[t] || String(t);

    function escapeHtml(str) {
        return String(str || "").replace(/[&<>"']/g, (m) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[m]));
    }

    function renderCheckboxes() {
        $("techStackCards").innerHTML = TECH_STACKS.map(([slug, label]) => `
            <div class="col-6 col-md-3 col-lg-2">
                <div class="form-check border rounded p-2 bg-light" data-stack-card="${slug}">
                    <input class="form-check-input ms-1" type="checkbox" value="${slug}" id="stack_${slug}" ${slug === "dotnet" ? "checked" : ""} />
                    <label class="form-check-label ms-2 small" for="stack_${slug}">${label}</label>
                </div>
            </div>`).join("");

        $("sdlcRoleCards").innerHTML = SDLC_ROLES.map(([code, label]) => `
            <div class="col-6 col-md-3">
                <div class="form-check border rounded p-2 bg-light" data-role-card="${code}">
                    <input class="form-check-input ms-1" type="checkbox" value="${code}" id="role_${code}" ${code !== "PG_FE" ? "checked" : ""} />
                    <label class="form-check-label ms-2 small" for="role_${code}">${label}</label>
                </div>
            </div>`).join("");
    }

    function applyCompatibility() {
        const rule = COMPATIBILITY_RULES[$("ProjectType").value] || COMPATIBILITY_RULES.Web;
        const archSel = $("ArchStyle");
        Array.from(archSel.options).forEach(o => {
            const ok = rule.allowedArch.includes(o.value);
            o.disabled = !ok;
        });
        if (!rule.allowedArch.includes(archSel.value)) archSel.value = rule.defaultArch;

        syncGroup("[data-stack-card]", "data-stack-card", rule.allowedStacks, rule.defaultStacks);
        syncGroup("[data-role-card]", "data-role-card", rule.allowedRoles, rule.allowedRoles);
    }

    function syncGroup(selector, attr, allowed, fallback) {
        const set = new Set(allowed.map(x => x.toLowerCase()));
        let anyChecked = false;
        document.querySelectorAll(selector).forEach(card => {
            const key = card.getAttribute(attr).toLowerCase();
            const cb = card.querySelector("input");
            const ok = set.has(key);
            cb.disabled = !ok;
            card.classList.toggle("opacity-50", !ok);
            if (!ok) cb.checked = false;
            else if (cb.checked) anyChecked = true;
        });
        if (!anyChecked && fallback) {
            const fb = new Set(fallback.map(x => x.toLowerCase()));
            document.querySelectorAll(selector).forEach(card => {
                const cb = card.querySelector("input");
                if (!cb.disabled && fb.has(card.getAttribute(attr).toLowerCase())) cb.checked = true;
            });
        }
    }

    function inferCommands(projectType, stacks) {
        const s = new Set(stacks.map(x => x.toLowerCase()));
        const sec = "npx --yes ecc-agentshield scan --path . --format text";
        if (s.has("python")) return ["Python 3.12 / FastAPI / pytest", "python -m compileall src", "ruff check .", "pytest", sec];
        if (s.has("go")) return ["Go 1.23", "go build ./...", "gofmt -l .", "go test ./...", sec];
        if (s.has("kotlin")) return ["Android / Kotlin / Gradle", "./gradlew assembleDebug", "./gradlew ktlintCheck", "./gradlew testDebugUnitTest", sec];
        if (s.has("java")) return ["Java 21 / Spring Boot", "./mvnw compile -q", "./mvnw spotless:check", "./mvnw test", sec];
        if (s.has("luau")) return ["Roblox Studio / Luau", "selene src/", "stylua --check src/", "busted tests/", sec];
        if (s.has("godot")) return ["Godot 4 / GDScript", "godot --headless --check-only --quit", "gdlint src/", "godot --headless -s addons/gut/gut_cmdln.gd", sec];
        if ((s.has("react") || s.has("vue")) && !s.has("dotnet")) return ["Node.js / TypeScript", "npm run build", "npm run lint", "npm test", sec];
        return [projectType === "API" ? ".NET 10 / ASP.NET Core Web API" : ".NET 10 / ASP.NET Core MVC", "dotnet build -warnaserror", "dotnet format --verify-no-changes --no-restore", "dotnet test --no-build --verbosity normal", sec];
    }

    function refreshGeminiBadge() {
        const key = localStorage.getItem(LS_KEY) || "";
        const badge = $("geminiConfigBadge");
        if (key) {
            badge.className = "badge bg-success";
            badge.textContent = "已啟用 Gemini API（金鑰存於瀏覽器本機）";
            $("geminiApiKeyInput").value = key;
            $("geminiModelInput").value = localStorage.getItem(LS_MODEL) || "gemini-2.5-flash";
        } else {
            badge.className = "badge bg-secondary";
            badge.textContent = "未設定 API Key（使用確定性收斂，或於結果區使用 Chrome Gemini 接力）";
        }
    }

    async function readUploadedFileText() {
        const input = $("RequirementDocument");
        if (!input.files || input.files.length === 0) return "";
        const file = input.files[0];
        if (file.size > 2 * 1024 * 1024) throw new Error("需求文件超過 2MB 上限。");
        return await file.text();
    }

    function deterministicAnalyze(text) {
        const lower = text.toLowerCase();
        const stacks = [];
        const stackTriggers = [
            ["dotnet", ["dotnet", ".net", "c#", "csharp", "asp.net"]],
            ["kotlin", ["kotlin", "android"]], ["react", ["react", "nextjs"]], ["vue", ["vue", "nuxt"]],
            ["python", ["python", "fastapi", "django", "pytest"]], ["go", ["golang", "go test", "gin"]],
            ["java", ["java", "spring"]], ["luau", ["luau", "roblox"]], ["godot", ["godot", "gdscript"]],
            ["cpp", ["c++", "cpp"]], ["postgres", ["postgres", "postgresql"]], ["sqlite", ["sqlite"]],
            ["sqlserver", ["sql server", "mssql"]], ["mysql", ["mysql"]]
        ];
        stackTriggers.forEach(([slug, triggers]) => {
            if (triggers.some(t => lower.includes(t))) stacks.push(slug);
        });

        let projectType = "Web";
        if (stacks.includes("luau") || stacks.includes("godot") || lower.includes("遊戲")) projectType = "Game";
        else if (stacks.includes("kotlin") || lower.includes("android")) projectType = "Mobile";
        else if (lower.includes("microservice") || lower.includes("微服務") || lower.includes("api")) projectType = (stacks.includes("react") || stacks.includes("vue")) ? "Fullstack" : "API";
        else if ((stacks.includes("react") || stacks.includes("vue")) && stacks.includes("dotnet")) projectType = "Fullstack";

        const lifecycle = (lower.includes("legacy") || lower.includes("refactor") || lower.includes("重構") || lower.includes("舊系統")) ? "Brownfield" : "Greenfield";
        const kws = ["security", "payment", "api", "database", "tdd", "e2e", "refactor", "architect", "review"].filter(k => lower.includes(k)).concat(stacks);
        return { projectType, lifecycle, stacks, keywords: [...new Set(kws)] };
    }

    async function callGeminiApi(prompt) {
        const key = localStorage.getItem(LS_KEY);
        if (!key) return null;
        const model = localStorage.getItem(LS_MODEL) || "gemini-2.5-flash";
        const url = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent?key=${encodeURIComponent(key)}`;
        const res = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                contents: [{ parts: [{ text: prompt }] }],
                generationConfig: { temperature: 0.1, responseMimeType: "application/json" }
            })
        });
        if (!res.ok) return null;
        const data = await res.json();
        return data?.candidates?.[0]?.content?.parts?.[0]?.text || null;
    }

    function getFormState() {
        const stacks = Array.from(document.querySelectorAll("[data-stack-card] input:checked")).map(c => c.value);
        const roles = Array.from(document.querySelectorAll("[data-role-card] input:checked")).map(c => c.value);
        return {
            projectType: $("ProjectType").value,
            lifecycle: $("ProjectLifecycle").value,
            archStyle: $("ArchStyle").value,
            mode: $("Mode").value,
            stacks,
            roles,
            purpose: $("ProjectPurpose").value.trim(),
            keywords: extractedKeywords,
            opts: {
                projectName: $("optProjectName").value.trim() || "MyProject",
                specFilePath: $("optSpecFilePath").value.trim() || "SPEC.md",
                targetPlatform: $("optTargetPlatform").value,
                devServerUrl: $("optDevServerUrl").value.trim(),
                targetEnvironment: $("optTargetEnvironment").value.trim(),
                scopeGuardNote: $("optScopeGuardNote").value.trim(),
                protectedPaths: $("optProtectedPaths").value.trim(),
                buildCommand: $("optBuildCommand").value.trim(),
                formatCommand: $("optFormatCommand").value.trim(),
                testCommand: $("optTestCommand").value.trim(),
                securityScanCommand: $("optSecurityScanCommand").value.trim(),
                requireSpecFirst: $("chkRequireSpecFirst").checked,
                requireTestsBeforeDone: $("chkRequireTestsBeforeDone").checked,
                requireEvidenceOutput: $("chkRequireEvidenceOutput").checked,
                includeGitPreCommitHook: $("chkIncludeGitPreCommitHook").checked,
                includeIdeAutoRules: $("chkIncludeIdeAutoRules").checked
            }
        };
    }

    function recommendDeterministic(state) {
        const isCompact = state.mode !== "Comprehensive";
        const minScore = isCompact ? 50 : 40;
        const selectedStacks = new Set(state.stacks.map(s => s.toLowerCase()));
        const selectedArch = state.archStyle.toLowerCase();

        const evaluate = (comp, role) => {
            const cRoles = splitTokens(comp.SdlcRoles);
            if (!cRoles.includes(role.toLowerCase())) return null;
            const cStacks = splitTokens(comp.TechStacks);
            const hasCommon = cStacks.includes("common");
            const specific = cStacks.filter(s => s !== "common");
            const matched = specific.filter(s => selectedStacks.has(s));
            if (!hasCommon && specific.length > 0 && matched.length === 0) return null;

            let score = 40;
            const reasons = [`符合角色：${ROLE_LABELS[role] || role} (+40)`];
            if (matched.length > 0) {
                const b = Math.min(matched.length * 25, 50);
                score += b;
                reasons.push(`命中技術棧：${matched.join(", ")} (+${b})`);
            }
            if (hasCommon) { score += 15; reasons.push("通用工程規範 (+15)"); }
            const cArch = splitTokens(comp.ArchStyles);
            if (cArch.includes(selectedArch)) { score += 15; reasons.push(`符合架構：${state.archStyle} (+15)`); }
            else if (cArch.includes("general")) { score += 5; }

            const searchable = `${comp.Name} ${comp.DescriptionEn} ${comp.SummaryZh} ${comp.UsageGuide}`.toLowerCase();
            const kwHit = state.keywords.filter(k => searchable.includes(k));
            if (kwHit.length > 0) {
                const kb = Math.min(kwHit.length * 8, 32);
                score += kb;
                reasons.push(`命中需求關鍵字：${kwHit.join(", ")} (+${kb})`);
            }
            return { comp, score, reasons, role };
        };

        const agentMap = new Map();
        const allScored = new Map();
        state.roles.forEach(role => {
            catalog.forEach(comp => {
                const ev = evaluate(comp, role);
                if (!ev || ev.score < minScore) return;
                if (!allScored.has(comp.Id) || ev.score > allScored.get(comp.Id).score) allScored.set(comp.Id, ev);
                if (normType(comp.ComponentType) === "Agent") {
                    const prev = agentMap.get(comp.Id);
                    if (!prev) agentMap.set(comp.Id, { ...ev, coveredRoles: new Set([ROLE_LABELS[role] || role]) });
                    else {
                        prev.coveredRoles.add(ROLE_LABELS[role] || role);
                        if (ev.score > prev.score) prev.score = ev.score;
                    }
                }
            });
        });

        const maxSuites = isCompact ? 3 : 6;
        const maxSkills = isCompact ? 2 : 3;
        const maxRules = isCompact ? 2 : 3;
        const maxCommands = isCompact ? 1 : 2;
        const topAgents = Array.from(agentMap.values()).sort((a, b) => b.score - a.score).slice(0, maxSuites);
        const nonAgents = catalog.filter(c => normType(c.ComponentType) !== "Agent");

        const suites = topAgents.map(agentEv => {
            const aRoles = splitTokens(agentEv.comp.SdlcRoles);
            const aTokens = agentEv.comp.Name.toLowerCase().split("-").filter(t => t.length > 2);
            const paired = [];
            nonAgents.forEach(cand => {
                const cStacks = splitTokens(cand.TechStacks);
                const hasCommon = cStacks.includes("common");
                const specific = cStacks.filter(s => s !== "common");
                const matched = specific.filter(s => selectedStacks.has(s));
                if (!hasCommon && specific.length > 0 && matched.length === 0) return;

                let ps = 0;
                const pr = [];
                const overlap = aRoles.filter(r => splitTokens(cand.SdlcRoles).includes(r));
                if (overlap.length > 0) { ps += Math.min(overlap.length * 15, 35); pr.push(`共同涵蓋角色 (${overlap.join(", ")})`); }
                if (matched.length > 0) { ps += Math.min(matched.length * 20, 40); pr.push(`符合技術棧 (${matched.join(", ")})`); }
                else if (hasCommon) { ps += 15; pr.push("通用規範"); }
                if (aTokens.some(t => cand.Name.toLowerCase().includes(t) || cand.Path.toLowerCase().includes(t))) {
                    ps += 35; pr.push(`與 ${agentEv.comp.Name} 領域互補`);
                }
                const cType = normType(cand.ComponentType);
                const reqScore = cType === "Rule" ? 30 : (isCompact ? 40 : 30);
                if (ps >= reqScore) paired.push({ comp: cand, score: ps, reasons: pr, type: cType });
            });
            paired.sort((a, b) => b.score - a.score);
            return {
                leadAgent: agentEv,
                coveredRoles: Array.from(agentEv.coveredRoles),
                pairedSkills: paired.filter(x => x.type === "Skill").slice(0, maxSkills),
                pairedRules: paired.filter(x => x.type === "Rule").slice(0, maxRules),
                pairedCommands: paired.filter(x => x.type === "Command").slice(0, maxCommands)
            };
        });

        const suiteIds = new Set();
        suites.forEach(s => {
            suiteIds.add(s.leadAgent.comp.Id);
            [...s.pairedSkills, ...s.pairedRules, ...s.pairedCommands].forEach(p => suiteIds.add(p.comp.Id));
        });

        const additionalCandidates = Array.from(allScored.values())
            .filter(x => !suiteIds.has(x.comp.Id))
            .sort((a, b) => b.score - a.score)
            .slice(0, 25);

        return { state, suites, additionalCandidates, curationSource: "Deterministic", playbookMarkdown: "" };
    }

    function buildPlaybook(res) {
        const o = res.state.opts;
        const lines = [
            `# ${o.projectName} — Harness 運作說明書（Operational Playbook）`,
            "",
            `- **專案配置**：\`${res.state.projectType}\` / \`${res.state.lifecycle}\` / \`${res.state.archStyle}\`（技術棧：\`${res.state.stacks.join(", ")}\`）`,
            `- **目標環境**：\`${o.targetEnvironment}\``,
            `- **規格書卡點**：\`${o.specFilePath}\``,
            "",
            "## 一、SDLC 日常開發動線",
            `1. **規格先行（Spec-First）**：任何需求異動前，先更新 \`${o.specFilePath}\`。`,
            "2. **AI 協作執行**：依任務階段呼叫以下精選 Lead Agent 與 Command："
        ];
        res.suites.forEach(s => {
            const cmds = s.pairedCommands.length ? s.pairedCommands.map(c => `\`/${c.comp.Name}\``).join(", ") : "（無額外 Command）";
            lines.push(`   - **\`${s.leadAgent.comp.Name}\`**（${s.coveredRoles.join("、")}）— 指令：${cmds}`);
        });
        lines.push("3. **自動背景規範**：已選 `Rules` 與根目錄 `AGENTS.md` / `CLAUDE.md` 會於每次對話與編輯自動載入。");
        lines.push("", "## 二、已啟用之防堵卡點（Guardrails）");
        lines.push(`- **規格先行卡點**：${o.requireSpecFirst ? `已啟用 — 修改核心程式前未更新 \`${o.specFilePath}\` 時阻擋。` : "未啟用"}`);
        lines.push(`- **品質閘門腳本**：${o.requireTestsBeforeDone ? `已啟用 — 必須執行 \`scripts/quality-gate.ps1\` / \`.sh\`（建置：\`${o.buildCommand}\`、測試：\`${o.testCommand}\`）。` : "未啟用"}`);
        lines.push(`- **證據輸出契約**：${o.requireEvidenceOutput ? "已啟用 — 未實際執行品質閘門並逐項附上終端實測輸出前，嚴禁宣告完成。" : "未啟用"}`);
        lines.push(`- **Git Pre-Commit 卡點**：${o.includeGitPreCommitHook ? "已啟用 — `.githooks/pre-commit` 於提交前攔截密鑰外洩與格式違規。" : "未啟用"}`);
        lines.push(`- **IDE 自動載入規則 & Hooks**：${o.includeIdeAutoRules ? `已啟用 — 透過 \`AGENTS.md\` / \`CLAUDE.md\` 與 \`.claude/settings.json\` \`permissions.deny\`（阻擋 \`${o.protectedPaths}\`）防堵。` : "未啟用"}`);
        return lines.join("\n");
    }

    function buildCurationPrompt(res) {
        const all = [];
        const seen = new Set();
        res.suites.forEach(s => [s.leadAgent, ...s.pairedSkills, ...s.pairedRules, ...s.pairedCommands].forEach(x => {
            if (!seen.has(x.comp.Id)) { seen.add(x.comp.Id); all.push(x.comp); }
        }));
        res.additionalCandidates.forEach(x => {
            if (!seen.has(x.comp.Id) && all.length < 35) { seen.add(x.comp.Id); all.push(x.comp); }
        });
        const o = res.state.opts;
        const list = all.map(c => `- ID=${c.Id} | ${normType(c.ComponentType)} | ${c.Name} | Stacks=${c.TechStacks} | ${(c.SummaryZh || c.DescriptionEn || "").slice(0, 120)}`).join("\n");
        return `你是資深 AI Harness 架構師。請從下方候選清單精挑細選最必要的核心組合（2~3 Agent，每個最多 1~2 Skill、1~2 Rule、0~1 Command）。\n\n## 專案條件\n- 名稱：${o.projectName}\n- 配置：${res.state.projectType} / ${res.state.lifecycle} / ${res.state.archStyle} / ${res.state.stacks.join(", ")}\n- 用途：${res.state.purpose || "無"}\n- 卡點：規格先行=${o.requireSpecFirst}, 品質閘門=${o.requireTestsBeforeDone}, 證據輸出=${o.requireEvidenceOutput}, GitHook=${o.includeGitPreCommitHook}, IDE規則=${o.includeIdeAutoRules}\n\n## 候選元件\n${list}\n\n## 請只回傳純 JSON：\n{"selectedIds":[1,2],"reasons":{"1":"入選理由"},"playbookMarkdown":"# 運作說明書..."}`;
    }

    function applyCurationJson(res, rawText, sourceLabel) {
        const cleaned = rawText.trim().replace(/^```(?:json)?\s*/i, "").replace(/```$/i, "").trim();
        const parsed = JSON.parse(cleaned);
        if (!Array.isArray(parsed.selectedIds) || parsed.selectedIds.length === 0) throw new Error("JSON 缺少有效的 selectedIds");
        const ids = new Set(parsed.selectedIds.map(Number));
        const reasons = parsed.reasons || {};

        const moved = [];
        res.suites = res.suites.filter(s => {
            if (!ids.has(s.leadAgent.comp.Id)) {
                [s.leadAgent, ...s.pairedSkills, ...s.pairedRules, ...s.pairedCommands].forEach(x => { if (!ids.has(x.comp.Id)) moved.push(x); });
                return false;
            }
            if (reasons[s.leadAgent.comp.Id]) s.leadAgent.reasons.unshift(`[Gemini 精選] ${reasons[s.leadAgent.comp.Id]}`);
            const filterList = (arr) => arr.filter(x => {
                if (ids.has(x.comp.Id)) { if (reasons[x.comp.Id]) x.reasons.unshift(`[Gemini 精選] ${reasons[x.comp.Id]}`); return true; }
                moved.push(x); return false;
            });
            s.pairedSkills = filterList(s.pairedSkills);
            s.pairedRules = filterList(s.pairedRules);
            s.pairedCommands = filterList(s.pairedCommands);
            return true;
        });

        const suiteIds = new Set();
        res.suites.forEach(s => [s.leadAgent, ...s.pairedSkills, ...s.pairedRules, ...s.pairedCommands].forEach(x => suiteIds.add(x.comp.Id)));
        res.additionalCandidates = [...res.additionalCandidates, ...moved].filter(x => !suiteIds.has(x.comp.Id));
        if (parsed.playbookMarkdown) res.playbookMarkdown = parsed.playbookMarkdown;
        res.curationSource = sourceLabel;
    }

    function renderResult(res) {
        currentResult = res;
        $("resultSection").classList.remove("d-none");
        $("curationSourceBadge").textContent = `精選來源：${res.curationSource}`;
        $("playbookPreview").textContent = res.playbookMarkdown;

        $("suitesContainer").innerHTML = res.suites.map(s => {
            const paired = [...s.pairedSkills, ...s.pairedRules, ...s.pairedCommands];
            const roleBadges = (s.coveredRoles || []).map(r => `<span class="badge bg-primary-subtle text-primary ms-1">${escapeHtml(r)}</span>`).join("");
            return `
            <div class="card shadow-sm border-0 mb-3">
                <div class="card-header bg-white py-2 d-flex justify-content-between align-items-center">
                    <div>
                        <span class="badge bg-dark me-1">Lead Agent</span>
                        <strong>${escapeHtml(s.leadAgent.comp.Name)}</strong>
                        <span class="badge bg-info text-dark ms-1">Score: ${s.leadAgent.score}</span>
                        <span class="ms-2 small text-muted">適用角色：</span>${roleBadges}
                        <div class="small text-success mt-1">${escapeHtml(s.leadAgent.reasons.join(" ｜ "))}</div>
                    </div>
                    <label class="small border rounded px-2 py-1 bg-light">
                        <input type="checkbox" class="form-check-input me-1 comp-chk" value="${s.leadAgent.comp.Id}" checked /> 包含 Agent
                    </label>
                </div>
                <div class="table-responsive">
                    <table class="table table-sm table-hover align-middle mb-0">
                        <thead class="table-light"><tr><th style="width:48px">勾選</th><th style="width:90px">類型</th><th>名稱</th><th>理由</th></tr></thead>
                        <tbody>
                            ${paired.map(p => `
                                <tr>
                                    <td class="text-center"><input type="checkbox" class="form-check-input comp-chk" value="${p.comp.Id}" checked /></td>
                                    <td><span class="badge bg-secondary">${normType(p.comp.ComponentType)}</span></td>
                                    <td class="fw-semibold small">${escapeHtml(p.comp.Name)}</td>
                                    <td class="small text-muted">${escapeHtml(p.reasons.join("、"))}</td>
                                </tr>`).join("")}
                        </tbody>
                    </table>
                </div>
            </div>`;
        }).join("");

        $("additionalCandidatesContainer").innerHTML = res.additionalCandidates.length === 0 ? "" : `
            <div class="card shadow-sm border-0 mb-4">
                <div class="card-header bg-light py-2 d-flex justify-content-between align-items-center">
                    <span class="fw-bold small">其他候選元件（預設未勾選，共 ${res.additionalCandidates.length} 項，可手動加勾）</span>
                    <button class="btn btn-sm btn-outline-secondary" type="button" data-bs-toggle="collapse" data-bs-target="#candCollapse">展開 / 收合</button>
                </div>
                <div class="collapse" id="candCollapse">
                    <div class="table-responsive">
                        <table class="table table-sm mb-0">
                            <tbody>
                                ${res.additionalCandidates.map(c => `
                                    <tr>
                                        <td style="width:48px" class="text-center"><input type="checkbox" class="form-check-input comp-chk" value="${c.comp.Id}" /></td>
                                        <td style="width:90px"><span class="badge bg-secondary">${normType(c.comp.ComponentType)}</span></td>
                                        <td class="small fw-semibold">${escapeHtml(c.comp.Name)}</td>
                                        <td class="small text-muted">${escapeHtml(c.comp.UsageGuide || c.comp.SummaryZh || "")} <span class="badge bg-primary-subtle text-primary">${escapeHtml(ROLE_LABELS[c.role] || c.role || "")}</span></td>
                                    </tr>`).join("")}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>`;

        const updateCount = () => {
            const count = new Set(Array.from(document.querySelectorAll(".comp-chk:checked")).map(c => c.value)).size;
            $("selectedCountBadge").textContent = String(count);
            $("downloadZipBtn").disabled = count === 0;
        };
        document.querySelectorAll(".comp-chk").forEach(c => c.addEventListener("change", updateCount));
        updateCount();
        $("resultSection").scrollIntoView({ behavior: "smooth" });
    }

    async function downloadHarnessZip() {
        if (!currentResult || typeof JSZip === "undefined") return;
        const selectedIds = new Set(Array.from(document.querySelectorAll(".comp-chk:checked")).map(c => Number(c.value)));
        const selectedComps = catalog.filter(c => selectedIds.has(c.Id));
        const o = currentResult.state.opts;
        const rootDir = o.targetPlatform === "Antigravity" ? ".ecc" : ".claude";

        const zip = new JSZip();
        const btn = $("downloadZipBtn");
        btn.disabled = true;
        $("downloadProgressText").textContent = `正在從 GitHub 下載 ${selectedComps.length} 個元件檔案...`;

        const errors = [];
        let skillAuxPaths = [];
        if (selectedComps.some(c => normType(c.ComponentType) === "Skill")) {
            try {
                const treeRes = await fetch("https://api.github.com/repos/affaan-m/ECC/git/trees/main?recursive=1");
                if (treeRes.ok) {
                    const treeJson = await treeRes.json();
                    skillAuxPaths = (treeJson.tree || [])
                        .filter(x => x.type === "blob" && x.path && x.path.startsWith("skills/") && !x.path.endsWith("/SKILL.md"))
                        .map(x => x.path);
                }
            } catch { /* non-blocking fallback to SKILL.md only */ }
        }

        const downloadTargets = [];
        selectedComps.forEach(c => {
            const t = normType(c.ComponentType);
            const path = t === "Agent" ? `${rootDir}/agents/${c.Name}.md`
                : t === "Skill" ? `${rootDir}/skills/${c.Name}/SKILL.md`
                : t === "Command" ? `${rootDir}/commands/${c.Name}.md`
                : `${rootDir}/${c.Path}`;
            downloadTargets.push({ name: c.Name, zipPath: path, rawUrl: c.RawUrl });

            if (t === "Skill" && skillAuxPaths.length > 0 && c.Path) {
                const folderPrefix = c.Path.slice(0, c.Path.lastIndexOf("/") + 1);
                skillAuxPaths
                    .filter(p => p.startsWith(folderPrefix))
                    .forEach(aux => downloadTargets.push({
                        name: `${c.Name} (aux)`,
                        zipPath: `${rootDir}/${aux}`,
                        rawUrl: `https://raw.githubusercontent.com/affaan-m/ECC/main/${aux}`
                    }));
            }
        });

        await Promise.all(downloadTargets.map(async (item) => {
            try {
                const resp = await fetch(item.rawUrl);
                if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
                zip.file(item.zipPath, await resp.text());
            } catch (e) {
                errors.push(`${item.name} (${item.rawUrl}): ${e.message}`);
            }
        }));

        if (o.includeIdeAutoRules) {
            const guardDoc = `# ${o.projectName} — AI Harness 強制規範\n\n- 規格書：\`${o.specFilePath}\`\n- 架構邊界：${o.scopeGuardNote}\n- 受保護路徑：\`${o.protectedPaths}\`\n\n## HARD GATES\n1. 規格先行：修改程式前先更新 \`${o.specFilePath}\`。\n2. 品質閘門：執行 \`scripts/quality-gate.ps1\`（\`${o.buildCommand}\` / \`${o.testCommand}\`），未通過禁止宣告完成。\n3. 證據契約：最終回覆須逐項附上實測輸出。`;
            if (o.targetPlatform !== "ClaudeCode") {
                zip.file("AGENTS.md", guardDoc);
                zip.file(".agents/rules/workflow-guardrails.md", guardDoc);
            }
            if (o.targetPlatform !== "Antigravity") {
                zip.file("CLAUDE.md", guardDoc);
                const deny = o.protectedPaths.split(",").map(s => s.trim()).filter(Boolean).flatMap(p => [`Edit(${p})`, `Write(${p})`]).concat(["Bash(rm -rf *)"]);
                zip.file(".claude/settings.json", JSON.stringify({ permissions: { deny } }, null, 2));
            }
        }
        if (o.requireTestsBeforeDone || o.includeIdeAutoRules) {
            zip.file("scripts/quality-gate.ps1", `$ErrorActionPreference = "Stop"\n${o.buildCommand}\n${o.formatCommand}\n${o.testCommand}\n${o.securityScanCommand}\n`);
            zip.file("scripts/quality-gate.sh", `#!/usr/bin/env bash\nset -euo pipefail\n${o.buildCommand}\n${o.formatCommand}\n${o.testCommand}\n${o.securityScanCommand}\n`);
        }
        if (o.includeGitPreCommitHook) {
            zip.file(".githooks/pre-commit", `#!/usr/bin/env bash\nset -euo pipefail\n${o.formatCommand}\n`);
            zip.file("scripts/install-hooks.ps1", `git config core.hooksPath .githooks\n`);
        }
        zip.file("HARNESS_PLAYBOOK.md", currentResult.playbookMarkdown);
        if (errors.length > 0) zip.file("DOWNLOAD_ERRORS.txt", errors.join("\n"));

        const blob = await zip.generateAsync({ type: "blob" });
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = `${o.projectName.toLowerCase().replace(/[^a-z0-9_-]+/g, "-") || "ecc"}-harness-bundle.zip`;
        a.click();
        $("downloadProgressText").textContent = errors.length ? `完成（${errors.length} 個檔案下載失敗，詳見 DOWNLOAD_ERRORS.txt）` : "打包下載完成！";
        btn.disabled = false;
    }

    async function init() {
        renderCheckboxes();
        applyCompatibility();
        refreshGeminiBadge();
        $("ProjectType").addEventListener("change", applyCompatibility);

        try {
            const res = await fetch("data/catalog.json");
            catalog = await res.json();
            $("catalogCountBadge").textContent = `已載入 ${catalog.length} 筆 ECC 元件`;
        } catch {
            $("catalogCountBadge").textContent = "知識庫載入失敗";
        }

        $("saveGeminiKeyBtn").addEventListener("click", () => {
            const key = $("geminiApiKeyInput").value.trim();
            if (!key) return;
            localStorage.setItem(LS_KEY, key);
            localStorage.setItem(LS_MODEL, $("geminiModelInput").value.trim() || "gemini-2.5-flash");
            refreshGeminiBadge();
            $("geminiSaveStatus").className = "alert alert-success py-1 px-2 mb-0 small mt-2";
            $("geminiSaveStatus").textContent = "已儲存至您個人瀏覽器 localStorage。";
        });

        $("clearGeminiKeyBtn").addEventListener("click", () => {
            localStorage.removeItem(LS_KEY);
            $("geminiApiKeyInput").value = "";
            refreshGeminiBadge();
        });

        $("analyzeReqBtn").addEventListener("click", async () => {
            const box = $("analysisStatusBox");
            try {
                const fileText = await readUploadedFileText();
                const combined = `${$("ProjectPurpose").value}\n${fileText}`.trim();
                if (!combined) return;
                const det = deterministicAnalyze(combined);
                extractedKeywords = det.keywords;
                $("ProjectType").value = det.projectType;
                $("ProjectLifecycle").value = det.lifecycle;
                applyCompatibility();
                if (det.stacks.length > 0) {
                    document.querySelectorAll("[data-stack-card] input").forEach(cb => {
                        if (!cb.disabled) cb.checked = det.stacks.includes(cb.value);
                    });
                }
                const [env, b, f, t, s] = inferCommands(det.projectType, det.stacks);
                $("optTargetEnvironment").value = env;
                $("optBuildCommand").value = b;
                $("optFormatCommand").value = f;
                $("optTestCommand").value = t;
                $("optSecurityScanCommand").value = s;
                box.className = "alert alert-success mt-3 mb-0 py-2 small";
                box.textContent = `已解析需求：建議 ${det.projectType} / ${det.lifecycle}，技術棧 [${det.stacks.join(", ") || "預設"}]，關鍵字 [${det.keywords.join(", ")}]。`;
            } catch (err) {
                box.className = "alert alert-danger mt-3 mb-0 py-2 small";
                box.textContent = err.message;
            }
        });

        $("generateBtn").addEventListener("click", async () => {
            const btn = $("generateBtn");
            btn.disabled = true;
            btn.textContent = "推薦與精選中...";
            try {
                const state = getFormState();
                const res = recommendDeterministic(state);
                res.playbookMarkdown = buildPlaybook(res);
                const prompt = buildCurationPrompt(res);
                res.chromePrompt = prompt;
                if (localStorage.getItem(LS_KEY)) {
                    const llmText = await callGeminiApi(prompt);
                    if (llmText) {
                        try { applyCurationJson(res, llmText, "Gemini API (Browser)"); } catch { res.curationSource = "Deterministic (Gemini Fallback)"; }
                    }
                }
                renderResult(res);
            } finally {
                btn.disabled = false;
                btn.textContent = "產生精選推薦與組建 Harness";
            }
        });

        $("copyAndOpenGeminiBtn").addEventListener("click", async () => {
            if (!currentResult) return;
            try { await navigator.clipboard.writeText(currentResult.chromePrompt || ""); $("copyGeminiStatus").classList.remove("d-none"); } catch { }
            window.open("https://gemini.google.com/app", "_blank", "noopener");
        });

        $("applyChromeGeminiBtn").addEventListener("click", () => {
            if (!currentResult) return;
            try {
                applyCurationJson(currentResult, $("chromeGeminiResponseInput").value, "Chrome Gemini Web");
                renderResult(currentResult);
            } catch (e) {
                alert(`套用失敗：${e.message}`);
            }
        });

        $("downloadZipBtn").addEventListener("click", downloadHarnessZip);
    }

    init();
})();
