---
name: roblox-ui-patterns
description: Roblox UI engineering patterns for React-Lua / Fusion declarative state, SafeArea & UIScale responsiveness, TweenService animations, and input cleanup.
---

# Roblox UI Patterns

## Declarative UI (React-Lua / Fusion)
- Manage HUD, inventory, and shop state declaratively with **React-Lua** (`@jsdotlua/react`) or **Fusion** rather than mutating deeply nested `ScreenGui` instances imperatively.
- Keep UI state read-only projections of replicated server state; dispatch user intents to server `RemoteEvent`s and show optimistic loading states with rollback on rejection.

## Responsive Layout & Safe Area
- Enable `ScreenGui.ScreenInsets = Enum.ScreenInsets.DeviceSafeInsets` (or `CoreUISafeInsets`) so mobile notches and Roblox TopBar never clip HUD buttons.
- Combine `UIScale` driven by viewport size breakpoints (`Camera.ViewportSize`) with `UIAspectRatioConstraint`, `UIListLayout`, and `UIGridLayout` using `Scale` (`{0.5, 0}`) instead of hardcoded `Offset` pixels.
- Support Desktop, Mobile Touch, and Gamepad (`GuiService.SelectedObject` / `NextSelection*`) navigation.

## Animation & Resource Cleanup
- Use `TweenService:Create(...)` for smooth transitions and cancel active tweens before starting conflicting ones.
- Bind UI `Trove` cleaners to component unmount or `ScreenGui.Destroying` to prevent leaked `RenderStepped` / `UserInputService` listeners.
