# NôFarma Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Criar uma fundação .NET 10 compilável, testada e segura para a aplicação Windows, API, sincronização, portal e persistência do NôFarma.

**Architecture:** A solução separa domínio, aplicação, contratos, infraestrutura e superfícies executáveis. Dependências apontam para o centro. A fundação inclui apenas capacidades transversais e shells verificáveis, sem antecipar regras de vendas ou stock.

**Tech Stack:** .NET SDK 10.0.302, C# 14, Windows App SDK 2.3.1, ASP.NET Core 10.0.10, Entity Framework Core 10.0.10, Npgsql 10.0.3, xUnit v3 3.2.2, Microsoft.NET.Test.Sdk 18.8.1, PostgreSQL 18, Docker Compose e GitHub Actions.

## Global Constraints

- Marca visível: `NôFarma by ABIPTOM`.
- Namespaces: `Nofarma`.
- `Nullable` e analisadores activados.
- Avisos do compilador tratados como erros no código próprio.
- `net10.0` em bibliotecas, API, sincronização, portal e testes.
- `net10.0-windows10.0.19041.0` no projecto WinUI.
- Windows mínimo declarado: `10.0.19045.0`, Windows 10 22H2.
- Pacotes geridos centralmente em `Directory.Packages.props`.
- Nenhum projecto recebe um pacote sem uso comprovado por esta entrega.
- Nenhum segredo tem valor por omissão em ficheiros versionados.
- Commits pequenos e limitados aos ficheiros de cada tarefa.

---

## Mapa de ficheiros

### Raiz

- `global.json`: fixa o SDK e a política de roll-forward.
- `Directory.Build.props`: regras comuns de compilação e análise.
- `Directory.Packages.props`: versões NuGet centrais.
- `.editorconfig`: estilo C# e severidade mínima.
- `Nofarma.slnx`: solução principal.

### Bibliotecas

- `src/Nofarma.Domain/`: entidades, value objects e regras puras.
- `src/Nofarma.Application/`: casos de uso e portas.
- `src/Nofarma.Contracts/`: DTOs versionados da API e sincronização.
- `src/Nofarma.Infrastructure/`: SQLite, PostgreSQL, ficheiros, relógio e integrações.

### Executáveis

- `src/Nofarma.Desktop/`: aplicação WinUI 3.
- `src/Nofarma.Api/`: API ASP.NET Core.
- `src/Nofarma.AdminWeb/`: portal Blazor.
- `src/Nofarma.Sync/`: worker de sincronização e tarefas locais.

### Testes

- `tests/Nofarma.UnitTests/`: domínio e aplicação.
- `tests/Nofarma.IntegrationTests/`: API, persistência e composição.
- `tests/Nofarma.ArchitectureTests/`: fronteiras de dependência.

### Operação

- `deploy/compose.dev.yml`: PostgreSQL de desenvolvimento.
- `deploy/.env.example`: nomes de variáveis sem valores secretos.
- `.github/workflows/ci.yml`: restore, format, build e testes.
- `.github/dependabot.yml`: actualizações NuGet e GitHub Actions.
- `postman/Nofarma.postman_collection.json`: pedido de saúde.
- `postman/environments/local.postman_environment.json`: URL local sem credenciais.

---

