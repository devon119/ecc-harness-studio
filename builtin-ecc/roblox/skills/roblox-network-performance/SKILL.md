---
name: roblox-network-performance
description: Roblox networking and performance optimization covering StreamingEnabled, UnreliableRemoteEvent bandwidth reduction, ScriptProfiler, and memory leak diagnostics.
---

# Roblox Network & Performance Optimization

## Bandwidth & Replication
- Use `UnreliableRemoteEvent` for high-frequency, non-critical updates (aim direction, footstep VFX, cosmetic projectiles) and reserve `RemoteEvent` for state transitions and economy events.
- Batch state updates into compact buffers (`buffer` library) or delta tables instead of firing dozens of individual remotes per frame.
- Minimize `Instance:SetAttribute()` churn on `Heartbeat`—only update attributes when values meaningfully change.

## StreamingEnabled Compatibility
- Design client scripts assuming `Workspace.StreamingEnabled = true`:
  - Never index `workspace.Map.BossDoor` directly at startup without `WaitForChild` or `CollectionService` tag listeners.
  - Set `Model.ModelStreamingMode = Enum.ModelStreamingMode.Atomic` only on small interactive models that require all descendants present together.
  - Handle `CollectionService:GetInstanceAddedSignal` / `GetInstanceRemovedSignal` idempotently as parts stream in and out.

## Profiling & Memory Leaks
- Monitor server and client frame budgets (< 16.6 ms at 60 FPS) using Roblox Studio **MicroProfiler** and **ScriptProfiler**.
- Check `Stats:GetTotalMemoryUsageMb()` and `LuaHeap` growth over 10+ minutes; common culprits are uncleaned `RBXScriptConnection`s, `Players` tables not cleared on `PlayerRemoving`, and orphaned `Tween`s.
