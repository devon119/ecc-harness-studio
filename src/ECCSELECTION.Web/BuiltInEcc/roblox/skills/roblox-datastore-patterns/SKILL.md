---
name: roblox-datastore-patterns
description: Production Roblox DataStore patterns using UpdateAsync, exponential retry backoff, session locking, BindToClose flushing, and schema migration.
---

# Roblox DataStore Patterns

## Core Principles
- **Use `UpdateAsync`, never `SetAsync`** for player profile saves so concurrent server hops cannot silently overwrite newer data.
- **Session Locking**: Store `{ SessionJobId = game.JobId, LockedAt = os.time() }` inside the profile metadata; if another active server holds a fresh lock (< 30 min), retry with backoff or kick gracefully to prevent item duplication.
- **Consider ProfileStore / ProfileService** via Wally for battle-tested session locking unless custom DataStore logic is required.

## Retry & Backoff Template
```luau
--!strict
local MAX_RETRIES = 5

local function retryDataStore<T>(operation: () -> T): (boolean, T | string)
    local delaySeconds = 1
    for attempt = 1, MAX_RETRIES do
        local ok, result = pcall(operation)
        if ok then
            return true, result
        end
        if attempt < MAX_RETRIES then
            task.wait(delaySeconds)
            delaySeconds *= 2
        else
            return false, tostring(result)
        end
    end
    return false, "Exhausted retries"
end
```

## Safe Shutdown (`BindToClose`)
- Register `game:BindToClose` to release session locks and flush dirty profiles for all connected players in parallel (`task.spawn` + completion counter), keeping total shutdown within Roblox's 30-second budget.
- In Studio (`RunService:IsStudio()`), use a short wait or skip DataStore calls when API Services are disabled.

## Schema Migration
- Include `SchemaVersion: number` in every profile; run pure, idempotent migration functions (`v1 -> v2 -> v3`) immediately after `UpdateAsync` loads data and before any gameplay system reads it.