### Task 1: Fixar toolchain e criar a solução

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.editorconfig`
- Create: `Nofarma.slnx`
- Create: `src/Nofarma.Domain/Nofarma.Domain.csproj`
- Create: `src/Nofarma.Application/Nofarma.Application.csproj`
- Create: `src/Nofarma.Contracts/Nofarma.Contracts.csproj`
- Create: `src/Nofarma.Infrastructure/Nofarma.Infrastructure.csproj`
- Create: `src/Nofarma.Api/Nofarma.Api.csproj`
- Create: `src/Nofarma.AdminWeb/Nofarma.AdminWeb.csproj`
- Create: `src/Nofarma.Sync/Nofarma.Sync.csproj`
- Create: `src/Nofarma.Desktop/Nofarma.Desktop.csproj`
- Create: `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj`
- Create: `tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj`
- Create: `tests/Nofarma.ArchitectureTests/Nofarma.ArchitectureTests.csproj`

**Interfaces:**
- Consumes: .NET SDK 10.0.302 instalado.
- Produces: solução `Nofarma.slnx` e projectos com nomes estáveis usados por todas as tarefas seguintes.

- [ ] **Step 1: Verificar pré-requisitos sem alterar o sistema**

Run:

```powershell
dotnet --version
dotnet new list winui
docker version
docker compose version
```

Expected:

- `dotnet --version` devolve `10.0.302`.
- O template oficial WinUI pode ainda não existir.
- Docker pode falhar porque não está actualmente acessível no terminal. Registar este resultado sem o esconder.

- [ ] **Step 2: Instalar apenas o template oficial WinUI se estiver ausente**

Run:

```powershell
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
dotnet new list winui
```

Expected: `WinUI Blank App` da Microsoft aparece com short name `winui`.

- [ ] **Step 3: Criar os ficheiros de toolchain**

`global.json`:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.10" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.10" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.10" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.10" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.7705" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools.WinApp" Version="0.3.1" />
    <PackageVersion Include="Microsoft.WindowsAppSDK" Version="2.3.1" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
```

`.editorconfig`:

```ini
root = true

[*.cs]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
indent_size = 4
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
csharp_style_namespace_declarations = file_scoped:warning
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion
dotnet_diagnostic.IDE0005.severity = warning
```

- [ ] **Step 4: Criar solução e projectos com comandos reproduzíveis**

Run:

```powershell
dotnet new sln -n Nofarma --format slnx
dotnet new classlib -n Nofarma.Domain -o src/Nofarma.Domain -f net10.0
dotnet new classlib -n Nofarma.Application -o src/Nofarma.Application -f net10.0
dotnet new classlib -n Nofarma.Contracts -o src/Nofarma.Contracts -f net10.0
dotnet new classlib -n Nofarma.Infrastructure -o src/Nofarma.Infrastructure -f net10.0
dotnet new webapi -n Nofarma.Api -o src/Nofarma.Api -f net10.0 --no-openapi --no-https
dotnet new blazor -n Nofarma.AdminWeb -o src/Nofarma.AdminWeb -f net10.0 --interactivity Server --no-https
dotnet new worker -n Nofarma.Sync -o src/Nofarma.Sync -f net10.0
dotnet new winui -n Nofarma.Desktop -o src/Nofarma.Desktop -tfm net10.0 -tpmv 10.0.19041.0 -w 2.3.1
dotnet new xunit -n Nofarma.UnitTests -o tests/Nofarma.UnitTests -f net10.0
dotnet new xunit -n Nofarma.IntegrationTests -o tests/Nofarma.IntegrationTests -f net10.0
dotnet new xunit -n Nofarma.ArchitectureTests -o tests/Nofarma.ArchitectureTests -f net10.0
dotnet sln Nofarma.slnx add (Get-ChildItem src,tests -Recurse -Filter *.csproj | Select-Object -ExpandProperty FullName)
```

Expected: onze projectos aparecem em `dotnet sln Nofarma.slnx list`.

- [ ] **Step 5: Normalizar referências de pacotes dos testes**

Cada projecto de testes contém:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit.v3" />
  <PackageReference Include="xunit.runner.visualstudio">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

Remover versões geradas dentro dos `.csproj` porque as versões pertencem a `Directory.Packages.props`.

- [ ] **Step 6: Executar o primeiro build**

Run:

```powershell
dotnet restore Nofarma.slnx
dotnet build Nofarma.slnx --no-restore -warnaserror
dotnet test Nofarma.slnx --no-build
```

Expected: build e testes dos templates passam. Se o WinUI gerar ficheiros incompatíveis com `net10.0-windows10.0.19041.0`, ajustar apenas o `TargetFramework` e `SupportedOSPlatformVersion` do projecto Desktop.

- [ ] **Step 7: Commit**

```powershell
git add global.json Directory.Build.props Directory.Packages.props .editorconfig Nofarma.slnx src tests
git commit -m "build: scaffold Nofarma solution"
```

