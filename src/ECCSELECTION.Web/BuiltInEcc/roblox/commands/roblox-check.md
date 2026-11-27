---
name: roblox-check
description: Run the complete Roblox Luau quality gate: Selene lint, StyLua format check, Busted/TestEZ unit tests, Rojo build verification, and server security audit.
---

# `/roblox-check` — Roblox Luau Quality Gate

Execute the full verification pipeline for a Roblox Studio / Luau project before committing or deploying:

## 1. Static Analysis & Formatting
```bash
selene src/
stylua --check src/
```

## 2. Unit & Logic Tests
```bash
busted tests/
```
*(If using Lune / TestEZ, run `lune run tests/` or Studio TestEZ runner.)*

## 3. Rojo Build Check
```bash
rojo build default.project.json -o /tmp/check-build.rbxl
```

## 4. Security & Strictness Audit
Verify across `src/`:
- [ ] All modules start with `--!strict`
- [ ] Zero deprecated `wait()`, `spawn()`, or `delay()` calls
- [ ] Every `RemoteEvent.OnServerEvent` validates `typeof`, numeric bounds (`NaN`/`inf`), and per-player rate limits
- [ ] DataStore writes use `UpdateAsync` + `BindToClose`
- [ ] All `RBXScriptConnection`s are managed via `Trove` / `Maid` or disconnected on `Destroying` / `PlayerRemoving`
