# NôFarma Local Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar uma aplicação Windows funcional que configure uma farmácia, crie o administrador principal, gere recuperação offline, autentique utilizadores, aplique permissões e mantenha auditoria local em SQLite.

**Architecture:** O domínio mantém regras sem dependências externas. A camada de aplicação expõe casos de uso e contratos. A infraestrutura usa Entity Framework Core com SQLite, PBKDF2 para credenciais e DPAPI para proteger o pepper fora da base de dados. A aplicação WinUI usa injecção de dependências, view models pequenos e páginas que seguem obrigatoriamente o atlas em `docs/design/previews`.

**Tech Stack:** C# 14, .NET 10.0.302, WinUI 3, Windows App SDK 2.3.1, Entity Framework Core 10.0.10, SQLite, Windows DPAPI, xUnit v3 e GitHub Actions.

## Global Constraints

- Marca apresentada ao utilizador: `NôFarma by ABIPTOM`.
- Namespaces e nomes técnicos: `Nofarma` sem acento.
- Windows mínimo: Windows 10 22H2 de 64 bits.
- Operação local sem internet: pelo menos 90 dias.
- SQLite é a fonte operacional local.
- Uma farmácia, um estabelecimento e um computador operacional no piloto.
- O assistente cria a farmácia e o administrador antes da activação da licença.
- Utilizadores não Caixa usam palavra-passe forte.
- Caixas usam apenas PIN de quatro a seis dígitos.
- Cinco falhas bloqueiam a conta durante 15 minutos.
- O código de recuperação é de utilização única e roda depois da utilização.
- Segredos, bases de dados, backups e dados de execução não entram no Git.
- Datas persistidas usam UTC.
- O atlas em `docs/design/previews` é a referência visual obrigatória.
- A especificação prevalece quando uma imagem contiver texto ou dados fictícios incoerentes.
- A interface não apresenta valores de subscrição ainda não definidos.
- Não é inventada uma API, URL ou autorização da DGCI.
- Todo o código novo testável começa por um teste que falha.
- Cada tarefa termina com testes, revisão do diff e commit.

## Estrutura de ficheiros

### Domínio

- `src/Nofarma.Domain/Identity/InstallationStatus.cs`: estados da instalação.
- `src/Nofarma.Domain/Identity/UserRole.cs`: funções predefinidas.
- `src/Nofarma.Domain/Identity/Permission.cs`: permissões explícitas.
- `src/Nofarma.Domain/Identity/CredentialKind.cs`: palavra-passe ou PIN.
- `src/Nofarma.Domain/Identity/LocalUser.cs`: utilizador, bloqueio e estado.
- `src/Nofarma.Domain/Identity/Pharmacy.cs`: farmácia e estabelecimento do piloto.
- `src/Nofarma.Domain/Identity/Device.cs`: dispositivo local.
- `src/Nofarma.Domain/Identity/Installation.cs`: agregado de configuração.
- `src/Nofarma.Domain/Identity/RolePermissions.cs`: matriz de permissões.
- `src/Nofarma.Domain/Auditing/AuditEvent.cs`: evento imutável.

### Aplicação

- `src/Nofarma.Application/Abstractions/ILocalIdentityStore.cs`: persistência transaccional.
- `src/Nofarma.Application/Abstractions/ICredentialHasher.cs`: hash e verificação.
- `src/Nofarma.Application/Abstractions/ICurrentSession.cs`: sessão activa.
- `src/Nofarma.Application/Identity/Setup/*`: configuração inicial.
- `src/Nofarma.Application/Identity/Authentication/*`: login, bloqueio e recuperação.
- `src/Nofarma.Application/Identity/Authorization/*`: autorização.
- `src/Nofarma.Application/Identity/Users/*`: administração de utilizadores.

### Infraestrutura

- `src/Nofarma.Infrastructure/Persistence/NofarmaDbContext.cs`: contexto SQLite.
- `src/Nofarma.Infrastructure/Persistence/Configurations/*`: mapeamentos EF Core.
- `src/Nofarma.Infrastructure/Persistence/Migrations/*`: migração inicial.
- `src/Nofarma.Infrastructure/Persistence/SqliteLocalIdentityStore.cs`: implementação transaccional.
- `src/Nofarma.Infrastructure/Security/Pbkdf2CredentialHasher.cs`: PBKDF2-HMAC-SHA256.
- `src/Nofarma.Infrastructure/Security/WindowsCredentialPepperStore.cs`: pepper protegido por DPAPI.
- `src/Nofarma.Infrastructure/Composition/ServiceCollectionExtensions.cs`: registo de serviços.

