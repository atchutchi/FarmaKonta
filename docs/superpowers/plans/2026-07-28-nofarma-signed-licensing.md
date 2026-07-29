# NôFarma Signed Licensing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar activação e renovação offline por licença ECDSA vinculada à farmácia e ao dispositivo, com canais QA e comercial separados, sete dias de tolerância e modo de consulta após expiração.

**Architecture:** O domínio calcula estados temporais sem conhecer ficheiros ou criptografia. A aplicação coordena pedido, validação, importação e política de operações através de interfaces pequenas. A infraestrutura fornece serialização canónica, ECDSA P-256, DPAPI, SQLite transaccional e registo de chaves públicas por canal. O Desktop apresenta o estado e importa ficheiros sem conter chaves privadas.

**Tech Stack:** .NET 10, C# 14, WinUI 3, EF Core 10, SQLite, `System.Security.Cryptography` ECDSA P-256/SHA-256, DPAPI CurrentUser, xUnit v3.

## Global Constraints

- Windows suportado: Windows 10 22H2 de 64 bits e Windows 11.
- A chave privada comercial nunca existe no repositório, Desktop, instalador ou artefacto publicado.
- O canal comercial nunca aceita uma licença ou chave pública QA.
- O canal QA mostra permanentemente `Modo QA` e usa uma pasta local própria.
- O build normal sem canal explícito fica `Unlicensed` e não activa operações.
- Planos suportados: `Monthly` e `Annual`.
- No piloto, `EstablishmentId` é igual a `PharmacyId` até existir um modelo separado de estabelecimentos.
- Tolerância: exactamente sete dias depois de `ValidUntilUtc`.
- Os 90 dias offline não prolongam a subscrição.
- Depois da tolerância, consulta, exportação, reimpressão e fecho seguro continuam disponíveis.
- Não existe activação por edição de SQLite, variável de ambiente, argumento de linha de comandos ou código secreto.
- Formato de ficheiro: `.nofarma-license`; pedido: `.nofarma-request`.
- Limite máximo de cada documento: 64 KiB; profundidade JSON máxima: 16.
- Criptografia de assinatura: ECDSA P-256 com SHA-256 e assinatura IEEE P1363 de 64 bytes.
- Datas são UTC e os limites temporais são inclusivos: `now <= ValidUntilUtc` é válido; `now <= GraceUntilUtc` é tolerância.
- Uma importação inválida nunca substitui a licença actual.
- Nenhum teste ou documento contém palavras-passe, PINs, códigos de recuperação, licenças reais ou chaves privadas.

---

## Mapa de ficheiros

### Domínio

- `src/Nofarma.Domain/Licensing/LicensePlan.cs`: planos aceites.
- `src/Nofarma.Domain/Licensing/LicenseState.cs`: estados calculados.
- `src/Nofarma.Domain/Licensing/LicenseGrant.cs`: concessão assinada já normalizada.
- `src/Nofarma.Domain/Licensing/LicenseEvaluation.cs`: estado e capacidades resultantes.
- `src/Nofarma.Domain/Licensing/LicenseEvaluator.cs`: fronteiras de validade e tolerância.

### Aplicação

- `src/Nofarma.Application/Licensing/LicenseDtos.cs`: pedidos e respostas sem detalhes de persistência.
- `src/Nofarma.Application/Licensing/LicenseExceptions.cs`: erros estáveis para a interface.
- `src/Nofarma.Application/Licensing/LicenseService.cs`: consulta, pedido, importação e renovação.
- `src/Nofarma.Application/Abstractions/ILicenseStore.cs`: persistência transaccional.
- `src/Nofarma.Application/Abstractions/ILicenseContextStore.cs`: contexto local de farmácia, estabelecimento e dispositivo.
- `src/Nofarma.Application/Abstractions/ILicenseDocumentVerifier.cs`: parse e verificação criptográfica.
- `src/Nofarma.Application/Abstractions/IDeviceLicenseIdentityStore.cs`: identidade DPAPI do dispositivo.
- `src/Nofarma.Application/Abstractions/ILicenseClockCheckpoint.cs`: detecção de recuo temporal.
- `src/Nofarma.Application/Abstractions/ILicensedOperationPolicy.cs`: gate comum de operações.

### Infraestrutura

- `src/Nofarma.Infrastructure/Licensing/CanonicalLicenseJson.cs`: bytes canónicos.
- `src/Nofarma.Infrastructure/Licensing/EcdsaLicenseDocumentVerifier.cs`: ECDSA e limites do documento.
- `src/Nofarma.Infrastructure/Licensing/TrustedLicenseKeyRegistry.cs`: chaves públicas compiladas por canal.
- `src/Nofarma.Infrastructure/Licensing/WindowsDeviceLicenseIdentityStore.cs`: chave local DPAPI.
- `src/Nofarma.Infrastructure/Licensing/WindowsLicenseClockCheckpoint.cs`: marcador temporal DPAPI.
- `src/Nofarma.Infrastructure/Licensing/SqliteLicenseStore.cs`: licença, renovação, auditoria e instalação.
- `src/Nofarma.Infrastructure/Licensing/SqliteLicenseContextStore.cs`: IDs locais necessários à activação.
- `src/Nofarma.Infrastructure/Licensing/LicenseOperationPolicy.cs`: política runtime real.
- `src/Nofarma.Infrastructure/Persistence/Records/LicenseRecord.cs`: registo persistido.
- `src/Nofarma.Infrastructure/Persistence/Configurations/LicenseConfiguration.cs`: índices e limites.
- `src/Nofarma.Infrastructure/Persistence/Migrations/*_AddSignedLicensing.cs`: migração.