---

### Task 2: Aplicar fronteiras de dependência

**Files:**
- Modify: `src/Nofarma.Application/Nofarma.Application.csproj`
- Modify: `src/Nofarma.Infrastructure/Nofarma.Infrastructure.csproj`
- Modify: `src/Nofarma.Api/Nofarma.Api.csproj`
- Modify: `src/Nofarma.AdminWeb/Nofarma.AdminWeb.csproj`
- Modify: `src/Nofarma.Sync/Nofarma.Sync.csproj`
- Modify: `src/Nofarma.Desktop/Nofarma.Desktop.csproj`
- Test: `tests/Nofarma.ArchitectureTests/ProjectDependencyTests.cs`
- Modify: `tests/Nofarma.ArchitectureTests/Nofarma.ArchitectureTests.csproj`

**Interfaces:**
- Consumes: assemblies criados na Task 1.
- Produces: grafo de referências estável e testes que impedem dependências para fora do centro.

- [ ] **Step 1: Escrever o teste de arquitectura que falha**

`ProjectDependencyTests.cs`:

```csharp
using System.Reflection;

namespace Nofarma.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    public static TheoryData<string, string[]> CoreProjects => new()
    {
        { "Nofarma.Domain", [] },
        { "Nofarma.Contracts", [] },
        { "Nofarma.Application", ["Nofarma.Contracts", "Nofarma.Domain"] }
    };

    [Theory]
    [MemberData(nameof(CoreProjects))]
    public void CoreProjectsReferenceOnlyAllowedNofarmaProjects(
        string assemblyName,
        string[] allowedReferences)
    {
        Assembly assembly = Assembly.Load(assemblyName);
        string[] actual = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Nofarma.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] forbidden = actual
            .Except(allowedReferences, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(forbidden);
    }
}
```

- [ ] **Step 2: Executar o teste e confirmar falha de carregamento ou referências ausentes**

Run:

```powershell
dotnet test tests/Nofarma.ArchitectureTests/Nofarma.ArchitectureTests.csproj --filter ProjectDependencyTests
```

Expected: FAIL enquanto os projectos não estão referenciados pelo teste e as dependências não foram configuradas.

- [ ] **Step 3: Adicionar referências exactas**

Run:

```powershell
dotnet add src/Nofarma.Application reference src/Nofarma.Domain src/Nofarma.Contracts
dotnet add src/Nofarma.Infrastructure reference src/Nofarma.Domain src/Nofarma.Application src/Nofarma.Contracts
dotnet add src/Nofarma.Api reference src/Nofarma.Application src/Nofarma.Infrastructure src/Nofarma.Contracts
dotnet add src/Nofarma.AdminWeb reference src/Nofarma.Contracts
dotnet add src/Nofarma.Sync reference src/Nofarma.Application src/Nofarma.Infrastructure src/Nofarma.Contracts
dotnet add src/Nofarma.Desktop reference src/Nofarma.Application src/Nofarma.Infrastructure src/Nofarma.Contracts
dotnet add tests/Nofarma.ArchitectureTests reference src/Nofarma.Domain src/Nofarma.Application src/Nofarma.Contracts
```

Não adicionar referência de Domain para qualquer projecto. Não adicionar Infrastructure ao AdminWeb.

- [ ] **Step 4: Executar teste e solução**

Run:

```powershell
dotnet test tests/Nofarma.ArchitectureTests/Nofarma.ArchitectureTests.csproj --filter ProjectDependencyTests
dotnet build Nofarma.slnx -warnaserror
```

Expected: PASS e build sem avisos.

- [ ] **Step 5: Commit**

```powershell
git add src tests/Nofarma.ArchitectureTests
git commit -m "test: enforce project dependency boundaries"
```

---

### Task 3: Criar primitivas de domínio seguras

**Files:**
- Create: `src/Nofarma.Domain/Common/EntityId.cs`
- Create: `src/Nofarma.Domain/Common/Money.cs`
- Create: `src/Nofarma.Domain/Common/UtcInstant.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Common/MoneyTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Common/EntityIdTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Common/UtcInstantTests.cs`
- Modify: `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj`