### Aplicação Windows

- `src/Nofarma.Desktop/Design/ThemeResources.xaml`: tokens visuais.
- `src/Nofarma.Desktop/Models/NavigationItem.cs`: destinos do shell.
- `src/Nofarma.Desktop/ViewModels/SetupWizardViewModel.cs`: estado do assistente.
- `src/Nofarma.Desktop/ViewModels/LoginViewModel.cs`: acesso local.
- `src/Nofarma.Desktop/ViewModels/UsersViewModel.cs`: utilizadores.
- `src/Nofarma.Desktop/Views/SetupWizardPage.xaml`: assistente visual.
- `src/Nofarma.Desktop/Views/LoginPage.xaml`: palavra-passe e PIN.
- `src/Nofarma.Desktop/Views/AppShellPage.xaml`: navegação principal.
- `src/Nofarma.Desktop/Views/UsersPage.xaml`: utilizadores e permissões.
- `src/Nofarma.Desktop/Views/SettingsPage.xaml`: farmácia e DGCI.
- `src/Nofarma.Desktop/Views/ModuleEmptyPage.xaml`: estado vazio seguro para módulos posteriores.

### Testes

- `tests/Nofarma.UnitTests/Domain/Identity/*`: regras puras.
- `tests/Nofarma.UnitTests/Application/Identity/*`: casos de uso.
- `tests/Nofarma.UnitTests/Infrastructure/Security/*`: hashing e protecção.
- `tests/Nofarma.IntegrationTests/LocalIdentity/*`: SQLite real e transacções.

---

### Task 1: Modelo de identidade e instalação

**Files:**

- Create: `src/Nofarma.Domain/Identity/InstallationStatus.cs`
- Create: `src/Nofarma.Domain/Identity/UserRole.cs`
- Create: `src/Nofarma.Domain/Identity/Permission.cs`
- Create: `src/Nofarma.Domain/Identity/CredentialKind.cs`
- Create: `src/Nofarma.Domain/Identity/UserStatus.cs`
- Create: `src/Nofarma.Domain/Identity/Pharmacy.cs`
- Create: `src/Nofarma.Domain/Identity/Device.cs`
- Create: `src/Nofarma.Domain/Identity/LocalUser.cs`
- Create: `src/Nofarma.Domain/Identity/Installation.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Identity/InstallationTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Identity/LocalUserTests.cs`

**Interfaces:**

- Produces: `Installation.Create(...)`, `Installation.MarkReadyForActivation(...)`.
- Produces: `LocalUser.CreatePrimaryAdministrator(...)`, `LocalUser.CreateCashier(...)`.
- Produces: `LocalUser.RecordFailedLogin(...)`, `LocalUser.RecordSuccessfulLogin(...)`, `LocalUser.Unlock(...)`.

- [ ] **Step 1: Escrever testes de instalação que falham**

Criar testes que exigem estado inicial `Preparing`, administrador principal com `Password` e rejeição de uma segunda conclusão.

```csharp
[Fact]
public void CreateStartsPreparingWithPrimaryAdministrator()
{
    UtcInstant now = UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

    Installation installation = Installation.Create(
        EntityId.New(),
        new Pharmacy(EntityId.New(), "Farmácia Central", "500123456", "Bissau", "Africa/Bissau"),
        new Device(EntityId.New(), "NBF-PC-001"),
        LocalUser.CreatePrimaryAdministrator(EntityId.New(), "Maria Indjai", "maria.indjai", now),
        now);

    Assert.Equal(InstallationStatus.Preparing, installation.Status);
    Assert.True(installation.PrimaryAdministrator.IsPrimaryAdministrator);
    Assert.Equal(CredentialKind.Password, installation.PrimaryAdministrator.CredentialKind);
}
```

