---
name: roblox-monetization-patterns
description: Roblox MarketplaceService monetization rules covering idempotent ProcessReceipt handling, GamePass caching, Developer Product fulfillment, and audit logging.
---

# Roblox Monetization Patterns

## Idempotent `ProcessReceipt`
- Assign `MarketplaceService.ProcessReceipt` **once** in a single authoritative server module.
- Check whether `receiptInfo.PurchaseId` has already been processed in the player's DataStore receipt history before granting currency or items.
- Grant rewards and record `PurchaseId` atomically inside `DataStore:UpdateAsync`; only return `Enum.ProductPurchaseDecision.PurchaseGranted` **after** the DataStore save succeeds.
- If the player is offline or DataStore fails, return `Enum.ProductPurchaseDecision.NotProcessedYet` so Roblox retries safely.

## GamePass & Entitlement Caching
- Cache `MarketplaceService:UserOwnsGamePassAsync(userId, passId)` results per session in server memory and invalidate/refresh on `PromptGamePassPurchaseFinished`.
- Wrap `UserOwnsGamePassAsync` in `pcall` with retry/backoff; never crash player join if Roblox's API is temporarily unavailable.

## Audit & Compliance
- Log all product grants (`UserId`, `ProductId`, `PurchaseId`, `CurrencySpent`) for support diagnostics.
- Never allow client-triggered remotes to grant paid items without server `ProcessReceipt` or `UserOwnsGamePassAsync` verification.