**Interfaces:**
- Consumes: nenhum serviço externo.
- Produces: `EntityId.New()`, `Money.Xof(long)`, `Money.Add(Money)` e `UtcInstant.From(DateTimeOffset)`.

- [ ] **Step 1: Escrever testes que falham**

`MoneyTests.cs`:

```csharp
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Common;

public sealed class MoneyTests
{
    [Fact]
    public void XofPreservesIntegerAmount()
    {
        Money money = Money.Xof(6_250);

        Assert.Equal(6_250, money.Amount);
        Assert.Equal("XOF", money.Currency);
    }

    [Fact]
    public void AddRejectsDifferentCurrency()
    {
        Money xof = Money.Xof(100);
        Money other = new(100, "EUR");

        Assert.Throws<InvalidOperationException>(() => xof.Add(other));
    }

    [Fact]
    public void AddUsesCheckedIntegerArithmetic()
    {
        Money maximum = Money.Xof(long.MaxValue);

        Assert.Throws<OverflowException>(() => maximum.Add(Money.Xof(1)));
    }
}
```

`EntityIdTests.cs`:

```csharp
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Common;

public sealed class EntityIdTests
{
    [Fact]
    public void NewNeverReturnsEmptyGuid()
    {
        EntityId id = EntityId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }
}
```

`UtcInstantTests.cs`:

```csharp
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Common;

public sealed class UtcInstantTests
{
    [Fact]
    public void FromNormalizesOffsetToUtc()
    {
        UtcInstant instant = UtcInstant.From(new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.FromHours(1)));

        Assert.Equal(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero), instant.Value);
    }
}
```

- [ ] **Step 2: Executar e confirmar falha de compilação**

Run:

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Domain.Common"
```

Expected: FAIL porque os tipos ainda não existem.

- [ ] **Step 3: Implementar o mínimo**

`EntityId.cs`:

```csharp
namespace Nofarma.Domain.Common;

public readonly record struct EntityId(Guid Value)
{
    public static EntityId New() => new(Guid.NewGuid());
}
```

`Money.cs`:

```csharp
namespace Nofarma.Domain.Common;

public readonly record struct Money
{
    public Money(long amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public long Amount { get; }
    public string Currency { get; }

    public static Money Xof(long amount) => new(amount, "XOF");

    public Money Add(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Currencies must match.");
        }

        return new Money(checked(Amount + other.Amount), Currency);
    }
}
```

`UtcInstant.cs`:

```csharp
namespace Nofarma.Domain.Common;

public readonly record struct UtcInstant
{
    private UtcInstant(DateTimeOffset value) => Value = value;

    public DateTimeOffset Value { get; }

    public static UtcInstant From(DateTimeOffset value) => new(value.ToUniversalTime());
}
```

- [ ] **Step 4: Executar testes e build**

Run:

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Domain.Common"
dotnet build Nofarma.slnx -warnaserror
```

Expected: todos os testes passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Domain tests/Nofarma.UnitTests
git commit -m "feat: add core domain value types"
```

---

### Task 4: Definir contratos de diagnóstico

**Files:**
- Create: `src/Nofarma.Contracts/Diagnostics/HealthResponse.cs`
- Create: `src/Nofarma.Application/Abstractions/IUtcClock.cs`
- Create: `src/Nofarma.Infrastructure/Time/SystemUtcClock.cs`
- Test: `tests/Nofarma.UnitTests/Infrastructure/Time/SystemUtcClockTests.cs`
- Modify: `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj`

**Interfaces:**
- Consumes: `UtcInstant` da Task 3.
- Produces: `IUtcClock.GetCurrentInstant()` e `HealthResponse` usados pela API, Desktop, Sync e portal.

- [ ] **Step 1: Escrever o teste do relógio**

```csharp
using Nofarma.Infrastructure.Time;

namespace Nofarma.UnitTests.Infrastructure.Time;