### Desktop

- `src/Nofarma.Desktop/ViewModels/LicenseViewModel.cs`: estado, importação e pedido.
- `src/Nofarma.Desktop/Views/LicensePage.xaml`: página nativa.
- `src/Nofarma.Desktop/Views/LicensePage.xaml.cs`: selecção e gravação de ficheiros.
- `src/Nofarma.Desktop/Services/LicensePageOperations.cs`: adaptador testável.
- `src/Nofarma.Desktop/Views/AppShellPage.xaml`: estado curto e marca QA.
- `src/Nofarma.Desktop/Views/AppShellPage.xaml.cs`: navegação e actualização do estado.
- `src/Nofarma.Desktop/Nofarma.Desktop.csproj`: canal de compilação e exclusão de chaves QA.

### Ferramenta QA

- `tools/Nofarma.Licensing.Qa/Nofarma.Licensing.Qa.csproj`: consola não distribuível.
- `tools/Nofarma.Licensing.Qa/Program.cs`: `provision` e `issue`.
- `tools/Nofarma.Licensing.Qa/QaKeyStore.cs`: chave privada QA protegida por DPAPI.
- `build/keys/nofarma-qa-public.spki.b64`: chave pública QA segura para Git.

---

### Task 1: Modelo de domínio e fronteiras temporais

**Files:**
- Create: `src/Nofarma.Domain/Licensing/LicensePlan.cs`
- Create: `src/Nofarma.Domain/Licensing/LicenseState.cs`
- Create: `src/Nofarma.Domain/Licensing/LicenseGrant.cs`
- Create: `src/Nofarma.Domain/Licensing/LicenseEvaluation.cs`
- Create: `src/Nofarma.Domain/Licensing/LicenseEvaluator.cs`
- Create: `tests/Nofarma.UnitTests/Domain/Licensing/LicenseEvaluatorTests.cs`
- Create: `tests/Nofarma.UnitTests/TestSupport/Licensing/LicenseTestData.cs`

**Interfaces:**
- Consumes: `UtcInstant`, `EntityId`.
- Produces: `LicenseEvaluator.Evaluate(LicenseGrant grant, UtcInstant now, bool clockRollback) -> LicenseEvaluation`.

- [ ] **Step 1: Escrever testes falhados para todas as fronteiras**

```csharp
[Theory]
[InlineData("2026-08-01T00:00:00Z", LicenseState.Valid)]
[InlineData("2026-08-31T23:59:59Z", LicenseState.Valid)]
[InlineData("2026-09-01T00:00:00Z", LicenseState.Grace)]
[InlineData("2026-09-07T23:59:59Z", LicenseState.Grace)]
[InlineData("2026-09-08T00:00:00Z", LicenseState.ExpiredReadOnly)]
public void EvaluatesInclusiveValidityAndSevenDayGrace(string now, LicenseState expected)
{
    LicenseGrant grant = LicenseTestData.Monthly(
        validFrom: "2026-08-01T00:00:00Z",
        validUntil: "2026-08-31T23:59:59Z",
        graceUntil: "2026-09-07T23:59:59Z");
    Assert.Equal(expected, LicenseEvaluator.Evaluate(grant, LicenseTestData.Instant(now), false).State);
}

[Fact]
public void ClockRollbackOverridesOtherwiseValidGrant()
{
    LicenseEvaluation result = LicenseEvaluator.Evaluate(
        LicenseTestData.Monthly("2026-08-01T00:00:00Z", "2026-08-31T23:59:59Z", "2026-09-07T23:59:59Z"),
        LicenseTestData.Instant("2026-08-10T12:00:00Z"),
        clockRollback: true);
    Assert.Equal(LicenseState.ClockRollback, result.State);
    Assert.False(result.AllowsNewOperations);
    Assert.True(result.AllowsReadOnlyAccess);
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~LicenseEvaluatorTests`

Expected: FAIL por tipos `Nofarma.Domain.Licensing` inexistentes.

- [ ] **Step 3: Implementar o modelo mínimo**

```csharp
public enum LicensePlan { Monthly = 1, Annual = 2 }
public enum LicenseState
{
    Missing = 0, Invalid = 1, NotYetValid = 2, Valid = 3,
    Grace = 4, ExpiredReadOnly = 5, ClockRollback = 6
}

public sealed record LicenseEvaluation(
    LicenseState State,
    bool AllowsNewOperations,
    bool AllowsReadOnlyAccess);

public static class LicenseEvaluator
{
    public static LicenseEvaluation Evaluate(LicenseGrant grant, UtcInstant now, bool clockRollback)
    {
        if (clockRollback) return new(LicenseState.ClockRollback, false, true);
        if (now < grant.ValidFromUtc) return new(LicenseState.NotYetValid, false, true);
        if (now <= grant.ValidUntilUtc) return new(LicenseState.Valid, true, true);
        if (now <= grant.GraceUntilUtc) return new(LicenseState.Grace, true, true);
        return new(LicenseState.ExpiredReadOnly, false, true);
    }
}
```

