# NôFarma Cash Shifts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver offline opening, operation and closing of cash shifts as the required foundation for point-of-sale transactions.

**Architecture:** The local installation device is the cash point for the first release. A domain aggregate enforces lifecycle and integer-XOF rules. An application service enforces permissions and stock-operation licensing policy. SQLite stores each command with audit and outbox data in one immediate transaction. The WinUI screen follows the approved cash panel in `docs/design/previews/02-dashboard-sales-invoices-cash.png`.

**Tech Stack:** .NET 10, C# 14, WinUI 3, EF Core 10, SQLite, xUnit and Microsoft.Extensions.DependencyInjection.

## Global Constraints

- Cash operations must work without internet access.
- One open shift is allowed per pharmacy and installation device.
- Opening cash, movements, expected cash, counted cash and difference use integer XOF.
- Negative opening and counted values are rejected.
- Manual entries and exits require a reason with at most 500 characters.
- Each command uses a pharmacy-scoped idempotency key with at most 160 characters.
- Cash shift state, audit and outbox event commit in one SQLite transaction.
- Cashiers receive no purchase cost, margin, supplier or payment credential data.
- The interface remains usable at 1366 by 768 with visible focus and controls at least 44 pixels high.
- The first release treats one installation device as one cash point. Multiple physical drawers on the same computer require a future `CashRegister` entity.

---

### Task 1: Cash shift domain

**Files:**
- Create: `src/Nofarma.Domain/Sales/CashShiftStatus.cs`
- Create: `src/Nofarma.Domain/Sales/CashMovementType.cs`
- Create: `src/Nofarma.Domain/Sales/CashMovement.cs`
- Create: `src/Nofarma.Domain/Sales/CashShift.cs`
- Create: `src/Nofarma.Domain/Sales/SalesValidationException.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Sales/CashShiftTests.cs`

**Interfaces:**
- Consumes: `EntityId`, `Money` and `UtcInstant`.
- Produces: `CashShift.Open(...)`, `RecordMovement(...)`, `Close(...)`, `ExpectedCash` and `Difference`.

- [x] **Step 1: Write failing lifecycle and money tests**

```csharp
CashShift shift = CashShift.Open(
    shiftId,
    pharmacyId,
    deviceId,
    userId,
    Money.Xof(50_000),
    openedAt);
shift.RecordMovement(saleMovementId, CashMovementType.Sale, Money.Xof(12_500), saleId, "Venda", occurredAt);
shift.RecordMovement(entryId, CashMovementType.ManualEntry, Money.Xof(2_000), null, "Reforço", occurredAt);
shift.RecordMovement(exitId, CashMovementType.ManualExit, Money.Xof(500), null, "Transporte", occurredAt);
shift.Close(Money.Xof(64_000), closedAt);
Assert.Equal(64_000, shift.ExpectedCash.Amount);
Assert.Equal(0, shift.Difference!.Value.Amount);
```

Add individual tests proving that invalid identifiers, negative opening cash, zero movement, negative movement, missing or oversized reason, sale movement without source sale, movement after close, negative counted cash, overflow and second close throw `SalesValidationException`. Prove that the exposed movement collection cannot mutate the aggregate.

- [x] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~CashShiftTests"`

Expected: compilation fails because `Nofarma.Domain.Sales` does not exist.

- [x] **Step 3: Implement the minimal aggregate**

```csharp
public static CashShift Open(
    EntityId id,
    EntityId pharmacyId,
    EntityId deviceId,
    EntityId userId,
    Money openingCash,
    UtcInstant openedAt);

public CashMovement RecordMovement(
    EntityId movementId,
    CashMovementType type,
    Money amount,
    EntityId? sourceSaleId,
    string reason,
    UtcInstant occurredAt);

public void Close(Money countedCash, UtcInstant closedAt);
```

`Sale` and `ManualEntry` add to expected cash. `ManualExit` and `Refund` subtract. The close difference equals counted cash minus expected cash.