public sealed class SystemUtcClockTests
{
    [Fact]
    public void GetCurrentInstant_returns_zero_offset()
    {
        var clock = new SystemUtcClock();

        DateTimeOffset value = clock.GetCurrentInstant().Value;

        Assert.Equal(TimeSpan.Zero, value.Offset);
        Assert.InRange(value, DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow.AddSeconds(2));
    }
}
```

- [ ] **Step 2: Executar e confirmar falha**

Run:

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter SystemUtcClockTests
```

Expected: FAIL porque o relógio não existe.

- [ ] **Step 3: Implementar contratos e relógio**

`IUtcClock.cs`:

```csharp
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface IUtcClock
{
    UtcInstant GetCurrentInstant();
}
```

`SystemUtcClock.cs`:

```csharp
using Nofarma.Application.Abstractions;
using Nofarma.Domain.Common;

namespace Nofarma.Infrastructure.Time;

public sealed class SystemUtcClock : IUtcClock
{
    public UtcInstant GetCurrentInstant() => UtcInstant.From(DateTimeOffset.UtcNow);
}
```

`HealthResponse.cs`:

```csharp
namespace Nofarma.Contracts.Diagnostics;

public sealed record HealthResponse(
    string Service,
    string Status,
    string Version,
    DateTimeOffset CheckedAtUtc);
```

- [ ] **Step 4: Adicionar referências de teste e validar**

Run:

```powershell
dotnet add tests/Nofarma.UnitTests reference src/Nofarma.Domain src/Nofarma.Application src/Nofarma.Infrastructure
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter SystemUtcClockTests
dotnet build Nofarma.slnx -warnaserror
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Contracts src/Nofarma.Application src/Nofarma.Infrastructure tests/Nofarma.UnitTests
git commit -m "feat: add diagnostics contracts and UTC clock"
```

---

### Task 5: Criar API de saúde testada

**Files:**
- Modify: `src/Nofarma.Api/Program.cs`
- Create: `src/Nofarma.Api/Composition/ServiceCollectionExtensions.cs`
- Test: `tests/Nofarma.IntegrationTests/Api/HealthEndpointTests.cs`
- Modify: `tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj`

**Interfaces:**
- Consumes: `IUtcClock` e `HealthResponse` da Task 4.
- Produces: `GET /health/live` com HTTP 200 e JSON estável.

- [ ] **Step 1: Escrever teste de integração que falha**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nofarma.Contracts.Diagnostics;

namespace Nofarma.IntegrationTests.Api;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_returns_service_identity()
    {
        HttpResponseMessage response = await _client.GetAsync("/health/live");
        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Nofarma.Api", body.Service);
        Assert.Equal("Healthy", body.Status);
        Assert.Equal(TimeSpan.Zero, body.CheckedAtUtc.Offset);
    }
}
```

- [ ] **Step 2: Adicionar pacote de teste e confirmar falha 404**

`Nofarma.IntegrationTests.csproj` recebe:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  <ProjectReference Include="..\..\src\Nofarma.Api\Nofarma.Api.csproj" />
  <ProjectReference Include="..\..\src\Nofarma.Contracts\Nofarma.Contracts.csproj" />
</ItemGroup>
```

Run:

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter HealthEndpointTests
```

Expected: FAIL com 404.

- [ ] **Step 3: Implementar composição e endpoint**

`ServiceCollectionExtensions.cs`:

```csharp
using Nofarma.Application.Abstractions;
using Nofarma.Infrastructure.Time;

namespace Nofarma.Api.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNofarmaFoundation(this IServiceCollection services)
    {
        services.AddSingleton<IUtcClock, SystemUtcClock>();
        return services;
    }
}
```

`Program.cs`:

```csharp
using System.Reflection;
using Nofarma.Api.Composition;
using Nofarma.Application.Abstractions;
using Nofarma.Contracts.Diagnostics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddNofarmaFoundation();

WebApplication app = builder.Build();

app.MapGet("/health/live", (IUtcClock clock) =>
{
    string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
    return Results.Ok(new HealthResponse(
        "Nofarma.Api",
        "Healthy",
        version,
        clock.GetCurrentInstant().Value));
});

app.Run();

