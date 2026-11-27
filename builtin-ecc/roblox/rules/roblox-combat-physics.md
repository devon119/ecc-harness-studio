---
name: roblox-combat-physics
description: Roblox combat and physics rules for client-side prediction, server-side hitbox and raycast validation, SpatialQuery usage, and anti-speed/teleport checks.
---

# Roblox Combat & Physics Rules

## Client Prediction + Server Verification
- Play immediate VFX, animations, and hit-markers on the client for responsive feel, while sending action intent (`timestamp`, `aimDirection`, `targetId`) to the server.
- The server reconstructs or validates the attack using authoritative player position, weapon cooldown, line-of-sight, and range tolerances.

## Raycast & SpatialQuery Best Practices
- Use `Workspace:Raycast(origin, direction, raycastParams)` and `Workspace:GetPartBoundsInBox` / `GetPartBoundsInRadius` instead of deprecated `FindPartOnRay` or `Touched` events for melee/projectile hitboxes.
- Configure `RaycastParams.FilterType = Enum.RaycastFilterType.Exclude` and cache filter arrays where possible.
- Account for network latency with bounded lag-compensation tolerances (e.g., max 150-250ms ping window) rather than disabling distance checks.

## Movement & Anti-Exploit
- Validate player displacement over `dt` on the server when awarding positional objectives or melee hits; ignore impossible velocity spikes caused by exploits while allowing legitimate knockback/teleports flagged by the server.
- Set `BasePart:SetNetworkOwner(nil)` on server-controlled projectiles or hazards so clients cannot hijack physics.
