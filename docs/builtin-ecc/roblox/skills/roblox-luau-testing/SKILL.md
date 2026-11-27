---
name: roblox-luau-testing
description: Roblox Luau testing workflow using TestEZ, Busted, Selene static analysis, StyLua formatting, and Roblox Service mocking in CI.
---

# Roblox Luau Testing Workflow

## Quality Gate Commands
Run these checks before every commit or PR:
```bash
selene src/
stylua --check src/
busted tests/
```

## Pure Logic vs Roblox Engine Separation
- Extract pure game math, economy formulas, inventory state machines, and schema migrations into `src/shared/` modules with zero `game:GetService(...)` calls at module top-level.
- Unit-test pure Luau modules directly in CI with **Busted** or **Lune** (`lune run tests/`), achieving fast millisecond feedback without launching Roblox Studio.

## Studio Integration Tests (TestEZ)
- Place `.spec.luau` files alongside modules or under `tests/studio/`.
- Use **TestEZ** `describe` / `it` / `expect` blocks for `Workspace`, `CollectionService`, and `DataStoreService` integration tests inside Roblox Studio / Open Cloud Run-Task.
- Inject mock services (e.g., `MockDataStore`, `MockRemoteEvent`) via constructor/init parameters so server modules remain testable offline.

## Coverage Targets
- 100% test coverage on currency transactions, receipt idempotency, damage/cooldown formulas, and DataStore schema migrations.
