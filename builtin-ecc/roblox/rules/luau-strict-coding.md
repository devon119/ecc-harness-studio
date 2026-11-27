---
name: luau-strict-coding
description: Enforces --!strict Luau typing, modern task.* scheduling, Trove/Maid connection cleanup, and Selene/StyLua compliance across Roblox Studio projects.
---

# Luau Strict Coding Standards

## Mandatory Directives
- Place `--!strict` at line 1 of all new `.luau` / `.lua` modules.
- Export explicit domain types with `export type` and annotate function parameters and return types.

## Task Scheduling
- **NEVER** use deprecated globals `wait()`, `spawn()`, `delay()`, or `ypcall()`.
- **ALWAYS** use `task.wait()`, `task.spawn()`, `task.defer()`, `task.delay()`, and `task.cancel()`.

## Resource & Connection Cleanup
- Track all `RBXScriptConnection`s, spawned threads, and temporary `Instance`s using `Trove` or `Maid`.
- Clean up per-player resources on `Players.PlayerRemoving` and per-instance resources on `Instance.Destroying`.
- Avoid capturing large tables or `Instance` references inside long-lived closures.

## Style & Static Analysis
- Format code with **StyLua** (`stylua.toml` with 4 spaces or tabs consistently, `call_parentheses = "Always"`).
- Lint with **Selene** (`selene.toml` targeting `roblox` standard library); fix unused variables, shadowed locals, and deprecated APIs.
- Use `Workspace:GetServerTimeNow()` for synchronized timestamps instead of `tick()`.
