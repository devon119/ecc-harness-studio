---
name: roblox-rojo-wally-workflow
description: Roblox Rojo project mapping, Wally package management, Aftman/Rokit toolchain pinning, and CI/CD Open Cloud deployment workflow.
---

# Rojo, Wally & Toolchain Workflow

## Toolchain Pinning (`aftman.toml` / `rokit.toml`)
Pin exact versions of all Roblox CLI tools so local dev and CI stay identical:
- `rojo` (sync filesystem <-> Roblox Studio `.rbxl`)
- `wally` (Luau package manager)
- `selene` (Luau linter)
- `stylua` (Luau formatter)
- `lune` (optional Luau runtime for headless tests/build scripts)

## `default.project.json` Structure
```json
{
  "name": "RobloxGame",
  "tree": {
    "$className": "DataModel",
    "ReplicatedStorage": {
      "Shared": { "$path": "src/shared" },
      "Packages": { "$path": "Packages" }
    },
    "ServerScriptService": {
      "Server": { "$path": "src/server" },
      "ServerPackages": { "$path": "ServerPackages" }
    },
    "StarterPlayer": {
      "StarterPlayerScripts": {
        "Client": { "$path": "src/client" }
      }
    }
  }
}
```

## Wally Package Workflow
1. Declare dependencies in `wally.toml` (`[dependencies]` for shared/client, `[server-dependencies]` for server-only like `ProfileStore` or `Cmdr`).
2. Run `wally install` and commit `wally.lock` (never commit `Packages/` if generated in CI, or generate sourcemap with `rojo sourcemap default.project.json -o sourcemap.json`).

## CI/CD Pipeline
1. `selene src/` && `stylua --check src/`
2. `rojo build default.project.json -o build.rbxl`
3. Run headless tests (`busted tests/` or `lune run tests/`) and publish `.rbxl` via Roblox Open Cloud Place Publishing API on tagged releases.
