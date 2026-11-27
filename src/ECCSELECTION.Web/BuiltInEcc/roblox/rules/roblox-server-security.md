---
name: roblox-server-security
description: Zero-trust Roblox server security rules covering RemoteEvent/RemoteFunction payload validation, rate limiting, anti-exploit sanity checks, and DataStore concurrency locks.
---

# Roblox Server Security Rules

## Never Trust the Client
- Treat every argument arriving via `RemoteEvent.OnServerEvent(player, ...)` or `RemoteFunction.OnServerInvoke(player, ...)` as untrusted attacker input.
- Never let the client specify damage amounts, currency deltas, item grants, or authoritative cooldowns.

## Input Validation & Sanitization
- Validate every parameter's `typeof(arg)` before use.
- For numbers, reject `NaN` (`arg ~= arg`), `math.huge`, `-math.huge`, and out-of-range values with `math.clamp` or early `return`.
- For strings, enforce maximum byte length (`#str <= MAX_LEN`) and whitelist allowed characters/IDs.
- For `Instance` references, verify `arg:IsDescendantOf(workspace)` or expected container and check ownership/proximity.

## Rate Limiting & Anti-Spam
- Enforce per-player token-bucket or timestamp cooldowns on every `RemoteEvent` handler.
- Drop or log excessive bursts without yielding the server thread.
- Prefer `RemoteEvent` over `RemoteFunction` for Client-to-Server and Server-to-Client calls to avoid server thread hangs on client disconnects.

## State & Persistence Integrity
- Guard critical economy/inventory mutations with per-player mutex/session locks.
- Use `DataStoreService` `UpdateAsync` with atomic version/session checks.