Acrescentar uma teoria equivalente para `Annual` com início `2026-01-01T00:00:00Z`, fim `2026-12-31T23:59:59Z` e tolerância até `2027-01-07T23:59:59Z`.

`LicenseGrant` valida IDs não vazios, `EstablishmentId == PharmacyId`, `Sequence >= 1`, `IssuedAtUtc <= ValidUntilUtc`, `ValidFromUtc <= ValidUntilUtc` e `GraceUntilUtc` exactamente sete dias depois de `ValidUntilUtc`.

O suporte de testes tem estes métodos exactos e usa GUIDs literais constantes para farmácia, estabelecimento, dispositivo e licença:

```csharp
internal static LicenseGrant Monthly(string from, string until, string grace);
internal static LicenseGrant Active(long sequence);
internal static StoredLicense StoredActive(long sequence);
internal static UtcInstant Instant(string value);
```

- [ ] **Step 4: Executar GREEN e suite de domínio**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Domain"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Domain/Licensing tests/Nofarma.UnitTests/Domain/Licensing
git commit -m "feat: model signed licence states"
```

### Task 2: Contratos de aplicação e serviço de licença

**Files:**
- Create: `src/Nofarma.Application/Licensing/LicenseDtos.cs`
- Create: `src/Nofarma.Application/Licensing/LicenseExceptions.cs`
- Create: `src/Nofarma.Application/Licensing/LicenseService.cs`
- Create: `src/Nofarma.Application/Abstractions/ILicenseStore.cs`
- Create: `src/Nofarma.Application/Abstractions/ILicenseContextStore.cs`
- Create: `src/Nofarma.Application/Abstractions/ILicenseDocumentVerifier.cs`
- Create: `src/Nofarma.Application/Abstractions/IDeviceLicenseIdentityStore.cs`
- Create: `src/Nofarma.Application/Abstractions/ILicenseClockCheckpoint.cs`
- Create: `src/Nofarma.Application/Abstractions/ILicensedOperationPolicy.cs`
- Test: `tests/Nofarma.UnitTests/Application/Licensing/LicenseServiceTests.cs`
- Create: `tests/Nofarma.UnitTests/TestSupport/Licensing/LicenseServiceTestDoubles.cs`

**Interfaces:**
- Consumes: domínio da Task 1 e `IUtcClock`.
- Produces: `GetStatusAsync`, `CreateActivationRequestAsync`, `ImportAsync` e `EnsureNewOperationsAllowedAsync`.

- [ ] **Step 1: Escrever testes falhados para consulta, pedido e importação**

```csharp
[Fact]
public async Task InvalidImportDoesNotReplaceCurrentLicense()
{
    var store = new RecordingLicenseStore(existing: LicenseTestData.StoredActive(sequence: 4));
    var verifier = new StubVerifier(LicenseVerification.Invalid("SIGNATURE_INVALID"));
    var service = LicenseServiceTestFactory.Create(store, verifier);

    LicenseImportException error = await Assert.ThrowsAsync<LicenseImportException>(
        () => service.ImportAsync(new LicenseImportRequest([1, 2, 3]), CancellationToken.None));

    Assert.Equal("SIGNATURE_INVALID", error.Code);
    Assert.Equal(0, store.ReplaceCalls);
}