- [ ] **Step 2: Executar e confirmar a falha correcta**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~Domain.Identity --no-restore`

Expected: FAIL porque os tipos de identidade ainda não existem.

- [ ] **Step 3: Implementar o modelo mínimo**

Usar classes seladas com construtores privados para entidades mutáveis e enums para estados. Normalizar o identificador de acesso com `Trim().ToUpperInvariant()`. Rejeitar nomes vazios, NIF vazio, dispositivo vazio e transições de estado inválidas.

- [ ] **Step 4: Acrescentar testes de bloqueio do utilizador**

O teste usa cinco instantes consecutivos e confirma `LockedUntilUtc` exactamente 15 minutos depois da quinta falha. Uma tentativa anterior à expiração é rejeitada. Um sucesso repõe o contador.

- [ ] **Step 5: Executar os testes do domínio**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~Domain.Identity --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Domain/Identity tests/Nofarma.UnitTests/Domain/Identity
git commit -m "feat: add local identity domain model"
```

### Task 2: Funções e permissões com negação por omissão

**Files:**

- Create: `src/Nofarma.Domain/Identity/RolePermissions.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Identity/RolePermissionsTests.cs`

**Interfaces:**

- Consumes: `UserRole`, `Permission`.
- Produces: `RolePermissions.IsAllowed(UserRole role, Permission permission)`.
- Produces: `RolePermissions.GetPermissions(UserRole role)`.

- [ ] **Step 1: Escrever o teste que protege o Caixa**

```csharp
[Theory]
[InlineData(Permission.ManageUsers)]
[InlineData(Permission.ConfigureFiscalSettings)]
[InlineData(Permission.ViewPurchasePrices)]
[InlineData(Permission.ManagePermissions)]
public void CashierIsDeniedAdministrativePermissions(Permission permission)
{
    Assert.False(RolePermissions.IsAllowed(UserRole.Cashier, permission));
}
```

Acrescentar testes positivos para `SignIn`, `LockOwnSession` e futuras permissões `CreateSale` e `ManageCashShift` do Caixa. Testar que uma permissão desconhecida ou não atribuída é negada.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~RolePermissionsTests --no-restore`

Expected: FAIL porque `RolePermissions` não existe.

- [ ] **Step 3: Implementar uma matriz imutável**

Usar `FrozenDictionary<UserRole, FrozenSet<Permission>>`. Não adicionar uma regra especial que conceda tudo ao administrador. Enumerar explicitamente as permissões do Administrador para manter a negação por omissão.

- [ ] **Step 4: Executar os testes**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~RolePermissionsTests --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Domain/Identity/RolePermissions.cs tests/Nofarma.UnitTests/Domain/Identity/RolePermissionsTests.cs
git commit -m "feat: define local role permissions"
```

### Task 3: Hash versionado e pepper protegido pelo Windows

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/Nofarma.Infrastructure/Nofarma.Infrastructure.csproj`
- Create: `src/Nofarma.Application/Abstractions/ICredentialHasher.cs`
- Create: `src/Nofarma.Application/Identity/CredentialHash.cs`
- Create: `src/Nofarma.Infrastructure/Security/ICredentialPepperStore.cs`
- Create: `src/Nofarma.Infrastructure/Security/Pbkdf2CredentialHasher.cs`
- Create: `src/Nofarma.Infrastructure/Security/WindowsCredentialPepperStore.cs`
- Test: `tests/Nofarma.UnitTests/Infrastructure/Security/Pbkdf2CredentialHasherTests.cs`

**Interfaces:**

```csharp
public interface ICredentialHasher
{
    CredentialHash Hash(string credential);
    bool Verify(string credential, CredentialHash stored);
    bool NeedsRehash(CredentialHash stored);
}

public sealed record CredentialHash(
    int Version,
    string Algorithm,
    int WorkFactor,
    byte[] Salt,
    byte[] Hash);
```

- [ ] **Step 1: Escrever testes de segurança que falham**

Testar que dois hashes da mesma palavra-passe têm salts e hashes diferentes, que a credencial correcta valida, que a errada falha e que a comparação não altera o valor persistido. Testar `NeedsRehash` para uma versão ou factor antigos.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~Pbkdf2CredentialHasherTests --no-restore`

Expected: FAIL porque o hasher ainda não existe.

- [ ] **Step 3: Adicionar a dependência Windows**

Adicionar `System.Security.Cryptography.ProtectedData` versão `10.0.10` ao controlo central e à infraestrutura.

- [ ] **Step 4: Implementar PBKDF2 e DPAPI**