- [x] **Step 4: Run focused and full unit tests**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --configuration Release`

Expected: all tests pass.

- [x] **Step 5: Commit**

```powershell
git add src/Nofarma.Domain/Sales tests/Nofarma.UnitTests/Domain/Sales docs/superpowers/plans/2026-07-28-nofarma-cash-shifts.md
git commit -m "feat: add cash shift domain"
```

### Task 2: Cash shift application service

**Files:**
- Create: `src/Nofarma.Application/Abstractions/ICashShiftStore.cs`
- Create: `src/Nofarma.Application/Sales/CashShiftDtos.cs`
- Create: `src/Nofarma.Application/Sales/CashShiftRequests.cs`
- Create: `src/Nofarma.Application/Sales/CashCommandDtos.cs`
- Create: `src/Nofarma.Application/Sales/CashCommandFingerprint.cs`
- Create: `src/Nofarma.Application/Sales/CashShiftConflictException.cs`
- Create: `src/Nofarma.Application/Sales/CashShiftConcurrencyException.cs`
- Create: `src/Nofarma.Application/Sales/CashShiftOperationBlockedException.cs`
- Create: `src/Nofarma.Application/Sales/CashShiftService.cs`
- Test: `tests/Nofarma.UnitTests/Application/Sales/CashShiftServiceTests.cs`

**Interfaces:**
- Consumes: `AuthorizationService`, `IUtcClock`, `IStockOperationPolicy` and `ICashShiftStore`.
- Produces: `OpenAsync`, `GetCurrentAsync`, `RecordManualMovementAsync` and `CloseAsync`.

- [x] **Step 1: Write failing service tests**

```csharp
CashShiftSummary result = await service.OpenAsync(
    cashierSession,
    new OpenCashShiftRequest(50_000, "open:T1:20260728"),
    cancellationToken);
Assert.Equal(50_000, result.OpeningCashXof);
Assert.Equal(CashShiftStatus.Open, result.Status);
```

Add separate tests for missing `ManageCashShift`, blocked opening and manual movement policy, allowed consultation and closing after the policy becomes blocked, a second open shift, blank or oversized idempotency key, missing manual reason, duplicate idempotency returning the stored result, concurrent identical retries and reuse of the same key with a different request fingerprint raising `CashShiftConflictException`.

- [x] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~CashShiftServiceTests"`

Expected: compilation fails because `CashShiftService` and `ICashShiftStore` do not exist.

- [x] **Step 3: Implement contracts and service**

```csharp
public interface ICashShiftStore
{
    Task<CashShiftActorContext?> GetActorContextAsync(EntityId userId, CancellationToken cancellationToken);
    Task<StoredCashShift?> GetCurrentAggregateAsync(EntityId pharmacyId, EntityId deviceId, CancellationToken cancellationToken);
    Task<CashCommandResult?> GetCommandResultAsync(EntityId pharmacyId, string idempotencyKey, CancellationToken cancellationToken);
    Task<CashShiftSummary> SaveOpenedAsync(CashShiftActorContext context, CashShift shift, CashCommandEnvelope command, AuditEvent audit, CancellationToken cancellationToken);
    Task<CashShiftSummary> SaveMovementAsync(CashShiftActorContext context, CashShift shift, CashMovement movement, long expectedVersion, CashCommandEnvelope command, AuditEvent audit, CancellationToken cancellationToken);
    Task<CashShiftSummary> SaveClosedAsync(CashShiftActorContext context, CashShift shift, long expectedVersion, CashCommandEnvelope command, AuditEvent audit, CancellationToken cancellationToken);
}
```

`StoredCashShift` contains the reconstructed aggregate and its persistence version. The service loads it, invokes `RecordMovement` or `Close` and gives the validated aggregate plus `expectedVersion` to the store. On a concurrency conflict, the service reloads, reapplies the same command and retries at most three times. The SQLite store reconstructs an aggregate by calling `Open`, replaying persisted movements through `RecordMovement` and, when applicable, calling `Close`.