[Fact]
public async Task OlderRenewalCannotReplaceNewerSequence()
{
    var store = new RecordingLicenseStore(existing: LicenseTestData.StoredActive(sequence: 4));
    var verifier = new StubVerifier(LicenseVerification.Valid(LicenseTestData.Active(sequence: 3)));
    var service = LicenseServiceTestFactory.Create(store, verifier);

    LicenseImportException error = await Assert.ThrowsAsync<LicenseImportException>(
        () => service.ImportAsync(new LicenseImportRequest(ValidBytes), CancellationToken.None));

    Assert.Equal("LICENSE_ROLLBACK", error.Code);
    Assert.Equal(0, store.ReplaceCalls);
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~LicenseServiceTests`

Expected: FAIL por contratos inexistentes.

- [ ] **Step 3: Implementar contratos exactos**

```csharp
public interface ILicenseDocumentVerifier
{
    LicenseVerification Verify(ReadOnlyMemory<byte> document, DeviceLicenseIdentity device, LicenseContext context);
}

public interface ILicenseStore
{
    Task<StoredLicense?> GetAsync(CancellationToken cancellationToken);
    Task ReplaceAsync(VerifiedLicense license, AuditEvent audit, CancellationToken cancellationToken);
}

public interface ILicenseContextStore
{
    Task<LicenseContext?> GetAsync(CancellationToken cancellationToken);
}

public interface IDeviceLicenseIdentityStore
{
    DeviceLicenseIdentity GetOrCreate(EntityId pharmacyId, EntityId deviceId);
}

public interface ILicenseClockCheckpoint
{
    LicenseClockCheck CheckAndAdvance(UtcInstant now);
}

public interface ILicensedOperationPolicy
{
    Task<LicensedOperationPolicyResult> CanCreateAsync(CancellationToken cancellationToken);
}
```

`LicenseService.ImportAsync` verifica o documento antes de persistir, exige sequência estritamente superior para um `LicenseId` diferente ou igual, permite repetição byte-a-byte idempotente e nunca regista assinatura ou documento no evento de auditoria.

`LicenseContext` contém `EntityId PharmacyId`, `EntityId EstablishmentId` e `EntityId DeviceId`. No piloto, o store de infraestrutura devolve `EstablishmentId` igual a `PharmacyId`. `LicenseService` obtém este contexto antes de criar pedidos, consultar ou importar. Contexto ausente produz `LicenseContextUnavailableException` e nunca cria identidade de dispositivo.

`LicenseServiceTestDoubles.cs` define `RecordingLicenseStore : ILicenseStore`, `StubVerifier : ILicenseDocumentVerifier` e `LicenseServiceTestFactory.Create(ILicenseStore, ILicenseDocumentVerifier)`. O store conta `ReplaceCalls` e mantém o `StoredLicense` apenas em memória. A factory usa relógio fixo `2026-08-10T12:00:00Z`, identidade literal e checkpoint sem recuo.

- [ ] **Step 4: Executar GREEN**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~LicenseServiceTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Application/Licensing src/Nofarma.Application/Abstractions tests/Nofarma.UnitTests/Application/Licensing
git commit -m "feat: add licence application contracts"
```

### Task 3: Documento canónico, ECDSA e registo de chaves

**Files:**
- Create: `src/Nofarma.Infrastructure/Licensing/CanonicalLicenseJson.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/EcdsaLicenseDocumentVerifier.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/TrustedLicenseKeyRegistry.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/LicenseBuildChannel.cs`
- Create: `tests/Nofarma.UnitTests/Infrastructure/Licensing/CanonicalLicenseJsonTests.cs`
- Create: `tests/Nofarma.UnitTests/Infrastructure/Licensing/EcdsaLicenseDocumentVerifierTests.cs`
- Modify: `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj`

**Interfaces:**
- Consumes: `ILicenseDocumentVerifier` da Task 2.
- Produces: validação determinística, limites de 64 KiB e profundidade 16, assinatura P1363.

- [ ] **Step 1: Escrever testes de mutação do documento**

```csharp
[Fact]
public void ChangingAnySignedFieldInvalidatesSignature()
{
    SignedLicenseEnvelope original = LicenseTestData.SignedEnvelope();
    foreach (SignedLicenseEnvelope changed in LicenseTestData.EachSingleFieldMutation(original))
    {
        LicenseVerification result = _verifier.Verify(
            CanonicalLicenseJson.SerializeEnvelope(changed),
            LicenseTestData.DeviceIdentity,
            LicenseTestData.Context);
        Assert.False(result.IsValid);
        Assert.Equal("SIGNATURE_INVALID", result.Code);
    }
}

[Theory]
[InlineData(65537)]
[InlineData(1048576)]
public void RejectsOversizedDocuments(int bytes)
{
    LicenseVerification result = _verifier.Verify(new byte[bytes], LicenseTestData.DeviceIdentity, LicenseTestData.Context);
    Assert.Equal("DOCUMENT_TOO_LARGE", result.Code);
}

[Fact]
public void RejectsDeterministicMalformedCorpusWithoutEscapingVerifier()
{
    var random = new Random(20260728);
    for (int index = 0; index < 10_000; index++)
    {
        byte[] document = new byte[random.Next(0, 4097)];
        random.NextBytes(document);
        LicenseVerification result = _verifier.Verify(
            document,
            LicenseTestData.DeviceIdentity,
            LicenseTestData.Context);
        Assert.False(result.IsValid);
    }
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~CanonicalLicenseJsonTests|FullyQualifiedName~EcdsaLicenseDocumentVerifierTests"`

Expected: FAIL por implementações inexistentes.

- [ ] **Step 3: Implementar serialização e verificação mínimas**

Usar `Utf8JsonWriter` com ordem fixa dos campos, UTF-8 sem BOM, datas em formato `O`, enumerações como inteiros versionados e sem whitespace. A assinatura não entra nos bytes assinados.

```csharp
bool ok = ecdsa.VerifyData(
    canonicalPayload,
    signature,
    HashAlgorithmName.SHA256,
    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
```

O parser usa `JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = false, CommentHandling = Disallow }`, rejeita propriedades desconhecidas ou repetidas e valida `channel`, `keyId`, vínculo e versão antes de devolver `VerifiedLicense`.

- [ ] **Step 4: Executar GREEN e mutation check manual**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Licensing"`

Expected: PASS. Alterar temporariamente a ordem ou retirar a verificação de `DeviceKeyThumbprint` deve fazer pelo menos um teste falhar; repor antes do commit.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Infrastructure/Licensing tests/Nofarma.UnitTests/Infrastructure/Licensing tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj
git commit -m "feat: verify canonical signed licences"
```

### Task 4: Identidade do dispositivo e marcador temporal protegidos

**Files:**
- Create: `src/Nofarma.Infrastructure/Licensing/WindowsDeviceLicenseIdentityStore.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/WindowsLicenseClockCheckpoint.cs`
- Create: `tests/Nofarma.UnitTests/Infrastructure/Licensing/WindowsDeviceLicenseIdentityStoreTests.cs`
- Create: `tests/Nofarma.UnitTests/Infrastructure/Licensing/WindowsLicenseClockCheckpointTests.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/ILocalDataProtector.cs`

**Interfaces:**
- Consumes: interfaces da Task 2.
- Produces: chave P-256 protegida por DPAPI e checkpoint com tolerância técnica de recuo de cinco minutos.

- [ ] **Step 1: Escrever testes falhados com protectores substituíveis**

```csharp
[Fact]
public void CopiedProtectedBlobCannotResolveOnAnotherProtector()
{
    var first = new WindowsDeviceLicenseIdentityStore(_path, new FakeProtector("machine-a"));
    DeviceLicenseIdentity identity = first.GetOrCreate(PharmacyId, DeviceId);
    var copied = new WindowsDeviceLicenseIdentityStore(_path, new FakeProtector("machine-b"));

    Assert.Throws<CryptographicException>(() => copied.GetOrCreate(PharmacyId, DeviceId));
    Assert.NotEmpty(identity.PublicKeyThumbprint);
}

[Theory]
[InlineData(-299, false)]
[InlineData(-301, true)]
public void DetectsRollbackBeyondFiveMinutes(int seconds, bool expected)
{
    _checkpoint.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:00:00Z"));
    LicenseClockCheck result = _checkpoint.CheckAndAdvance(
        LicenseTestData.Instant("2026-08-10T12:00:00Z").PlusSeconds(seconds));
    Assert.Equal(expected, result.RollbackDetected);
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~WindowsDeviceLicenseIdentityStoreTests|FullyQualifiedName~WindowsLicenseClockCheckpointTests"`

Expected: FAIL.

- [ ] **Step 3: Implementar com escrita atómica**

Guardar `device-license-key.bin` e `license-clock.bin` em `%LOCALAPPDATA%\ABIPTOM\Nofarma[-QA]\secrets`. Usar DPAPI `CurrentUser`, entropias distintas versionadas e `FileMode.CreateNew` com ficheiro temporário no mesmo directório seguido de `File.Move(temp, target, false)`. Nunca regenerar silenciosamente uma identidade existente corrompida.

`ILocalDataProtector` expõe `byte[] Protect(ReadOnlySpan<byte> clear, ReadOnlySpan<byte> entropy)` e `byte[] Unprotect(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> entropy)`. Produção usa `ProtectedData`; os testes usam `FakeProtector`, que prefixa o identificador da máquina e lança `CryptographicException` quando o prefixo não coincide.

- [ ] **Step 4: Executar GREEN**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Infrastructure.Licensing"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Infrastructure/Licensing tests/Nofarma.UnitTests/Infrastructure/Licensing
git commit -m "feat: protect licence device identity"
```

### Task 5: Persistência SQLite, migração e importação atómica

**Files:**
- Create: `src/Nofarma.Infrastructure/Persistence/Records/LicenseRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/LicenseConfiguration.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/SqliteLicenseStore.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/SqliteLicenseContextStore.cs`
- Modify: `src/Nofarma.Infrastructure/Persistence/NofarmaDbContext.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Migrations/*_AddSignedLicensing.cs`
- Test: `tests/Nofarma.IntegrationTests/Licensing/LicensePersistenceTests.cs`
- Create: `tests/Nofarma.IntegrationTests/Licensing/LicenseDatabase.cs`

**Interfaces:**
- Consumes: `ILicenseStore`, `ILicenseContextStore`, `VerifiedLicense`.
- Produces: uma licença corrente por instalação, histórico de sequência através de auditoria e actualização transaccional do estado da instalação.

**Clarificação da fronteira de confiança:** `ILicenseStore.GetAsync` devolve apenas os bytes persistidos como dados não confiáveis. `LicenseService` verifica criptograficamente esses bytes em cada consulta antes de usar `Grant`, `Channel`, `KeyId` ou `Sequence`. As colunas SQLite são projecções para persistência, índices e diagnóstico e nunca podem activar ou prolongar uma licença. Alterar `ValidUntilUtc`, `Sequence`, `Plan`, `Channel` ou `KeyId` directamente na base não altera a avaliação. Um documento persistido inválido produz `LicenseState.Invalid` e bloqueia novas operações. Uma importação nova com assinatura válida pode substituir esse estado inválido para recuperação.

- [ ] **Step 1: Escrever testes falhados de migração e rollback**

```csharp
[Fact]
public async Task ImportAtomicallyStoresLicenseAuditAndActiveInstallation()
{
    await using LicenseDatabase db = await LicenseDatabase.CreateAsync(InstallationStatus.ReadyForActivation);
    await db.Store.ReplaceAsync(db.Verified(sequence: 1), db.Audit("license.imported"), CancellationToken.None);

    await using var check = db.OpenContext();
    Assert.Equal(1, await check.Licenses.CountAsync());
    Assert.Equal((int)InstallationStatus.Active, await check.Installations.Select(x => x.Status).SingleAsync());
    Assert.Equal(1, await check.AuditEvents.CountAsync(x => x.EventType == "license.imported"));
}

[Fact]
public async Task FailureBeforeCommitLeavesPreviousLicenseUntouched()
{
    await using LicenseDatabase db = await LicenseDatabase.CreateWithLicenseAsync(sequence: 2);
    db.Store.FailBeforeCommit = true;
    await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.ReplaceAsync(
        db.Verified(sequence: 3), db.Audit("license.renewed"), CancellationToken.None));
    Assert.Equal(2, await db.CurrentSequenceAsync());
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~LicensePersistenceTests`

Expected: FAIL por tabela e store inexistentes.

- [ ] **Step 3: Implementar registo e transacção**

`LicenseRecord` guarda `Id`, `InstallationId`, `LicenseId`, `Sequence`, `Channel`, `KeyId`, `DocumentBytes`, `DocumentHash`, `ValidFromUtc`, `ValidUntilUtc`, `GraceUntilUtc`, `ImportedAtUtc`. Criar índice único em `InstallationId` e em `(LicenseId, Sequence)`. Validar `DocumentBytes.Length <= 65536` antes da transacção.

Abrir SQLite, iniciar `BeginTransaction(deferred: false)`, validar sequência novamente dentro da transacção, substituir a linha, acrescentar auditoria e actualizar `Installation.Status` para `Active`. Em erro, rollback total.

`LicenseDatabase` é um fixture descartável que cria uma pasta temporária, migra SQLite, expõe `Options`, `Store`, `OpenContext()`, `Verified(long sequence)`, `Audit(string eventType)` e `CurrentSequenceAsync()`. `DisposeAsync` chama `SqliteConnection.ClearAllPools()` antes de remover apenas a sua pasta temporária validada.

`SqliteLicenseContextStore.GetAsync` lê uma única instalação e devolve `LicenseContext(PharmacyId, PharmacyId, DeviceId)`. Devolve `null` quando não existe instalação e lança perante múltiplas instalações, sem escolher silenciosamente uma linha.

- [ ] **Step 4: Executar migração e GREEN**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~Licensing`

Expected: PASS, incluindo upgrade a partir de `20260728120000_AddCashShifts`.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Infrastructure/Persistence src/Nofarma.Infrastructure/Licensing/SqliteLicenseStore.cs tests/Nofarma.IntegrationTests/Licensing
git commit -m "feat: persist signed licences atomically"
```

### Task 6: Política runtime nas operações protegidas

**Files:**
- Create: `src/Nofarma.Infrastructure/Licensing/LicenseOperationPolicy.cs`
- Modify: `src/Nofarma.Infrastructure/Composition/ServiceCollectionExtensions.cs`
- Modify: `src/Nofarma.Application/Inventory/InventoryService.cs`
- Modify: `src/Nofarma.Application/Inventory/Import/InventoryImportService.cs`
- Modify: `src/Nofarma.Application/Purchasing/PurchaseService.cs`
- Modify: `src/Nofarma.Application/Sales/CashShiftService.cs`
- Delete: `src/Nofarma.Application/Abstractions/IStockOperationPolicy.cs`
- Delete: `src/Nofarma.Infrastructure/Licensing/InstallationStockOperationPolicy.cs`
- Modify tests under: `tests/Nofarma.UnitTests/Application/{Inventory,Purchasing,Sales}`
- Create: `tests/Nofarma.IntegrationTests/Licensing/LicenseOperationPolicyTests.cs`

**Interfaces:**
- Consumes: `LicenseService.GetStatusAsync` e `ILicensedOperationPolicy`.
- Produces: um gate comum que permite `Valid` e `Grace` e bloqueia os restantes estados.

- [ ] **Step 1: Escrever testes falhados de política real**

```csharp
[Theory]
[InlineData(LicenseState.Valid, true)]
[InlineData(LicenseState.Grace, true)]
[InlineData(LicenseState.Missing, false)]
[InlineData(LicenseState.Invalid, false)]
[InlineData(LicenseState.NotYetValid, false)]
[InlineData(LicenseState.ExpiredReadOnly, false)]
[InlineData(LicenseState.ClockRollback, false)]
public async Task NewOperationsFollowCalculatedLicenseState(LicenseState state, bool allowed)
{
    var statusProvider = new FixedLicenseStatusProvider(state);
    var policy = new LicenseOperationPolicy(statusProvider);
    LicensedOperationPolicyResult result = await policy.CanCreateAsync(CancellationToken.None);
    Assert.Equal(allowed, result.IsAllowed);
}
```

Acrescentar integração que usa a persistência e o verificador reais para provar que `OpenCashShift`, `ConfirmInventory`, `ReceivePurchase` e `ConfirmImport` bloqueiam sem licença e funcionam com licença QA válida.

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~LicenseOperationPolicy|FullyQualifiedName~CashShiftServiceTests|FullyQualifiedName~InventoryServiceTests|FullyQualifiedName~PurchaseServiceTests"`

Expected: FAIL por interface comum inexistente nos serviços.

- [ ] **Step 3: Substituir a política antiga**

Todos os serviços recebem `ILicensedOperationPolicy`. Antes de uma nova alteração operacional chamam `CanCreateAsync`. O fecho de turno mantém-se permitido para recuperação segura. Leitura, rascunhos e repetição idempotente já confirmada mantêm a semântica existente.

O teste define `FixedLicenseStatusProvider` com um único método `GetStatusAsync` que devolve o estado literal recebido no construtor. Não calcula o resultado esperado com código de produção.

- [ ] **Step 4: Executar GREEN completo de aplicação e integração**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Application"`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~Licensing|FullyQualifiedName~Inventory|FullyQualifiedName~Sales"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Application src/Nofarma.Infrastructure tests/Nofarma.UnitTests/Application tests/Nofarma.IntegrationTests/Licensing
git commit -m "feat: enforce signed licence policy"
```

### Task 7: Página Desktop, estado no shell e canal QA

**Files:**
- Create: `src/Nofarma.Desktop/ViewModels/LicenseViewModel.cs`
- Create: `src/Nofarma.Desktop/Views/LicensePage.xaml`
- Create: `src/Nofarma.Desktop/Views/LicensePage.xaml.cs`
- Create: `src/Nofarma.Desktop/Services/LicensePageOperations.cs`
- Modify: `src/Nofarma.Desktop/Composition/ServiceCollectionExtensions.cs`
- Modify: `src/Nofarma.Desktop/Views/AppShellPage.xaml`
- Modify: `src/Nofarma.Desktop/Views/AppShellPage.xaml.cs`
- Modify: `src/Nofarma.Desktop/Nofarma.Desktop.csproj`
- Create: `tests/Nofarma.UnitTests/Desktop/LicenseViewModelTests.cs`
- Modify: `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj`

**Interfaces:**
- Consumes: `LicenseService`.
- Produces: estado acessível, exportação `.nofarma-request`, importação `.nofarma-license` e marca QA.

- [ ] **Step 1: Escrever testes falhados do view-model**

```csharp
[Theory]
[InlineData(LicenseState.Missing, "Sem licença")]
[InlineData(LicenseState.Valid, "Licença activa")]
[InlineData(LicenseState.Grace, "Tolerância")]
[InlineData(LicenseState.ExpiredReadOnly, "Só consulta")]
[InlineData(LicenseState.Invalid, "Licença inválida")]
[InlineData(LicenseState.ClockRollback, "Verificar relógio")]
public async Task ShowsHonestLocalizedState(LicenseState state, string expected)
{
    var vm = new LicenseViewModel(new OperationsReturning(state));
    await vm.LoadAsync(CancellationToken.None);
    Assert.Contains(expected, vm.StatusText, StringComparison.Ordinal);
}

[Fact]
public async Task FailedImportPreservesCurrentStatusAndShowsRecoveryMessage()
{
    var vm = new LicenseViewModel(new FailingImportOperations("DEVICE_MISMATCH"));
    await vm.ImportAsync([1, 2, 3], CancellationToken.None);
    Assert.Contains("outro computador", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Assert.False(vm.IsBusy);
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~LicenseViewModelTests`

Expected: FAIL.

- [ ] **Step 3: Implementar UI nativa**

A página usa dois painéis a 1366 por 768: estado e datas à esquerda; exportar pedido e importar licença à direita. Alvos mínimos de 44 por 44, rótulos persistentes, nomes de automação e `InfoBar` para recuperação. O shell adiciona destino `Licença` e actualiza a faixa depois de uma importação bem-sucedida.

No `.csproj`, definir `NofarmaLicenseChannel` com valor seguro `Unlicensed` por omissão. `QA` inclui apenas `build/keys/nofarma-qa-public.spki.b64`, usa `%LOCALAPPDATA%\ABIPTOM\Nofarma-QA` e mostra `Modo QA`. `Commercial` exige `build/keys/nofarma-commercial-public.spki.b64`; se ausente, o target `ValidateCommercialLicenseKey` falha o build.

- [ ] **Step 4: Executar GREEN e build dos canais**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~Desktop`

Run: `dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj -c Release -p:NofarmaLicenseChannel=Unlicensed`

Run: `dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj -c Release -p:NofarmaLicenseChannel=Commercial`

Expected: testes e `Unlicensed` PASS. `Commercial` FAIL com `NFLC001: commercial public key is not provisioned` enquanto a ABIPTOM não fornecer a chave pública comercial.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Desktop tests/Nofarma.UnitTests/Desktop tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj
git commit -m "feat: add offline licence activation UI"
```

### Task 8: Emissor QA separado e gate anti-contaminação

**Files:**
- Create: `tools/Nofarma.Licensing.Qa/Nofarma.Licensing.Qa.csproj`
- Create: `tools/Nofarma.Licensing.Qa/Program.cs`
- Create: `tools/Nofarma.Licensing.Qa/QaKeyStore.cs`
- Create: `tools/Nofarma.Licensing.Qa/QaLicenseIssuer.cs`
- Create: `tests/Nofarma.IntegrationTests/Licensing/QaIssuerTests.cs`
- Modify: `.gitignore`
- Create during provisioning and commit: `build/keys/nofarma-qa-public.spki.b64`

**Interfaces:**
- Consumes: `.nofarma-request` e formato canónico da Task 3.
- Produces: comandos `provision` e `issue` com opções obrigatórias `--request`, `--plan`, `--valid-from` e `--output`.

- [ ] **Step 1: Escrever testes falhados do emissor e separação**

```csharp
[Fact]
public void QaIssuedLicenseValidatesOnlyInQaRegistry()
{
    byte[] document = _issuer.Issue(_request, LicensePlan.Monthly, From, Until);
    Assert.True(_qaVerifier.Verify(document, _device, _context).IsValid);
    Assert.Equal("CHANNEL_MISMATCH", _commercialVerifier.Verify(document, _device, _context).Code);
}

[Fact]
public void RepositoryDoesNotContainQaPrivateKey()
{
    Assert.False(File.Exists(Path.Combine(RepoRoot, "build", "keys", "nofarma-qa-private.p8")));
    Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(QaPublicKeyPath), StringComparison.Ordinal);
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~QaIssuerTests`

Expected: FAIL por ferramenta inexistente.

- [ ] **Step 3: Implementar provisionamento e emissão**

`provision` cria a chave P-256, guarda PKCS#8 protegido em `%LOCALAPPDATA%\ABIPTOM\Nofarma-QA\issuer\qa-signing-key.bin` e escreve apenas SPKI Base64 em `build/keys/nofarma-qa-public.spki.b64`. Se já existir chave, não a substitui sem `--rotate`, e `--rotate` exige escrever exactamente `ROTATE-QA-KEY` no terminal.

`issue` recusa pedidos com canal diferente de QA, datas fora da ordem, validade mensal superior a 31 dias, validade anual superior a 366 dias ou output já existente. O ficheiro é criado com `FileMode.CreateNew`.

- [ ] **Step 4: Executar GREEN e inspeccionar artefactos**

Run: `dotnet run --project tools/Nofarma.Licensing.Qa -- provision --public-output build/keys/nofarma-qa-public.spki.b64`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~QaIssuerTests`

Run: `rg -n "PRIVATE KEY|BEGIN EC|BEGIN PRIVATE|qa-signing-key" build src tests tools --glob '!**/bin/**' --glob '!**/obj/**'`

Expected: testes PASS. Pesquisa encontra apenas nomes de teste ou mensagens, nunca material privado.

- [ ] **Step 5: Commit**

```powershell
git add tools/Nofarma.Licensing.Qa tests/Nofarma.IntegrationTests/Licensing build/keys/nofarma-qa-public.spki.b64 .gitignore
git commit -m "feat: add protected QA licence issuer"
```

### Task 9: Documentação, release gate e preparação do QA visual

**Files:**
- Create: `docs/development/licensing.md`
- Modify: `docs/development/verification.md`
- Modify: `README.md`
- Modify: `Nofarma.slnx`
- Create: `scripts/verify-license-channel.ps1`
- Test: `tests/Nofarma.IntegrationTests/Licensing/PublishedChannelTests.cs`
- Create: `tests/Nofarma.IntegrationTests/Licensing/PublishedFiles.cs`

**Interfaces:**
- Consumes: todas as tarefas anteriores.
- Produces: procedimento repetível para build QA, pedido, emissão, importação e release comercial segura.

- [ ] **Step 1: Escrever teste falhado do conteúdo publicado**

```csharp
[Fact]
public void QaPublishContainsNoIssuerOrPrivateMaterial()
{
    byte[] published = PublishedFiles.ReadAllBytes();
    Assert.DoesNotContain("Nofarma.Licensing.Qa", PublishedFiles.Names, StringComparer.OrdinalIgnoreCase);
    Assert.DoesNotContain("PRIVATE KEY", Encoding.UTF8.GetString(published), StringComparison.Ordinal);
}

[Fact]
public void CommercialBuildFailsClosedWithoutProvisionedPublicKey()
{
    ProcessResult result = PublishedFiles.BuildCommercialWithoutKey();
    Assert.NotEqual(0, result.ExitCode);
    Assert.Contains("NFLC001", result.Output, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~PublishedChannelTests`

Expected: FAIL porque o gate ainda não existe.

- [ ] **Step 3: Implementar script e documentação**

O script recebe `-Channel Unlicensed|QA|Commercial` e `-Output`. Publica para uma pasta absoluta, vazia e validada, procura a chave do canal oposto, ferramenta emissora, `.p8`, `.pem`, `.key`, `.nofarma-license`, `.nofarma-request` e padrões de segredos. `Commercial` falha se não tiver chave pública comercial.

`PublishedFiles` publica o canal QA para uma pasta temporária através de `dotnet publish`, enumera nomes relativos e concatena apenas ficheiros de texto até 8 MiB. `BuildCommercialWithoutKey` copia o projecto para uma pasta temporária sem `nofarma-commercial-public.spki.b64`, executa o target de validação e captura código e saída. O fixture rejeita caminhos fora da pasta temporária e remove-a no `Dispose`.

Documentar comandos exactos e explicar que o utilizador nunca deve enviar uma chave privada, palavra-passe ou código de recuperação.

- [ ] **Step 4: Executar gate completo**

```powershell
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx -c Release --no-restore -warnaserror -p:NofarmaLicenseChannel=Unlicensed
dotnet test Nofarma.slnx -c Release --no-build
dotnet list Nofarma.slnx package --vulnerable --include-transitive
powershell -File scripts/verify-license-channel.ps1 -Channel QA -Output artifacts/license-qa
git diff --check
```

Expected: todos os comandos PASS, excepto qualquer consulta externa que falhe por indisponibilidade de rede, que deve ser registada como não verificada e repetida antes da release.

- [ ] **Step 5: Commit**

```powershell
git add README.md docs/development Nofarma.slnx scripts/verify-license-channel.ps1 tests/Nofarma.IntegrationTests/Licensing
git commit -m "docs: verify signed licensing release gate"
```

## Gate de conclusão deste plano

- [ ] Cada função nova teve um teste observado em RED antes da implementação.
- [ ] As tarefas têm revisão de conformidade e qualidade sem achados críticos ou importantes abertos.
- [ ] O canal QA activa apenas com a chave QA correcta.
- [ ] O canal comercial rejeita a chave QA e falha sem chave pública comercial.
- [ ] Nenhuma chave privada, licença emitida ou pedido real está no Git.
- [ ] O estado sem licença continua testável e não é transformado por edição manual de SQLite.
- [ ] O plano seguinte `2026-07-28-nofarma-full-app-qa.md` só começa depois deste gate.