Usar `Rfc2898DeriveBytes.Pbkdf2` estático com HMAC-SHA256, 600000 iterações, salt aleatório de 16 bytes e resultado de 32 bytes. Antes do PBKDF2, aplicar HMAC-SHA256 à credencial com um pepper aleatório de 32 bytes.

Guardar o pepper fora de SQLite em `%LOCALAPPDATA%\ABIPTOM\Nofarma\secrets\credential-pepper.bin`. Proteger e desproteger com DPAPI `DataProtectionScope.CurrentUser`. Criar o ficheiro apenas se não existir. Nunca registar o pepper em logs.

Usar `CryptographicOperations.FixedTimeEquals` na verificação. Limitar a entrada a 256 caracteres antes do trabalho criptográfico para evitar abuso de CPU.

- [ ] **Step 5: Executar os testes**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~Pbkdf2CredentialHasherTests --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Directory.Packages.props src/Nofarma.Application src/Nofarma.Infrastructure tests/Nofarma.UnitTests/Infrastructure/Security
git commit -m "feat: protect local credentials"
```

### Task 4: SQLite, mapeamentos e migração inicial

**Files:**

- Modify: `src/Nofarma.Infrastructure/Nofarma.Infrastructure.csproj`
- Modify: `tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj`
- Create: `.config/dotnet-tools.json`
- Create: `src/Nofarma.Infrastructure/Persistence/NofarmaDbContext.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/NofarmaDbContextFactory.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/LocalDatabasePath.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/InstallationConfiguration.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/PharmacyConfiguration.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/DeviceConfiguration.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/LocalUserConfiguration.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Migrations/*`
- Test: `tests/Nofarma.IntegrationTests/LocalIdentity/LocalDatabaseTests.cs`

**Interfaces:**

- Produces: `NofarmaDbContext` with `Installations`, `Pharmacies`, `Devices`, `LocalUsers`, `CredentialRecords`, `RecoveryCodes`, `LocalSessions`, `AuditEvents`.
- Produces: `LocalDatabasePath.GetDefault()`.

- [ ] **Step 1: Escrever o teste SQLite que falha**

Criar uma pasta temporária por teste. Aplicar migrações a um ficheiro SQLite real. Consultar `sqlite_master` e confirmar as tabelas e índices únicos de instalação e identificador de acesso.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~LocalDatabaseTests --no-restore`

Expected: FAIL porque o contexto não existe.

- [ ] **Step 3: Configurar EF Core**

Adicionar referências de `Microsoft.EntityFrameworkCore.Sqlite` e `Microsoft.EntityFrameworkCore.Design` à infraestrutura. Referenciar Domain, Application e Infrastructure no projecto de integração. Fixar `dotnet-ef` em `10.0.10` no manifesto local.

- [ ] **Step 4: Implementar contexto e mapeamentos**

Usar nomes de tabela em inglês e colunas explícitas. Guardar `EntityId.Value` como `TEXT` de GUID. Guardar instantes como `TEXT` ISO 8601 UTC. Configurar WAL, foreign keys e busy timeout na abertura da ligação.

Criar índices únicos para uma instalação activa, NIF da farmácia, dispositivo e `PharmacyId + NormalizedLoginName`. Nunca mapear propriedades com credenciais em texto legível.

- [ ] **Step 5: Criar e aplicar a migração**

Run: `dotnet tool restore`

Run: `dotnet ef migrations add InitialLocalIdentity --project src/Nofarma.Infrastructure --startup-project src/Nofarma.Infrastructure --output-dir Persistence/Migrations`

- [ ] **Step 6: Executar o teste SQLite**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~LocalDatabaseTests --no-restore`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add .config Directory.Packages.props src/Nofarma.Infrastructure tests/Nofarma.IntegrationTests
git commit -m "feat: add local identity database"
```

### Task 5: Configuração inicial transaccional e auditoria

**Files:**

- Create: `src/Nofarma.Domain/Auditing/AuditOutcome.cs`
- Create: `src/Nofarma.Domain/Auditing/AuditEvent.cs`
- Create: `src/Nofarma.Application/Abstractions/ILocalIdentityStore.cs`
- Create: `src/Nofarma.Application/Identity/Setup/SetupRequest.cs`
- Create: `src/Nofarma.Application/Identity/Setup/SetupResult.cs`
- Create: `src/Nofarma.Application/Identity/Setup/SetupService.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/SqliteLocalIdentityStore.cs`
- Test: `tests/Nofarma.UnitTests/Application/Identity/SetupServiceTests.cs`
- Test: `tests/Nofarma.IntegrationTests/LocalIdentity/SetupTransactionTests.cs`

**Interfaces:**

```csharp
public sealed record SetupRequest(
    string PharmacyName,
    string TaxIdentifier,
    string Address,
    string Contact,
    string TimeZoneId,
    string DeviceName,
    string AdministratorName,
    string AdministratorLogin,
    string AdministratorPassword);

public sealed record SetupResult(
    EntityId InstallationId,
    string RecoveryCode);
```

- [ ] **Step 1: Escrever o teste do caso de uso que falha**

Testar que `SetupService.ConfigureAsync` cria instalação, funções, administrador, credencial, recuperação e auditoria. Confirmar que a resposta contém o código de recuperação apenas nessa execução.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~SetupServiceTests --no-restore`

Expected: FAIL porque o serviço não existe.

- [ ] **Step 3: Implementar o serviço mínimo**

Validar todos os campos antes de abrir a transacção. Gerar um código de recuperação com 20 caracteres aleatórios usando alfabeto sem caracteres ambíguos. Persistir apenas o hash. Marcar o primeiro utilizador como administrador principal.

- [ ] **Step 4: Escrever o teste de reversão real**

Injectar uma falha controlada antes do commit. Reabrir SQLite e confirmar zero instalações, zero utilizadores e zero códigos de recuperação. Repetir a configuração válida e confirmar uma única instalação.

- [ ] **Step 5: Implementar a transacção SQLite**

Usar uma transacção EF Core explícita. A auditoria de conclusão faz parte da mesma transacção. Uma segunda configuração lança um erro de domínio estável e não altera dados.

- [ ] **Step 6: Executar testes unitários e de integração**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~SetupServiceTests --no-restore`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~SetupTransactionTests --no-restore`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/Nofarma.Domain/Auditing src/Nofarma.Application src/Nofarma.Infrastructure tests
git commit -m "feat: configure pharmacy atomically"
```

### Task 6: Autenticação, bloqueio, sessão e recuperação

**Files:**

- Create: `src/Nofarma.Application/Identity/Authentication/SignInRequest.cs`
- Create: `src/Nofarma.Application/Identity/Authentication/SignInResult.cs`
- Create: `src/Nofarma.Application/Identity/Authentication/AuthenticationService.cs`
- Create: `src/Nofarma.Application/Identity/Authentication/RecoveryRequest.cs`
- Create: `src/Nofarma.Application/Identity/Authentication/RecoveryResult.cs`
- Create: `src/Nofarma.Application/Identity/Authentication/RecoveryService.cs`
- Create: `src/Nofarma.Application/Identity/Authentication/CurrentSession.cs`
- Test: `tests/Nofarma.UnitTests/Application/Identity/AuthenticationServiceTests.cs`
- Test: `tests/Nofarma.IntegrationTests/LocalIdentity/AuthenticationFlowTests.cs`

**Interfaces:**

- Produces: `AuthenticationService.SignInAsync(SignInRequest, CancellationToken)`.
- Produces: `RecoveryService.RecoverAsync(RecoveryRequest, CancellationToken)`.
- Produces: sessão administrativa com expiração por inactividade de 15 minutos.

- [ ] **Step 1: Escrever testes de autenticação que falham**

Cobrir credencial correcta, mensagem genérica para credencial errada, cinco falhas, bloqueio de 15 minutos, sucesso depois do período e reposição do contador. Usar relógio falso e armazenamento real ou fake comportamental sem afirmar chamadas de mocks.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~AuthenticationServiceTests --no-restore`

Expected: FAIL porque os casos de uso não existem.

- [ ] **Step 3: Implementar login e sessão**

Comparar credenciais mesmo quando o utilizador não existe através de um hash fictício previamente calculado para reduzir diferenças de tempo. Produzir sempre o erro público `Não foi possível iniciar sessão com os dados indicados.`. Guardar apenas o código interno em auditoria.

- [ ] **Step 4: Escrever testes de recuperação que falham**

Confirmar utilização única, invalidação de sessões administrativas, nova palavra-passe, novo código e auditoria. Confirmar que o código antigo falha depois da recuperação.

- [ ] **Step 5: Implementar recuperação transaccional**

Validar o código com comparação em tempo constante. Rodar código e credencial na mesma transacção. Mostrar o novo código apenas na resposta de sucesso.

- [ ] **Step 6: Executar testes**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~Identity.Authentication --no-restore`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~AuthenticationFlowTests --no-restore`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/Nofarma.Application/Identity/Authentication src/Nofarma.Infrastructure tests
git commit -m "feat: add offline authentication and recovery"
```

### Task 7: Administração de utilizadores e autorização

**Files:**

- Create: `src/Nofarma.Application/Identity/Authorization/AuthorizationService.cs`
- Create: `src/Nofarma.Application/Identity/Authorization/AuthorizationException.cs`
- Create: `src/Nofarma.Application/Identity/Users/CreateUserRequest.cs`
- Create: `src/Nofarma.Application/Identity/Users/UserAdministrationService.cs`
- Test: `tests/Nofarma.UnitTests/Application/Identity/AuthorizationServiceTests.cs`
- Test: `tests/Nofarma.IntegrationTests/LocalIdentity/UserAdministrationTests.cs`

**Interfaces:**

- Produces: `AuthorizationService.EnsureAllowed(LocalSession session, Permission permission)`.
- Produces: `UserAdministrationService.CreateAsync(...)`, `DeactivateAsync(...)`, `UnlockCashierAsync(...)`, `ChangeRoleAsync(...)`.

- [ ] **Step 1: Escrever o teste de autorização que falha**

Usar uma sessão real de Caixa e exigir `ManageUsers`. Confirmar `AuthorizationException`. Exigir `LockOwnSession` e confirmar sucesso.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~AuthorizationServiceTests --no-restore`

Expected: FAIL porque o serviço não existe.

- [ ] **Step 3: Implementar autorização e administração**

Autorizar antes de carregar ou alterar dados protegidos. Criar Caixa apenas com PIN válido. Criar outros perfis apenas com palavra-passe válida. Uma mudança de Caixa para outro perfil exige nova palavra-passe. Uma mudança para Caixa exige novo PIN.

- [ ] **Step 4: Escrever e executar testes SQLite**

Confirmar unicidade de login, auditoria de criação, desactivação, mudança de função e desbloqueio. Confirmar que um Caixa não consegue chamar nenhum caso de uso administrativo.

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~UserAdministrationTests --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Application/Identity/Authorization src/Nofarma.Application/Identity/Users src/Nofarma.Infrastructure tests
git commit -m "feat: administer users with explicit permissions"
```

### Task 8: Composição da aplicação Windows

**Files:**

- Modify: `src/Nofarma.Desktop/App.xaml`
- Modify: `src/Nofarma.Desktop/App.xaml.cs`
- Modify: `src/Nofarma.Desktop/MainWindow.xaml`
- Modify: `src/Nofarma.Desktop/MainWindow.xaml.cs`
- Modify: `src/Nofarma.Infrastructure/Composition/ServiceCollectionExtensions.cs`
- Create: `src/Nofarma.Desktop/Composition/ServiceCollectionExtensions.cs`
- Create: `src/Nofarma.Desktop/Services/NavigationService.cs`
- Test: `tests/Nofarma.IntegrationTests/LocalIdentity/ApplicationStartupTests.cs`

**Interfaces:**

- Produces: um `ServiceProvider` único com relógio, contexto, stores e casos de uso.
- Produces: navegação inicial para assistente ou login com base no estado persistido.

- [ ] **Step 1: Escrever o teste de arranque que falha**

Construir o contentor contra SQLite temporário. Confirmar que uma base nova devolve destino `Setup` e uma base configurada devolve `Login`.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~ApplicationStartupTests --no-restore`

Expected: FAIL porque a composição ainda não existe.

- [ ] **Step 3: Implementar composição e arranque**

Usar `Microsoft.Extensions.DependencyInjection` e o caminho em LocalApplicationData. Aplicar migrações antes de mostrar a primeira página. Nunca bloquear a thread da interface com I/O síncrono.

- [ ] **Step 4: Executar teste e build WinUI**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter FullyQualifiedName~ApplicationStartupTests --no-restore`

Run: `dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj --configuration Debug --no-restore -warnaserror`

Expected: PASS e build sem avisos.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Desktop src/Nofarma.Infrastructure tests/Nofarma.IntegrationTests
git commit -m "feat: compose local desktop services"
```

### Task 9: Tokens e assistente visual fiel ao atlas

**Files:**

- Create: `src/Nofarma.Desktop/Design/ThemeResources.xaml`
- Create: `src/Nofarma.Desktop/ViewModels/SetupWizardViewModel.cs`
- Create: `src/Nofarma.Desktop/Views/SetupWizardPage.xaml`
- Create: `src/Nofarma.Desktop/Views/SetupWizardPage.xaml.cs`
- Modify: `src/Nofarma.Desktop/App.xaml`
- Test: `tests/Nofarma.UnitTests/Application/Identity/SetupValidationTests.cs`

**Interfaces:**

- Consumes: `SetupService.ConfigureAsync`.
- Produces: cinco passos visuais e código de recuperação mostrado uma vez.

- [ ] **Step 1: Escrever testes de validação que falham**

Testar NIF vazio, fuso inválido, login vazio, palavra-passe fraca e confirmação diferente. O resultado devolve erros associados aos nomes dos campos.

- [ ] **Step 2: Executar e confirmar a falha**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~SetupValidationTests --no-restore`

Expected: FAIL porque a validação ainda não existe.

- [ ] **Step 3: Implementar validação e view model**

Manter dados ao voltar. Impedir duplo clique durante a conclusão. Não guardar a palavra-passe no estado depois da criação. Obrigar o administrador a confirmar que guardou o código antes de concluir.

- [ ] **Step 4: Implementar XAML conforme a prancha 1**

Usar `docs/design/previews/01-setup-and-access.png` como referência. Criar barra azul superior, passos à esquerda, formulário à direita, tema claro, Segoe UI, raio máximo de 12, foco visível, uma acção principal e texto `Preparação offline`.

Não copiar dados fictícios da imagem. Não usar gradiente, sombra larga, cruz médica, ícones emoji ou cartões aninhados.

- [ ] **Step 5: Build e inspecção visual**

Run: `dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj --configuration Debug --no-restore -warnaserror`

Executar a aplicação numa base vazia, capturar o assistente e comparar com a prancha 1 em 1366 por 768.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Desktop tests/Nofarma.UnitTests
git commit -m "feat: add pharmacy setup wizard"
```

### Task 10: Login, shell, utilizadores e configuração

**Files:**

- Create: `src/Nofarma.Desktop/ViewModels/LoginViewModel.cs`
- Create: `src/Nofarma.Desktop/ViewModels/UsersViewModel.cs`
- Create: `src/Nofarma.Desktop/Models/NavigationItem.cs`
- Create: `src/Nofarma.Desktop/Views/LoginPage.xaml`
- Create: `src/Nofarma.Desktop/Views/LoginPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/AppShellPage.xaml`
- Create: `src/Nofarma.Desktop/Views/AppShellPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/UsersPage.xaml`
- Create: `src/Nofarma.Desktop/Views/UsersPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/SettingsPage.xaml`
- Create: `src/Nofarma.Desktop/Views/SettingsPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/ModuleEmptyPage.xaml`
- Create: `src/Nofarma.Desktop/Views/ModuleEmptyPage.xaml.cs`

**Interfaces:**

- Consumes: autenticação, autorização e administração de utilizadores.
- Produces: navegação completa e estados vazios honestos para módulos ainda não implementados.

- [ ] **Step 1: Implementar o ecrã de acesso da prancha 1**

Apresentar perfis locais. Mostrar palavra-passe para todos os perfis excepto Caixa. Mostrar teclado numérico e PIN para Caixa. Usar a mensagem genérica de falha e mostrar o tempo restante do bloqueio sem revelar credenciais.

- [ ] **Step 2: Implementar o shell das pranchas 2 a 4**

Criar `NavigationView` com Painel, Vendas, Facturas, Produtos, Stock, Compras, Fornecedores, Caixa, Relatórios, Auditoria, Utilizadores e Configurações. A barra superior mostra farmácia, utilizador, turno, internet, sincronização, impressora, licença e alertas.

- [ ] **Step 3: Implementar Utilizadores e Configurações funcionais**

Seguir `04-reports-audit-users-settings.png`. Mostrar lista real de utilizadores, função, estado, tipo de credencial e última entrada. Mostrar matriz real de permissões. Permitir criar Caixa com PIN e outros perfis com palavra-passe.

Na configuração DGCI, deixar URLs vazias e configuráveis. Mostrar que a farmácia é responsável pela autorização e que a factura fiscal está bloqueada sem configuração e licença válidas.

- [ ] **Step 4: Criar estados vazios para os módulos posteriores**

Cada destino ainda sem domínio funcional mostra a estrutura visual correspondente do atlas, sem métricas, vendas, stock ou valores inventados. O estado explica que o módulo será activado na fase respectiva e oferece apenas acções já suportadas.

- [ ] **Step 5: Validar acessibilidade e resolução**

Confirmar teclado, foco, texto a 1366 por 768, contraste, alvos grandes, símbolos acompanhados de texto e ausência de conteúdo cortado. Confirmar que a redução de movimento elimina deslocações.

- [ ] **Step 6: Build e teste manual do percurso**

Executar: configuração, reinício, login administrativo, criação de Caixa, troca de utilizador, login com PIN, tentativa de abrir Utilizadores e bloqueio correcto.

- [ ] **Step 7: Commit**

```powershell
git add src/Nofarma.Desktop
git commit -m "feat: add local identity desktop experience"
```

### Task 11: Verificação, documentação e publicação

**Files:**

- Modify: `docs/development/verification.md`
- Create: `docs/development/local-identity.md`
- Modify: `README.md`
- Modify: `postman/Nofarma.postman_collection.json` only if a local identity API endpoint is deliberately added, otherwise leave unchanged.

- [ ] **Step 1: Documentar operação segura**

Explicar caminhos locais, configuração, recuperação, criação de Caixa, bloqueios e forma de apagar apenas dados de desenvolvimento. Não documentar valores reais de credenciais.

- [ ] **Step 2: Executar verificação completa**

Run: `dotnet restore Nofarma.slnx --locked-mode`

Run: `dotnet format Nofarma.slnx --verify-no-changes --no-restore`

Run: `dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror`

Run: `dotnet test Nofarma.slnx --configuration Release --no-build`

Expected: todas as operações terminam com código 0, sem avisos e sem testes falhados.

- [ ] **Step 3: Executar revisão de segurança**

Pesquisar chaves, tokens, palavras-passe, PIN, ficheiros SQLite, peppers, certificados e backups. Confirmar que `.gitignore` cobre `.db`, `.sqlite`, `.env`, `secrets`, backups e dados de execução.

- [ ] **Step 4: Verificar o diff e o estado do ramo**

Run: `git diff --check`

Run: `git status --short`

Run: `git log --oneline --decorate -12`

- [ ] **Step 5: Commit e publicação**

```powershell
git add README.md docs postman .gitignore
git commit -m "docs: verify local identity workflow"
git push
```

## Revisão do plano

### Cobertura da especificação

- Configuração offline: Tasks 4, 5, 8 e 9.
- Administrador principal: Tasks 1, 5 e 9.
- Palavra-passe e PIN: Tasks 1, 3, 6, 7 e 10.
- Bloqueio de cinco tentativas por 15 minutos: Tasks 1 e 6.
- Recuperação única: Tasks 3, 5 e 6.
- Funções e permissões: Tasks 2, 7 e 10.
- Auditoria imutável: Tasks 4, 5, 6 e 7.
- SQLite e transacções: Tasks 4 e 5.
- Interface fiel ao atlas: Tasks 9 e 10.
- Sem dados fictícios apresentados como reais: Task 10.
- Verificação final e segurança: Task 11.

### Decisões explícitas

- PBKDF2-HMAC-SHA256 usa 600000 iterações, salt de 16 bytes e hash de 32 bytes.
- O pepper tem 32 bytes, fica fora de SQLite e é protegido por DPAPI para o utilizador Windows actual.
- O piloto não cifra toda a base SQLite nesta fase. A protecção integral do backup e o restauro do pepper entram no plano de backups. O directório local usa as permissões do perfil Windows e não é partilhado.
- A interface completa de navegação é criada agora, mas os módulos comerciais posteriores mostram estados vazios e não dados simulados.
- O atlas orienta a composição visual de todas as páginas. A funcionalidade entra de acordo com a sequência do roteiro.
