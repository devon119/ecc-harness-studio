---
name: roblox-ecs-patterns
description: Roblox Entity-Component-System patterns using CollectionService tags, ReplicatedStorage/ServerScriptService separation, and deterministic lifecycle management.
---

# Roblox ECS & CollectionService Patterns

## Tag-Driven Entities (`CollectionService`)
- Model interactive world objects (doors, hazards, pickups, NPCs, turrets) using `CollectionService` tags rather than embedding duplicate `Script`s inside every `Model`.
- Tag prefixes: `Server_*` for server-authoritative behavior, `Client_*` for local VFX/audio/UI, `Shared_*` for replicated state.

## Lifecycle Binding
- Bind systems via `CollectionService:GetInstanceAddedSignal(tag)` + iterating `CollectionService:GetTagged(tag)` on startup.
- Always guard `GetInstanceAddedSignal` against instances outside `Workspace` (e.g., `ReplicatedStorage` templates) by checking `instance:IsDescendantOf(workspace)`.
- Tear down entity state and `Trove` cleaners on `CollectionService:GetInstanceRemovedSignal(tag)` and `instance.Destroying`.

## State & Component Storage
- Store server-only component data in module-scoped `[Instance]: ComponentState` weak/cleaned tables inside `ServerScriptService`.
- Replicate only minimal public state to clients via `Instance:SetAttribute()` or compact `RemoteEvent` snapshots.
- Keep `ReplicatedStorage` systems pure and free of `ServerScriptService` dependencies.