`CashCommandEnvelope` contains the normalised key, operation type and a SHA-256 fingerprint of a canonical request containing the pharmacy and device identifiers, amounts, movement type and normalised reason. An identical duplicate returns the stored immutable result snapshot. The same key with a different fingerprint raises `CashShiftConflictException`, including reuse by another device in the same pharmacy.

After every concurrency conflict, including the final attempt and opening, the service rechecks the idempotent result before propagating the conflict. Movement and closing retries calculate an effective timestamp that is not earlier than the last activity in the reloaded aggregate.

Every public operation calls `authorization.EnsureAllowed(actor, Capability.ManageCashShift)`. Opening and manual movements also validate `IStockOperationPolicy`. Consultation and safe closing of an existing shift remain available when the policy later becomes blocked, so licensing cannot trap unreconciled cash.

- [x] **Step 4: Run focused and full unit tests**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --configuration Release`

- [x] **Step 5: Commit**

```powershell
git add src/Nofarma.Application tests/Nofarma.UnitTests/Application/Sales
git commit -m "feat: add cash shift use cases"
```

### Task 3: Atomic SQLite persistence

**Files:**
- Create: `src/Nofarma.Infrastructure/Persistence/Records/CashShiftRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/CashMovementRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/CashCommandRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/OutboxEventRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/CashShiftConfigurations.cs`
- Modify: `src/Nofarma.Infrastructure/Persistence/Configurations/DeviceConfiguration.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/SqliteCashShiftStore.cs`
- Modify: `src/Nofarma.Infrastructure/Persistence/NofarmaDbContext.cs`
- Modify: `src/Nofarma.Infrastructure/Composition/ServiceCollectionExtensions.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Migrations/20260728120000_AddCashShifts.cs` using EF Core tooling and normalise the generated timestamp to this planned name if required
- Create: `src/Nofarma.Infrastructure/Persistence/Migrations/20260728120000_AddCashShifts.Designer.cs` using EF Core tooling and normalise the generated timestamp to this planned name if required
- Modify: `src/Nofarma.Infrastructure/Persistence/Migrations/NofarmaDbContextModelSnapshot.cs` using EF Core tooling
- Test: `tests/Nofarma.IntegrationTests/Sales/CashShiftTransactionTests.cs`

**Interfaces:**
- Consumes: `ICashShiftStore`, `CashShift` and `CashMovement`.
- Produces: durable, idempotent and pharmacy-scoped cash commands.

- [x] **Step 1: Write failing integration tests**

Open two different devices successfully. Reject a second open shift on the same pharmacy and device. Return the original immutable result snapshot for an identical duplicate idempotency key. Reject reuse of the key with a different fingerprint. Run concurrent identical requests and prove that one command result is stored. Run two different concurrent movements against the same shift and prove that both movements and their combined expected cash are preserved. Force an audit database failure and assert that no shift, command or outbox row remains. Verify audit actor, metadata and timestamp, command-movement type coherence, device and actor pharmacy membership, active actor status, outbox privacy, equal-timestamp movement order, closing and reopening and append-only cash commands and movements. Upgrade a database from the previous migration and preserve its installation data.

- [x] **Step 2: Run the focused integration tests and verify RED**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~CashShiftTransactionTests"`

- [x] **Step 3: Implement records, constraints and immediate transactions**

Create a filtered unique index on `(PharmacyId, DeviceId)` where `Status = 1`. Remove the previous unique constraint on `Devices.PharmacyId`, because a pharmacy can have several devices while the local installation remains singular. Add `RowVersion` to `CashShiftRecord`. Persist each movement with the resulting aggregate version as a unique per-shift sequence, so equal timestamps cannot invert valid operations during reconstruction. Movement and close updates use compare-and-swap with `WHERE Id = shiftId AND RowVersion = expectedVersion`, update the monetary state and increment the version. Zero affected rows raise `CashShiftConcurrencyException`, allowing the service to reload and retry without losing either command. `CashCommandRecord` stores pharmacy, key, operation type, request fingerprint, shift identifier and an immutable result snapshot. Create a unique index on `(PharmacyId, IdempotencyKey)`. Limit keys to 160 characters and movement reasons to 500 characters in both validation and EF configuration. Use `SqliteConnection.BeginTransaction(deferred: false)` and attach it with `Database.UseTransaction`. Validate the persisted pharmacy, device and active actor context. Store outbox payloads containing operational identifiers, amounts and timestamps only, without user identifiers or free text.