public partial class Program
{
}
```

- [ ] **Step 4: Executar testes e API local**

Run:

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter HealthEndpointTests
dotnet run --project src/Nofarma.Api --urls http://127.0.0.1:5080
```

Em outro terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:5080/health/live
```

Expected: teste PASS e resposta com `service`, `status`, `version` e `checkedAtUtc`.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Api tests/Nofarma.IntegrationTests
git commit -m "feat: add API live health endpoint"
```

---

### Task 6: Criar shells Desktop, Sync e portal

**Files:**
- Modify: `src/Nofarma.Desktop/MainWindow.xaml`
- Modify: `src/Nofarma.Desktop/MainWindow.xaml.cs`
- Modify: `src/Nofarma.Sync/Worker.cs`
- Modify: `src/Nofarma.AdminWeb/Components/Pages/Home.razor`
- Test: `tests/Nofarma.UnitTests/Composition/SurfaceIdentityTests.cs`
- Create: `src/Nofarma.Contracts/Diagnostics/ServiceNames.cs`

**Interfaces:**
- Consumes: tokens visuais de `DESIGN.md` e contratos da Task 4.
- Produces: três superfícies identificáveis sem lógica de negócio duplicada.

- [ ] **Step 1: Escrever teste de nomes estáveis**

```csharp
using Nofarma.Contracts.Diagnostics;

namespace Nofarma.UnitTests.Composition;

public sealed class SurfaceIdentityTests
{
    [Fact]
    public void Service_names_are_stable()
    {
        Assert.Equal("Nofarma.Desktop", ServiceNames.Desktop);
        Assert.Equal("Nofarma.Api", ServiceNames.Api);
        Assert.Equal("Nofarma.AdminWeb", ServiceNames.AdminWeb);
        Assert.Equal("Nofarma.Sync", ServiceNames.Sync);
    }
}
```

- [ ] **Step 2: Executar e confirmar falha**

Run:

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter SurfaceIdentityTests
```

Expected: FAIL porque `ServiceNames` não existe.

- [ ] **Step 3: Implementar contrato e shells mínimos**

`ServiceNames.cs`:

```csharp
namespace Nofarma.Contracts.Diagnostics;

public static class ServiceNames
{
    public const string Desktop = "Nofarma.Desktop";
    public const string Api = "Nofarma.Api";
    public const string AdminWeb = "Nofarma.AdminWeb";
    public const string Sync = "Nofarma.Sync";
}
```

`MainWindow.xaml`:

```xml
<Window
    x:Class="Nofarma.Desktop.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="NôFarma">
    <Grid Background="#F5F8FC">
        <StackPanel
            HorizontalAlignment="Center"
            VerticalAlignment="Center"
            Spacing="8">
            <TextBlock
                HorizontalAlignment="Center"
                FontFamily="Segoe UI Variable Display"
                FontSize="40"
                FontWeight="SemiBold"
                Foreground="#0B3B68"
                Text="NôFarma" />
            <TextBlock
                HorizontalAlignment="Center"
                FontFamily="Segoe UI Variable Text"
                FontSize="14"
                Foreground="#367C8D"
                Text="by ABIPTOM" />
            <TextBlock
                Margin="0,16,0,0"
                HorizontalAlignment="Center"
                FontFamily="Segoe UI Variable Text"
                FontSize="16"
                Foreground="#334155"
                Text="Fundação técnica instalada" />
        </StackPanel>
    </Grid>
</Window>
```

`MainWindow.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;

namespace Nofarma.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

Não criar navegação funcional nesta tarefa.

`Home.razor`:

```razor
@page "/"

<PageTitle>Portal NôFarma</PageTitle>

<main aria-labelledby="page-title">
    <p class="eyebrow">NôFarma by ABIPTOM</p>
    <h1 id="page-title">Portal NôFarma</h1>
    <p>Serviço disponível</p>
</main>
```

Não adicionar métricas fictícias.

`Worker.cs`:

```csharp
using Nofarma.Contracts.Diagnostics;

namespace Nofarma.Sync;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Service} iniciado", ServiceNames.Sync);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
```

Não iniciar sincronização real nesta tarefa.

