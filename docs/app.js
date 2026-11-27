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
    let iconSeq = 0;

    const $ = (id) => document.getElementById(id);
    const splitTokens = (v) => (v || "").split(",").map(s => s.trim().toLowerCase()).filter(Boolean);
    const normType = (t) => TYPE_LABELS[t] || String(t);

    function escapeHtml(str) {
        return String(str || "").replace(/[&<>"']/g, (m) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[m]));
    }

    function renderIcon3D(kind, size = "md", label = "") {
        const k = String(kind || "Agent").trim().toLowerCase();
        const sz = ["xs", "sm", "md", "lg", "xl"].includes(size) ? `i3d-${size}` : "i3d-md";
        const uid = `g${++iconSeq}`;
        const aria = escapeHtml(label || `${kind} 3D 圖示`);
        let svg = "";

        if (k === "agent") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><defs>
                <linearGradient id="agT-${uid}" x1="12" y1="8" x2="52" y2="28" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#a5b4fc"/><stop offset="100%" stop-color="#6366f1"/></linearGradient>
                <linearGradient id="agL-${uid}" x1="10" y1="20" x2="32" y2="56" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#4f46e5"/><stop offset="100%" stop-color="#312e81"/></linearGradient>
                <linearGradient id="agR-${uid}" x1="32" y1="20" x2="54" y2="56" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#818cf8"/><stop offset="100%" stop-color="#4338ca"/></linearGradient>
            </defs><polygon points="32,6 54,18 32,30 10,18" fill="url(#agT-${uid})"/><polygon points="10,18 32,30 32,56 10,44" fill="url(#agL-${uid})"/><polygon points="54,18 32,30 32,56 54,44" fill="url(#agR-${uid})"/><circle cx="32" cy="18" r="5" fill="#eef2ff"/><path d="M32 30V43M21 24L32 30L43 24" stroke="#c7d2fe" stroke-width="2" stroke-linecap="round"/><circle cx="32" cy="43" r="3" fill="#38bdf8"/></svg>`;
        } else if (k === "skill") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><defs>
                <linearGradient id="skT-${uid}" x1="14" y1="8" x2="50" y2="28" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#6ee7b7"/><stop offset="100%" stop-color="#10b981"/></linearGradient>
                <linearGradient id="skL-${uid}" x1="12" y1="20" x2="32" y2="56" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#059669"/><stop offset="100%" stop-color="#064e3b"/></linearGradient>
                <linearGradient id="skR-${uid}" x1="32" y1="20" x2="52" y2="56" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#34d399"/><stop offset="100%" stop-color="#047857"/></linearGradient>
            </defs><polygon points="32,7 52,19 32,31 12,19" fill="url(#skT-${uid})"/><polygon points="12,19 32,31 32,56 12,44" fill="url(#skL-${uid})"/><polygon points="52,19 32,31 32,56 52,44" fill="url(#skR-${uid})"/><circle cx="32" cy="19" r="5.5" stroke="#ecfdf5" stroke-width="2.4"/><path d="M22 36L28 40L22 44M42 36L36 40L42 44" stroke="#a7f3d0" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
        } else if (k === "rule" || k === "gate") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><defs>
                <linearGradient id="rlF-${uid}" x1="14" y1="8" x2="50" y2="56" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#fde68a"/><stop offset="50%" stop-color="#f59e0b"/><stop offset="100%" stop-color="#b45309"/></linearGradient>
            </defs><path d="M32 7L53 15V31C53 44 43 53 32 58C21 53 11 44 11 31V15L32 7Z" fill="#92400e" transform="translate(2,2)" opacity="0.4"/><path d="M32 6L52 14V30C52 43 42 52 32 57C22 52 12 43 12 30V14L32 6Z" fill="url(#rlF-${uid})"/><path d="M24 31L30 37L41 24" stroke="#fffbeb" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
        } else if (k === "command") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><rect x="8" y="18" width="48" height="36" rx="10" fill="#164e63"/><rect x="12" y="11" width="40" height="33" rx="8" fill="#06b6d4"/><path d="M22 23L28 28L22 33M31 33H41" stroke="#083344" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
        } else if (k === "success") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><circle cx="32" cy="32" r="24" fill="#10b981"/><ellipse cx="24" cy="20" rx="8" ry="4" transform="rotate(-25 24 20)" fill="#ffffff" opacity="0.45"/><path d="M22 33L29 40L43 25" stroke="#ffffff" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
        } else if (k === "lock") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><path d="M22 27V20C22 14.5 26.5 10 32 10C37.5 10 42 14.5 42 20V27" stroke="#d97706" stroke-width="5" stroke-linecap="round"/><rect x="15" y="26" width="34" height="28" rx="7" fill="#f59e0b"/><circle cx="32" cy="39" r="3.5" fill="#78350f"/><path d="M32 42V47" stroke="#78350f" stroke-width="3" stroke-linecap="round"/></svg>`;
        } else if (k === "llm") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><path d="M24 14C17 14 12 19 12 26C10 29 10 35 14 39C14 46 20 51 27 51H30V14H24Z" fill="#7c3aed"/><path d="M40 14C47 14 52 19 52 26C54 29 54 35 50 39C50 46 44 51 37 51H34V14H40Z" fill="#6366f1"/><circle cx="23" cy="28" r="2.5" fill="#ecfeff"/><circle cx="41" cy="28" r="2.5" fill="#ecfeff"/><circle cx="32" cy="38" r="3" fill="#38bdf8"/></svg>`;
        } else if (["sqlserver", "sqlite", "postgres", "mysql", "pg_db"].includes(k)) {
            const top = { sqlserver: "#f87171", postgres: "#60a5fa", mysql: "#fbbf24", pg_db: "#a78bfa" }[k] || "#38bdf8";
            const body = { sqlserver: "#b91c1c", postgres: "#1d4ed8", mysql: "#d97706", pg_db: "#6d28d9" }[k] || "#0284c7";
            svg = `<svg viewBox="0 0 64 64" fill="none"><path d="M14 18V46C14 51 22 55 32 55C42 55 50 51 50 46V18H14Z" fill="${body}"/><ellipse cx="32" cy="18" rx="18" ry="8" fill="${top}"/><path d="M14 28C14 33 22 37 32 37C42 37 50 33 50 28" stroke="#ffffff" stroke-opacity="0.45" stroke-width="2"/><path d="M14 38C14 43 22 47 32 47C42 47 50 43 50 38" stroke="#ffffff" stroke-opacity="0.35" stroke-width="2"/></svg>`;
        } else if (k === "web") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><rect x="8" y="14" width="48" height="36" rx="7" fill="#312e81" transform="translate(3,3)" opacity="0.4"/><rect x="8" y="12" width="48" height="36" rx="7" fill="#4f46e5"/><rect x="10" y="21" width="44" height="25" rx="4" fill="#eef2ff"/><rect x="15" y="26" width="16" height="14" rx="3" fill="#818cf8"/><rect x="34" y="26" width="15" height="6" rx="2" fill="#cbd5e1"/><rect x="34" y="34" width="15" height="6" rx="2" fill="#cbd5e1"/></svg>`;
        } else if (k === "mobile") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><rect x="18" y="7" width="28" height="48" rx="6" fill="#14b8a6"/><rect x="21" y="13" width="22" height="34" rx="3" fill="#f0fdfa"/><circle cx="32" cy="51" r="2" fill="#ccfbf1"/></svg>`;
        } else if (k === "api") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><rect x="12" y="12" width="40" height="12" rx="4" fill="#0284c7"/><rect x="12" y="27" width="40" height="12" rx="4" fill="#0369a1"/><rect x="12" y="42" width="40" height="12" rx="4" fill="#0c4a6e"/><circle cx="20" cy="18" r="2.2" fill="#34d399"/><circle cx="20" cy="33" r="2.2" fill="#34d399"/><circle cx="20" cy="48" r="2.2" fill="#38bdf8"/></svg>`;
        } else if (k === "fullstack") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><polygon points="32,8 52,18 32,28 12,18" fill="#818cf8"/><polygon points="32,22 52,32 32,42 12,32" fill="#34d399"/><polygon points="32,36 52,46 32,56 12,46" fill="#38bdf8"/></svg>`;
        } else if (k === "game") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><rect x="10" y="18" width="44" height="28" rx="12" fill="#7c3aed"/><path d="M22 26V38M16 32H28" stroke="#f5f3ff" stroke-width="3.5" stroke-linecap="round"/><circle cx="41" cy="29" r="3" fill="#fbbf24"/><circle cx="47" cy="35" r="3" fill="#34d399"/></svg>`;
        } else if (k === "bundle") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><polygon points="32,8 54,19 32,30 10,19" fill="#93c5fd"/><polygon points="10,19 32,30 32,54 10,43" fill="#3b82f6"/><polygon points="54,19 32,30 32,54 54,43" fill="#1d4ed8"/></svg>`;
        } else if (k === "playbook") {
            svg = `<svg viewBox="0 0 64 64" fill="none"><rect x="14" y="10" width="36" height="44" rx="6" fill="#334155"/><rect x="18" y="14" width="28" height="36" rx="4" fill="#f8fafc"/><path d="M24 23H40M24 31H40M24 39H34" stroke="#6366f1" stroke-width="3" stroke-linecap="round"/></svg>`;
        } else {
            const fill = {
                greenfield: "#10b981", brownfield: "#d97706", compact: "#4f46e5", comprehensive: "#059669",
                dotnet: "#512bd4", kotlin: "#7f52ff", react: "#0ea5e9", vue: "#10b981", python: "#2563eb",
                go: "#06b6d4", java: "#ea580c", luau: "#3b82f6", godot: "#0284c7", cpp: "#1d4ed8",
                sa: "#4f46e5", sd: "#0ea5e9", pg_fe: "#10b981", pg_be: "#6366f1", pg_test: "#f59e0b", qa: "#ef4444",
                mvc: "#4f46e5", clean: "#059669", mvvm: "#0284c7", ecs: "#7c3aed", wizard: "#6366f1"
            }[k] || "#475569";
            svg = `<svg viewBox="0 0 64 64" fill="none"><polygon points="32,8 52,19 32,30 12,19" fill="${fill}" fill-opacity="0.72"/><polygon points="12,19 32,30 32,54 12,43" fill="${fill}"/><polygon points="52,19 32,30 32,54 52,43" fill="${fill}" fill-opacity="0.86"/><circle cx="32" cy="19" r="4" fill="#ffffff" fill-opacity="0.85"/></svg>`;
        }

        return `<span class="i3d-stage" title="${aria}" aria-label="${aria}" role="img"><span class="i3d-icon ${sz}">${svg}</span></span>`;
    }

    function renderScoreGauge(score) {
        const cls = score >= 80 ? "i3d-score-high" : score >= 60 ? "i3d-score-mid" : "i3d-score-low";
        const lit = Math.min(5, Math.max(1, Math.ceil(score / 20)));
        const cells = Array.from({ length: 5 }, (_, idx) => `<span class="i3d-gauge-cell ${idx < lit ? "is-lit" : ""}"></span>`).join("");
        return `<div class="i3d-score-gauge ${cls}" title="適配度分數：${score}"><div class="i3d-gauge-cells" aria-hidden="true">${cells}</div><span class="i3d-score-num">${score}</span></div>`;
    }

    function hydrateStaticIcons() {
        document.querySelectorAll("[data-icon3d]").forEach(el => {
            const [kind, size, label] = (el.getAttribute("data-icon3d") || "").split(":");
            el.innerHTML = renderIcon3D(kind, size || "md", label || kind);
        });
    }

    function renderCheckboxes() {
        $("techStackCards").innerHTML = TECH_STACKS.map(([slug, label]) => `
            <div class="col-6 col-md-3 col-lg-2">
                <div class="i3d-selector-tile i3d-checkbox-tile ${slug === "dotnet" ? "is-selected" : ""}" data-stack-card="${slug}">
                    <input class="form-check-input flex-shrink-0" type="checkbox" value="${slug}" id="stack_${slug}" ${slug === "dotnet" ? "checked" : ""} />
                    ${renderIcon3D(slug, "xs", `${label} 3D 圖示`)}
                    <label class="form-check-label small fw-semibold mb-0" for="stack_${slug}">${label}</label>
                </div>
            </div>`).join("");

        $("sdlcRoleCards").innerHTML = SDLC_ROLES.map(([code, label]) => `
            <div class="col-6 col-md-3">
                <div class="i3d-selector-tile i3d-checkbox-tile ${code !== "PG_FE" ? "is-selected" : ""}" data-role-card="${code}">
                    <input class="form-check-input flex-shrink-0" type="checkbox" value="${code}" id="role_${code}" ${code !== "PG_FE" ? "checked" : ""} />
                    ${renderIcon3D(code, "xs", `${label} 3D 圖示`)}
                    <label class="form-check-label small fw-semibold mb-0" for="role_${code}">${label}</label>
                </div>
            </div>`).join("");
    }

    function syncCheckboxTiles() {
        document.querySelectorAll(".i3d-checkbox-tile").forEach(tile => {
            const cb = tile.querySelector("input[type='checkbox']");
            if (!cb) return;
            tile.classList.toggle("is-selected", cb.checked && !cb.disabled);
            tile.classList.toggle("is-disabled", cb.disabled);
        });
    }

    function syncSelectTiles(selectId) {
        if (!selectId) return;
        const select = $(selectId);
        if (!select) return;
        const val = select.value;
        document.querySelectorAll(`[data-select-target="${selectId}"]`).forEach(tile => {
            const tileVal = tile.getAttribute("data-select-value");
            const opt = Array.from(select.options).find(o => o.value === tileVal);
            const disabled = Boolean(opt && opt.disabled);
            const selected = !disabled && tileVal === val;
            tile.classList.toggle("is-selected", selected);
            tile.classList.toggle("is-disabled", disabled);
            tile.setAttribute("aria-pressed", selected ? "true" : "false");
            tile.setAttribute("aria-disabled", disabled ? "true" : "false");
        });
    }

    function syncAllSelectors() {
        ["ProjectType", "ProjectLifecycle", "ArchStyle", "Mode"].forEach(syncSelectTiles);
        syncCheckboxTiles();
    }

    function initTactileSelectors() {
        document.addEventListener("click", (e) => {
            const selTile = e.target.closest("[data-select-target]");
            if (selTile) {
                if (selTile.classList.contains("is-disabled")) return;
                const targetId = selTile.getAttribute("data-select-target");
                const select = $(targetId);
                if (!select) return;
                select.value = selTile.getAttribute("data-select-value") || "";
                select.dispatchEvent(new Event("change", { bubbles: true }));
                syncSelectTiles(targetId);
                return;
            }

            const chkTile = e.target.closest(".i3d-checkbox-tile");
            if (chkTile) {
                const cb = chkTile.querySelector("input[type='checkbox']");
                if (!cb || cb.disabled) return;
                if (e.target !== cb && !e.target.closest("label")) {
                    cb.checked = !cb.checked;
                    cb.dispatchEvent(new Event("change", { bubbles: true }));
                }
                setTimeout(syncCheckboxTiles, 0);
            }
        });

        document.addEventListener("keydown", (e) => {
            if (e.key !== "Enter" && e.key !== " ") return;
            const selTile = e.target.closest("[data-select-target]");
            if (selTile) {
                e.preventDefault();
                selTile.click();
            }
        });

        document.addEventListener("change", (e) => {
            if (e.target && e.target.matches("select") && e.target.id) {
                syncSelectTiles(e.target.id);
            }
            if (e.target && e.target.matches("input[type='checkbox']")) {
                syncCheckboxTiles();
            }
        });
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
        syncAllSelectors();
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
            card.classList.toggle("is-disabled", !ok);
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

    const GREENFIELD_KW = ["architect", "plan", "planner", "tdd", "gan-planner", "gan-generator", "database-reviewer", "api-design", "scaffold", "greenfield", "spec"];
    const BROWNFIELD_KW = ["explore", "code-explorer", "spec-miner", "refactor", "cleaner", "silent-failure", "harness-optimizer", "review", "security", "audit", "e2e", "coverage", "legacy", "migration", "brownfield"];

    function recommendDeterministic(state) {
        const isCompact = state.mode !== "Comprehensive";
        const minScore = isCompact ? 50 : 40;
        const selectedStacks = new Set(state.stacks.map(s => s.toLowerCase()));
        const selectedArch = state.archStyle.toLowerCase();
        const isGameLuau = state.projectType === "Game" && selectedStacks.has("luau");

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

            const text = `${comp.Name} ${comp.Path} ${comp.DescriptionEn}`.toLowerCase();
            if (state.lifecycle === "Brownfield") {
                if (BROWNFIELD_KW.some(k => text.includes(k))) {
                    score += 20;
                    reasons.push("適用舊專案導入與重構（Brownfield） (+20)");
                }
            } else if (GREENFIELD_KW.some(k => text.includes(k))) {
                score += 20;
                reasons.push("適用新專案從零建置（Greenfield） (+20)");
            }

            if (state.projectType === "Game") {
                const isRobloxLuau = cStacks.includes("luau") || cArch.includes("ecs") || text.includes("roblox") || text.includes("luau");
                if (isGameLuau && isRobloxLuau) {
                    score += 20;
                    reasons.push("符合 Game / Roblox Luau 開發特性 (+20)");
                } else if (cStacks.includes("godot") || cStacks.includes("cpp") || text.includes("godot") || text.includes("gdscript") || text.includes("game") || isRobloxLuau) {
                    score += 15;
                    reasons.push("符合 Game 遊戲開發型態特性 (+15)");
                } else if (comp.Name.toLowerCase().startsWith("gan-")) {
                    const ganBonus = isGameLuau ? 5 : 15;
                    score += ganBonus;
                    reasons.push(`符合 Game 生成式輔助 (+${ganBonus})`);
                }
            }

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
                const cArch = splitTokens(cand.ArchStyles);
                if (cArch.includes(selectedArch)) { ps += 15; pr.push(`符合架構 (${state.archStyle})`); }
                if (isGameLuau && (matched.includes("luau") || cArch.includes("ecs") || cand.Name.toLowerCase().includes("roblox") || cand.Name.toLowerCase().includes("luau"))) {
                    ps += 15;
                    pr.push("Roblox / Luau 套件加權");
                }
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

    function renderModuleBrick(item, checked, drawerId) {
        const comp = item.comp;
        const typeName = normType(comp.ComponentType);
        const typeLower = typeName.toLowerCase();
        const reasons = item.reasons || [];
        const summary = comp.UsageGuide || comp.SummaryZh || comp.DescriptionEn || "";
        const capsules = reasons.slice(0, 2).map(r => `<span class="i3d-micro-capsule" title="${escapeHtml(r)}">${escapeHtml(r)}</span>`).join("");
        const extraCapsule = reasons.length > 2 ? `<span class="i3d-micro-capsule text-muted">+${reasons.length - 2}</span>` : "";

        return `
        <article class="i3d-component-card i3d-type-${typeLower} ${checked ? "is-checked" : ""} i3d-hover-tilt">
            <div class="d-flex justify-content-between align-items-start gap-2">
                <div class="d-flex align-items-center gap-2 min-w-0">
                    <input type="checkbox" class="form-check-input comp-chk flex-shrink-0" value="${comp.Id}" ${checked ? "checked" : ""} aria-label="選取 ${escapeHtml(comp.Name)}" />
                    ${renderIcon3D(typeName, "sm", `${typeName}：${comp.Name}`)}
                    <div class="min-w-0">
                        <div class="fw-bold text-dark text-truncate" title="${escapeHtml(comp.Name)}">${escapeHtml(comp.Name)}</div>
                        <span class="badge bg-light text-dark border small">${escapeHtml(typeName)}</span>
                    </div>
                </div>
                ${renderScoreGauge(item.score)}
            </div>
            <div class="i3d-summary-clamp" title="${escapeHtml(summary)}">${escapeHtml(summary)}</div>
            <div class="i3d-capsule-row">${capsules}${extraCapsule}</div>
            <div id="${drawerId}" class="i3d-detail-drawer d-none" data-i3d-drawer>
                <div class="mb-1"><strong>說明：</strong>${escapeHtml(summary)}</div>
                <div class="mb-1"><strong>路徑：</strong><code>${escapeHtml(comp.Path)}</code></div>
                <div><strong>推薦理由：</strong>${escapeHtml(reasons.join("；"))}</div>
            </div>
            <div class="mt-auto pt-2 border-top d-flex justify-content-between align-items-center">
                <button type="button" class="btn btn-sm btn-outline-secondary" data-i3d-toggle="${drawerId}" data-label-closed="展開詳情" data-label-open="收合詳情" aria-expanded="false" aria-controls="${drawerId}">
                    <span data-i3d-toggle-label>展開詳情</span>
                </button>
                <a href="${escapeHtml(comp.RawUrl)}" target="_blank" rel="noopener" class="btn btn-sm btn-outline-primary">Raw</a>
            </div>
        </article>`;
    }

    function renderResult(res) {
        currentResult = res;
        $("resultSection").classList.remove("d-none");
        $("curationSourceBadge").textContent = `精選來源：${res.curationSource}`;
        $("playbookPreview").textContent = res.playbookMarkdown;

        $("suitesContainer").innerHTML = res.suites.map((s, idx) => {
            const paired = [...s.pairedSkills, ...s.pairedRules, ...s.pairedCommands];
            const leadDrawerId = `gh-lead-${idx}-${s.leadAgent.comp.Id}`;
            const roleBadges = (s.coveredRoles || []).map(r => `
                <span class="i3d-micro-capsule bg-white text-dark">
                    <span>${escapeHtml(r)}</span>
                </span>`).join("");

            return `
            <section class="i3d-lead-card mb-4">
                <div class="i3d-lead-header d-flex flex-wrap justify-content-between align-items-start gap-3">
                    <div class="d-flex align-items-start gap-3">
                        <div class="i3d-float">${renderIcon3D("Agent", "lg", `Lead Agent：${s.leadAgent.comp.Name}`)}</div>
                        <div>
                            <div class="d-flex flex-wrap align-items-center gap-2 mb-1">
                                <span class="badge bg-info text-dark">Lead Agent 主機核心</span>
                                <h3 class="h5 mb-0 fw-bold text-white">${escapeHtml(s.leadAgent.comp.Name)}</h3>
                                ${renderScoreGauge(s.leadAgent.score)}
                            </div>
                            <div class="i3d-capsule-row mt-1">
                                ${roleBadges}
                                <button type="button" class="btn btn-sm btn-outline-light py-0 px-2" data-i3d-toggle="${leadDrawerId}" data-label-closed="展開 Agent 詳情" data-label-open="收合 Agent 詳情" aria-expanded="false" aria-controls="${leadDrawerId}">
                                    <span data-i3d-toggle-label>展開 Agent 詳情</span>
                                </button>
                            </div>
                        </div>
                    </div>
                    <div class="d-flex align-items-center gap-2">
                        <label class="form-check-label small rounded px-2 py-1 bg-white text-dark fw-semibold d-inline-flex align-items-center gap-1">
                            <input type="checkbox" class="form-check-input me-1 comp-chk" value="${s.leadAgent.comp.Id}" checked />
                            <span>包含此 Agent</span>
                        </label>
                        <a href="${escapeHtml(s.leadAgent.comp.RawUrl)}" target="_blank" rel="noopener" class="btn btn-sm btn-outline-light">Raw</a>
                    </div>
                </div>
                <div id="${leadDrawerId}" class="p-3 bg-white border-bottom d-none small" data-i3d-drawer>
                    <div class="mb-1"><strong>路徑：</strong><code>${escapeHtml(s.leadAgent.comp.Path)}</code></div>
                    <div class="text-success"><strong>推薦理由：</strong>${escapeHtml(s.leadAgent.reasons.join(" ｜ "))}</div>
                </div>
                <div class="p-3">
                    ${paired.length === 0 ? `<div class="text-muted small">此 Agent 目前無額外配對的 Skill / Rule / Command。</div>` : `
                        <div class="i3d-tile-grid">
                            ${paired.map((p, pIdx) => renderModuleBrick(p, true, `gh-pair-${idx}-${pIdx}-${p.comp.Id}`)).join("")}
                        </div>`}
                </div>
            </section>`;
        }).join("");

        $("additionalCandidatesContainer").innerHTML = res.additionalCandidates.length === 0 ? "" : `
            <div class="card shadow-sm border-0 rounded-4 mb-4">
                <div class="card-header bg-light py-2 d-flex justify-content-between align-items-center">
                    <span class="fw-bold small">其他候選元件（預設未勾選，共 ${res.additionalCandidates.length} 項，可手動加勾）</span>
                    <button class="btn btn-sm btn-outline-secondary" type="button" data-bs-toggle="collapse" data-bs-target="#candCollapse">展開 / 收合候選積木</button>
                </div>
                <div class="collapse" id="candCollapse">
                    <div class="card-body">
                        <div class="i3d-tile-grid">
                            ${res.additionalCandidates.map((c, cIdx) => renderModuleBrick(c, false, `gh-cand-${cIdx}-${c.comp.Id}`)).join("")}
                        </div>
                    </div>
                </div>
            </div>`;

        const updateCount = () => {
            const count = new Set(Array.from(document.querySelectorAll(".comp-chk:checked")).map(c => c.value)).size;
            $("selectedCountBadge").textContent = String(count);
            $("downloadZipBtn").disabled = count === 0;
            document.querySelectorAll(".i3d-component-card").forEach(card => {
                const cb = card.querySelector(".comp-chk");
                if (cb) card.classList.toggle("is-checked", cb.checked);
            });
        };
        document.querySelectorAll(".comp-chk").forEach(c => c.addEventListener("change", updateCount));
        document.querySelectorAll("[data-i3d-toggle]").forEach(btn => {
            btn.addEventListener("click", () => {
                const target = $(btn.getAttribute("data-i3d-toggle"));
                if (!target) return;
                const willOpen = target.classList.contains("d-none");
                target.classList.toggle("d-none", !willOpen);
                btn.setAttribute("aria-expanded", willOpen ? "true" : "false");
                const lbl = btn.querySelector("[data-i3d-toggle-label]");
                if (lbl) lbl.textContent = willOpen ? (btn.getAttribute("data-label-open") || "收合詳情") : (btn.getAttribute("data-label-closed") || "展開詳情");
            });
        });

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
            const rawUrl = c.Path && c.Path.startsWith("builtin/")
                ? `./builtin-ecc/${c.Path.slice("builtin/".length)}`
                : c.RawUrl;
            downloadTargets.push({ name: c.Name, zipPath: path, rawUrl });

            if (t === "Skill" && skillAuxPaths.length > 0 && c.Path && !c.Path.startsWith("builtin/")) {
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

    let catalogPage = 1;
    const CATALOG_PAGE_SIZE = 24;

    function switchView(viewName) {
        const valid = ["dashboard", "catalog", "wizard"].includes(viewName) ? viewName : "dashboard";
        document.querySelectorAll("[data-gh-view]").forEach(sec => {
            sec.classList.toggle("d-none", sec.getAttribute("data-gh-view") !== valid);
        });
        document.querySelectorAll("[data-gh-tab]").forEach(link => {
            link.classList.toggle("active", link.getAttribute("data-gh-tab") === valid);
        });
        if (window.location.hash !== `#${valid}`) {
            history.replaceState(null, "", `#${valid}`);
        }
        if (valid === "catalog") {
            renderCatalog();
        }
    }

    function renderDashboard() {
        const total = catalog.length;
        const counts = { Agent: 0, Skill: 0, Rule: 0, Command: 0 };
        catalog.forEach(c => {
            const t = normType(c.ComponentType);
            if (counts[t] !== undefined) counts[t]++;
        });
        const denom = Math.max(1, total);
        if ($("dashTotalBadge")) $("dashTotalBadge").textContent = String(total);
        if ($("dashReadyTitle")) $("dashReadyTitle").textContent = `已載入 ${total} 筆 ECC 元件（離線可用）`;
        if ($("dashPodTotal")) $("dashPodTotal").textContent = `${total} 筆已同步元件`;

        [["Agent", "Agent"], ["Skill", "Skill"], ["Rule", "Rule"], ["Command", "Command"]].forEach(([key, suffix]) => {
            const cntEl = $(`dashCount${suffix}`);
            const barEl = $(`dashBar${suffix}`);
            if (cntEl) cntEl.textContent = String(counts[key]);
            if (barEl) barEl.style.width = `${Math.min(100, Math.max(6, Math.round((counts[key] * 100) / denom)))}%`;
        });
    }

    function renderCatalog() {
        const grid = $("catalogGrid");
        const pag = $("catalogPagination");
        const summaryEl = $("catalogFilterSummary");
        if (!grid || !pag) return;

        const kw = ($("catKeyword")?.value || "").trim().toLowerCase();
        const typeFilter = $("catType")?.value || "";
        const roleFilter = ($("catRole")?.value || "").toLowerCase();
        const stackFilter = ($("catStack")?.value || "").toLowerCase();

        const filtered = catalog.filter(c => {
            if (typeFilter && normType(c.ComponentType) !== typeFilter) return false;
            if (roleFilter && !splitTokens(c.SdlcRoles).includes(roleFilter)) return false;
            if (stackFilter && !splitTokens(c.TechStacks).includes(stackFilter)) return false;
            if (kw) {
                const hay = `${c.Name || ""} ${c.Path || ""} ${c.SummaryZh || ""} ${c.DescriptionEn || ""}`.toLowerCase();
                if (!hay.includes(kw)) return false;
            }
            return true;
        });

        const totalPages = Math.max(1, Math.ceil(filtered.length / CATALOG_PAGE_SIZE));
        if (catalogPage > totalPages) catalogPage = totalPages;
        if (catalogPage < 1) catalogPage = 1;
        const startIdx = (catalogPage - 1) * CATALOG_PAGE_SIZE;
        const pageItems = filtered.slice(startIdx, startIdx + CATALOG_PAGE_SIZE);

        if (summaryEl) {
            summaryEl.textContent = `顯示 ${filtered.length} 筆符合元件（知識庫總計 ${catalog.length} 筆｜第 ${catalogPage} / ${totalPages} 頁）`;
        }

        if (pageItems.length === 0) {
            grid.innerHTML = `<div class="i3d-pod justify-content-center py-5 text-muted" style="grid-column: 1 / -1;">${renderIcon3D("Inactive", "md", "查無符合元件")}<span>查無符合條件的 ECC 元件，請調整篩選條件。</span></div>`;
        } else {
            grid.innerHTML = pageItems.map(item => {
                const typeName = normType(item.ComponentType);
                const typeLower = typeName.toLowerCase();
                const roles = (item.SdlcRoles || "").split(",").map(s => s.trim()).filter(Boolean);
                const stacks = (item.TechStacks || "").split(",").map(s => s.trim()).filter(Boolean);
                const summary = item.SummaryZh || item.DescriptionEn || "";
                const drawerId = `cat-drawer-${item.Id}`;
                const capsules = [
                    ...roles.slice(0, 3).map(r => `<span class="i3d-micro-capsule" title="角色：${escapeHtml(r)}">${renderIcon3D(r, "xs", r)}<span>${escapeHtml(r)}</span></span>`),
                    ...stacks.slice(0, 3).map(s => `<span class="i3d-micro-capsule" title="技術棧：${escapeHtml(s)}">${renderIcon3D(s, "xs", s)}<span>${escapeHtml(s)}</span></span>`)
                ].join("");
                const extra = roles.length + stacks.length > 6 ? `<span class="i3d-micro-capsule text-muted">+${roles.length + stacks.length - 6}</span>` : "";

                return `
                <article class="i3d-component-card i3d-type-${typeLower} i3d-hover-tilt">
                    <div class="d-flex justify-content-between align-items-start gap-2">
                        <div class="d-flex align-items-center gap-2 min-w-0">
                            ${renderIcon3D(typeName, "md", `${typeName}：${item.Name}`)}
                            <div class="min-w-0">
                                <div class="fw-bold text-dark text-truncate" title="${escapeHtml(item.Name)}">${escapeHtml(item.Name)}</div>
                                <span class="badge bg-light text-dark border small">${escapeHtml(typeName)}</span>
                            </div>
                        </div>
                        <span class="badge bg-secondary-subtle text-secondary small">唯讀</span>
                    </div>
                    <div class="i3d-summary-clamp" title="${escapeHtml(summary)}">${escapeHtml(summary)}</div>
                    <div class="i3d-capsule-row">${capsules}${extra}</div>
                    <div id="${drawerId}" class="i3d-detail-drawer d-none" data-i3d-drawer>
                        <div class="mb-1"><strong>完整摘要：</strong>${escapeHtml(summary)}</div>
                        <div class="mb-1"><strong>檔案路徑：</strong><code>${escapeHtml(item.Path)}</code></div>
                        <div class="mb-1"><strong>所有角色：</strong>${escapeHtml(roles.join(", ") || "-")}</div>
                        <div class="mb-1"><strong>所有技術棧：</strong>${escapeHtml(stacks.join(", ") || "-")}</div>
                        <div class="text-muted small fst-italic">（線上簡易版僅供唯讀瀏覽；如需編輯摘要請安裝 .NET 10 本機完整版）</div>
                    </div>
                    <div class="mt-auto pt-2 border-top d-flex justify-content-between align-items-center gap-2">
                        <button type="button" class="btn btn-sm btn-outline-secondary" data-i3d-toggle="${drawerId}" data-label-closed="展開詳情" data-label-open="收合詳情" aria-expanded="false" aria-controls="${drawerId}">
                            <span data-i3d-toggle-label>展開詳情</span>
                        </button>
                        <a href="${escapeHtml(item.RawUrl)}" target="_blank" rel="noopener" class="btn btn-sm btn-outline-dark">Raw</a>
                    </div>
                </article>`;
            }).join("");
        }

        pag.innerHTML = totalPages <= 1 ? "" : `
            <span class="small text-muted">每頁 ${CATALOG_PAGE_SIZE} 筆｜目前第 ${catalogPage} / ${totalPages} 頁</span>
            <div class="btn-group btn-group-sm">
                <button type="button" class="btn btn-outline-secondary" id="catPrevBtn" ${catalogPage <= 1 ? "disabled" : ""}>上一頁</button>
                <button type="button" class="btn btn-outline-secondary" id="catNextBtn" ${catalogPage >= totalPages ? "disabled" : ""}>下一頁</button>
            </div>`;
        if ($("catPrevBtn")) $("catPrevBtn").addEventListener("click", () => { catalogPage--; renderCatalog(); grid.scrollIntoView({ behavior: "smooth" }); });
        if ($("catNextBtn")) $("catNextBtn").addEventListener("click", () => { catalogPage++; renderCatalog(); grid.scrollIntoView({ behavior: "smooth" }); });
    }

    function initDashboardAndCatalog() {
        document.querySelectorAll("[data-gh-tab], [data-gh-goto]").forEach(el => {
            el.addEventListener("click", (e) => {
                e.preventDefault();
                const target = el.getAttribute("data-gh-tab") || el.getAttribute("data-gh-goto") || "dashboard";
                switchView(target);
            });
        });

        window.addEventListener("hashchange", () => {
            const hash = (window.location.hash || "#dashboard").replace(/^#/, "");
            switchView(hash);
        });

        document.querySelectorAll("[data-dash-type]").forEach(card => {
            const go = () => {
                const t = card.getAttribute("data-dash-type") || "";
                if ($("catKeyword")) $("catKeyword").value = "";
                if ($("catRole")) $("catRole").value = "";
                if ($("catStack")) $("catStack").value = "";
                if ($("catType")) $("catType").value = t;
                catalogPage = 1;
                switchView("catalog");
            };
            card.addEventListener("click", go);
            card.addEventListener("keydown", (e) => {
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    go();
                }
            });
        });

        ["catKeyword", "catType", "catRole", "catStack"].forEach(id => {
            const el = $(id);
            if (!el) return;
            const evt = id === "catKeyword" ? "input" : "change";
            el.addEventListener(evt, () => { catalogPage = 1; renderCatalog(); });
        });

        if ($("catResetBtn")) {
            $("catResetBtn").addEventListener("click", () => {
                if ($("catKeyword")) $("catKeyword").value = "";
                if ($("catType")) $("catType").value = "";
                if ($("catRole")) $("catRole").value = "";
                if ($("catStack")) $("catStack").value = "";
                catalogPage = 1;
                renderCatalog();
            });
        }

        const grid = $("catalogGrid");
        if (grid) {
            grid.addEventListener("click", (e) => {
                const btn = e.target.closest("[data-i3d-toggle]");
                if (!btn) return;
                const target = $(btn.getAttribute("data-i3d-toggle"));
                if (!target) return;
                const willOpen = target.classList.contains("d-none");
                target.classList.toggle("d-none", !willOpen);
                btn.setAttribute("aria-expanded", willOpen ? "true" : "false");
                const lbl = btn.querySelector("[data-i3d-toggle-label]");
                if (lbl) lbl.textContent = willOpen ? (btn.getAttribute("data-label-open") || "收合詳情") : (btn.getAttribute("data-label-closed") || "展開詳情");
            });
        }

        const initialHash = (window.location.hash || "#dashboard").replace(/^#/, "");
        switchView(initialHash);
    }

    async function init() {
        hydrateStaticIcons();
        renderCheckboxes();
        initTactileSelectors();
        applyCompatibility();
        refreshGeminiBadge();
        initDashboardAndCatalog();
        $("ProjectType").addEventListener("change", applyCompatibility);

        try {
            const res = await fetch("data/catalog.json");
            catalog = await res.json();
            $("catalogCountBadge").textContent = `已載入 ${catalog.length} 筆 ECC 元件`;
            renderDashboard();
            renderCatalog();
        } catch {
            $("catalogCountBadge").textContent = "知識庫載入失敗";
            if ($("dashReadyTitle")) $("dashReadyTitle").textContent = "知識庫載入失敗";
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
                syncAllSelectors();
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
                btn.innerHTML = `${renderIcon3D("Bundle", "xs", "產生推薦")}<span>產生精選推薦與組建 Harness</span>`;
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