- [x] **Step 4: Generate the migration and run integration tests**

Run: `dotnet ef migrations add AddCashShifts --project src/Nofarma.Infrastructure --startup-project src/Nofarma.Infrastructure --context NofarmaDbContext`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --configuration Release`

- [x] **Step 5: Commit**

```powershell
git add src/Nofarma.Infrastructure tests/Nofarma.IntegrationTests/Sales
git commit -m "feat: persist cash shifts atomically"
```

### Task 4: Native Cash screen

**Files:**
- Create: `src/Nofarma.Desktop/ViewModels/CashViewModel.cs`
- Create: `src/Nofarma.Desktop/Views/CashPage.xaml`
- Create: `src/Nofarma.Desktop/Views/CashPage.xaml.cs`
- Modify: `src/Nofarma.Desktop/Views/AppShellPage.xaml.cs`
- Modify: `src/Nofarma.Desktop/Composition/ServiceCollectionExtensions.cs`
- Test: `tests/Nofarma.UnitTests/Desktop/CashViewModelTests.cs`

**Interfaces:**
- Consumes: `CashShiftService`.
- Produces: opening, current-shift and closing states at the `Caixa` navigation destination.

- [ ] **Step 1: Write failing view-model tests**

Prove the initial loading state, honest no-shift state, opening amount validation, duplicate-submit lock, manual entry/exit validation, expected cash display, close difference preview and reset after successful close.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~CashViewModelTests"`

- [ ] **Step 3: Implement the approved native interface**

Use one opening panel when no shift exists. With an open shift, show opening cash, entries, exits and expected cash plus a closing panel with counted cash and difference. Use explicit text with semantic colour for differences. Assign automation names to amount, reason and action controls. Bind `F2` to opening and `F4` to closing without hiding the labelled buttons.

- [ ] **Step 4: Verify tests and the 1366 by 768 interface**

Run all unit tests. Launch the application, complete login manually and inspect no-shift, open-shift and validation states. Do not insert sample totals as operational data.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Desktop tests/Nofarma.UnitTests/Desktop
git commit -m "feat: add cash shift desktop page"
```

### Task 5: Documentation and release gate

**Files:**
- Create: `docs/development/cash-shifts.md`
- Modify: `docs/development/verification.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: implemented cash-shift behaviour.
- Produces: operator and developer guidance without credentials or invented values.

- [ ] **Step 1: Document lifecycle and recovery rules**

Document opening, manual entry/exit, closing, difference calculation, offline persistence, idempotency and the one-device-one-cash-point limitation.

- [ ] **Step 2: Run the release gate**

```powershell
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror
dotnet test Nofarma.slnx --configuration Release --no-build
dotnet list Nofarma.slnx package --vulnerable --include-transitive
git diff --check
```

- [ ] **Step 3: Review staged content for secrets**

Reject private keys, access tokens, API keys, real passwords, real PINs and full payment identifiers. Configuration examples use empty variables only.

- [ ] **Step 4: Commit**

```powershell
git add README.md docs/development
git commit -m "docs: verify offline cash shifts"
```

## Next independent plan

Point of sale, registered payment methods, suspended sales, atomic FEFO stock exits and internal receipt simulation follow in `2026-07-28-nofarma-point-of-sale.md` after the cash-shift service is available. DGCI fiscal series, official invoices, QR, PDF/A4, credit notes and hardware ESC/POS validation remain a third independent plan.