- [ ] **Step 4: Validar shells**

Run:

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter SurfaceIdentityTests
dotnet build Nofarma.slnx -warnaserror
dotnet run --project src/Nofarma.AdminWeb --urls http://127.0.0.1:5081
```

Expected: teste PASS, build sem avisos e portal apresenta apenas o estado definido.

- [ ] **Step 5: Commit**

```powershell
git add src/Nofarma.Contracts src/Nofarma.Desktop src/Nofarma.Sync src/Nofarma.AdminWeb tests/Nofarma.UnitTests
git commit -m "feat: add executable surface shells"
```

---

### Task 7: Preparar PostgreSQL local e Postman sem segredos

**Files:**
- Create: `deploy/compose.dev.yml`
- Create: `deploy/.env.example`
- Create: `postman/Nofarma.postman_collection.json`
- Create: `postman/environments/local.postman_environment.json`
- Modify: `README.md`

**Interfaces:**
- Consumes: endpoint `/health/live` da Task 5.
- Produces: PostgreSQL local isolado e pedido Postman reproduzível.

- [ ] **Step 1: Criar Docker Compose com segredo obrigatório**

`deploy/compose.dev.yml`:

```yaml
services:
  postgres:
    image: postgres:18-alpine
    environment:
      POSTGRES_DB: nofarma
      POSTGRES_USER: nofarma
      POSTGRES_PASSWORD: ${NOFARMA_POSTGRES_PASSWORD:?Define NOFARMA_POSTGRES_PASSWORD}
    ports:
      - "127.0.0.1:5432:5432"
    volumes:
      - nofarma-postgres:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U nofarma -d nofarma"]
      interval: 5s
      timeout: 3s
      retries: 10
    restart: unless-stopped

volumes:
  nofarma-postgres:
```

`deploy/.env.example`:

```dotenv
NOFARMA_POSTGRES_PASSWORD=
```

- [ ] **Step 2: Validar Compose quando Docker estiver acessível**

Run:

```powershell
$env:NOFARMA_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString("N")
docker compose -f deploy/compose.dev.yml config
docker compose -f deploy/compose.dev.yml up -d postgres
docker compose -f deploy/compose.dev.yml ps
```

Expected: `postgres` fica `healthy` e só publica em `127.0.0.1`.

- [ ] **Step 3: Criar colecção Postman mínima**

A colecção usa Postman Collection v2.1 e contém um pedido:

```json
{
  "info": {
    "name": "NôFarma API",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "item": [
    {
      "name": "Diagnostics",
      "item": [
        {
          "name": "Live health",
          "request": {
            "method": "GET",
            "header": [],
            "url": "{{baseUrl}}/health/live"
          },
          "event": [
            {
              "listen": "test",
              "script": {
                "exec": [
                  "pm.test('status is 200', () => pm.response.to.have.status(200));",
                  "pm.test('service is Nofarma.Api', () => pm.expect(pm.response.json().service).to.eql('Nofarma.Api'));"
                ]
              }
            }
          ]
        }
      ]
    }
  ]
}
```

`postman/environments/local.postman_environment.json`:

```json
{
  "name": "NôFarma Local",
  "values": [
    {
      "key": "baseUrl",
      "value": "http://127.0.0.1:5080",
      "type": "default",
      "enabled": true
    }
  ],
  "_postman_variable_scope": "environment"
}
```

O ambiente não contém token ou palavra-passe.

- [ ] **Step 4: Documentar arranque seguro**

Adicionar ao `README.md`:

```markdown
## Desenvolvimento local

1. Copiar `deploy/.env.example` para `deploy/.env` e definir uma palavra-passe local forte.
2. Iniciar PostgreSQL com `docker compose -f deploy/compose.dev.yml --env-file deploy/.env up -d`.
3. Iniciar a API com `dotnet run --project src/Nofarma.Api --urls http://127.0.0.1:5080`.
4. Importar a colecção e o ambiente da pasta `postman/`.

Nunca guardar o ficheiro `deploy/.env` no Git.
```

- [ ] **Step 5: Verificar exclusões e segredos**

Run:

```powershell
git check-ignore -v deploy/.env
rg -n --hidden -g "!.git/**" -g "!.superpowers/**" "(ghp_|github_pat_|AKIA|BEGIN PRIVATE KEY|password\s*[:=]\s*[^$])" .
```

Expected: `deploy/.env` é ignorado e a pesquisa não encontra credenciais reais.

- [ ] **Step 6: Commit**

```powershell
git add deploy postman README.md
git commit -m "chore: add local services and Postman health check"
```

---

### Task 8: Criar CI, Dependabot e gate final

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `.github/dependabot.yml`
- Create: `docs/development/verification.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: solução e testes das Tasks 1 a 7.
- Produces: gate automático reproduzível em Windows e instrução única de verificação.

- [ ] **Step 1: Criar workflow CI**

`.github/workflows/ci.yml`:

```yaml
name: ci

on:
  pull_request:
  push:
    branches: [main]

permissions:
  contents: read

jobs:
  build-test:
    runs-on: windows-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: "**/packages.lock.json"
      - name: Restore
        run: dotnet restore Nofarma.slnx
      - name: Format
        run: dotnet format Nofarma.slnx --verify-no-changes --no-restore
      - name: Build
        run: dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror
      - name: Test
        run: dotnet test Nofarma.slnx --configuration Release --no-build --logger "trx;LogFileName=tests.trx"
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: "**/TestResults/*.trx"
          if-no-files-found: error
          retention-days: 14
```

- [ ] **Step 2: Activar lock files NuGet**

Adicionar a `Directory.Build.props`:

```xml
<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
<RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
```

Run:

```powershell
dotnet restore Nofarma.slnx --force-evaluate
```

Expected: cada projecto com pacotes recebe `packages.lock.json`.

- [ ] **Step 3: Configurar Dependabot**

`.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: "/"
    schedule:
      interval: weekly
    open-pull-requests-limit: 5
  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: weekly
    open-pull-requests-limit: 3
```

- [ ] **Step 4: Criar guia de verificação**

`docs/development/verification.md` contém os comandos exactos:

```powershell
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror
dotnet test Nofarma.slnx --configuration Release --no-build
git diff --check
git status --short
```

Expected: todos terminam com código zero e `git status --short` não mostra ficheiros gerados.

- [ ] **Step 5: Executar o gate completo local**

Run:

```powershell
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror
dotnet test Nofarma.slnx --configuration Release --no-build
git diff --check
```

Expected: tudo passa. Se Docker continuar indisponível, a verificação Compose permanece explicitamente não executada e impede declarar a infraestrutura local completa.

- [ ] **Step 6: Rever cobertura da especificação desta fundação**

Confirmar no diff:

1. Todos os projectos do mapa existem.
2. Domain e Contracts não dependem de outros projectos NôFarma.
3. A API responde em `/health/live`.
4. Desktop, Sync e AdminWeb compilam.
5. Postman não contém segredos.
6. PostgreSQL só publica em localhost.
7. CI usa SDK fixado e lock files.
8. `.superpowers`, `.env`, bases de dados, backups e certificados continuam ignorados.

- [ ] **Step 7: Commit e publicação**

```powershell
git add .github Directory.Build.props docs/development README.md
git add ':(glob)**/packages.lock.json'
git commit -m "ci: add foundation verification gates"
git push
```

---

## Verificação final do plano

Depois da Task 8, executar:

```powershell
dotnet --version
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror
dotnet test Nofarma.slnx --configuration Release --no-build
gh pr checks
git status -sb
```

Resultado necessário:

- SDK `10.0.302`.
- Build sem avisos.
- Todos os testes passam.
- Workflow do GitHub passa.
- Árvore de trabalho limpa.
- Nenhum ficheiro sensível versionado.
- A limitação do Docker local está resolvida ou declarada como bloqueio antes de iniciar persistência PostgreSQL.

## Próximo plano

Depois deste gate, criar e executar `docs/superpowers/plans/2026-07-27-nofarma-local-identity.md` para configuração da farmácia, dispositivo, utilizadores, funções, permissões e auditoria local.
