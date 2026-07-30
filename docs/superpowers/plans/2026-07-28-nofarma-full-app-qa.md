# NôFarma Full Application QA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Validar todas as páginas existentes do NôFarma de cima a baixo nos estados sem licença, licença QA válida, tolerância e só consulta, corrigindo defeitos reproduzíveis sem tocar nos dados reais do utilizador.

**Architecture:** O QA usa a compilação QA e a pasta `%LOCALAPPDATA%\ABIPTOM\Nofarma-QA`, separadas da aplicação real. Testes automatizados cobrem contratos e estados. A auditoria visual usa a aplicação WinUI real, capturas actuais a 1366 por 768 e uma matriz de evidência por página. Defeitos entram num ciclo TDD separado antes de nova captura.

**Tech Stack:** .NET 10, xUnit v3, WinUI 3, Computer Use com Windows Graphics Capture e UI Automation, PowerShell para gates, Markdown para evidência.

## Global Constraints

- Este plano depende da conclusão de `docs/superpowers/plans/2026-07-28-nofarma-signed-licensing.md`.
- Não tocar em `%LOCALAPPDATA%\ABIPTOM\Nofarma`; usar apenas `%LOCALAPPDATA%\ABIPTOM\Nofarma-QA`.
- Não automatizar nem registar palavras-passe, PINs ou códigos de recuperação.
- Não criar vendas, facturas oficiais ou dados fiscais fictícios na base real.
- Capturas actuais são guardadas em `artifacts/qa/full-app/` e ficam fora do Git quando contêm dados locais.
- O relatório versionado contém nomes dos passos, resultados, defeitos e limites, sem dados pessoais ou operacionais.
- Viewport físico: 1366 por 768; testar escala Windows 100 e 125 por cento quando disponível.
- Alvos interactivos mínimos: 44 por 44 píxeis.
- Texto: 200 por cento sem perda de acções essenciais.
- Não declarar WCAG integral a partir de capturas.
- Páginas planeadas ou vazias são marcadas `não implementada`; não são simuladas como concluídas.
- Cada defeito corrigido segue RED, GREEN, revisão e nova captura.

---

### Task 1: Inventário executável de páginas e matriz de estados

**Files:**
- Create: `tests/Nofarma.UnitTests/Desktop/ApplicationSurfaceInventoryTests.cs`
- Create: `docs/development/full-app-qa-matrix.md`
- Modify: `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj`

**Interfaces:**
- Consumes: destinos reais de `AppShellPage` e páginas WinUI.
- Produces: matriz única com `Setup`, `Login`, `Painel`, `Vendas`, `Facturas`, `Produtos`, `Stock`, `Compras`, `Fornecedores`, `Importação`, `Caixa`, `Relatórios`, `Auditoria`, `Utilizadores`, `Configurações` e `Licença`.

- [ ] **Step 1: Escrever teste falhado que inventaria superfícies reais**

```csharp
[Fact]
public void EveryNavigationDestinationHasAnExplicitQaClassification()
{
    string[] expected =
    [
        "Painel", "Vendas", "Facturas", "Produtos", "Stock", "Compras",
        "Fornecedores", "Importação", "Caixa", "Relatórios", "Auditoria",
        "Utilizadores", "Configurações", "Licença"
    ];
    Assert.Equal(expected, ApplicationSurfaceInventory.All.Select(x => x.Name));
    Assert.All(ApplicationSurfaceInventory.All, x => Assert.NotEqual(QaClassification.Unknown, x.Classification));
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~ApplicationSurfaceInventoryTests`

Expected: FAIL por inventário inexistente.

- [ ] **Step 3: Implementar inventário mínimo e documento**

Classificar `Produtos`, `Stock`, `Compras`, `Fornecedores`, `Importação`, `Caixa`, `Utilizadores`, `Configurações` e `Licença` como `Implemented`. Classificar `Painel` como `Partial`. Classificar `Vendas`, `Facturas`, `Relatórios` e `Auditoria` como `PlannedPlaceholder` enquanto continuarem em `ModuleEmptyPage`.

A matriz define para cada página: sem licença, válida, tolerância, só consulta, teclado, 200 por cento, 1366 por 768, captura esperada e operações proibidas.

- [ ] **Step 4: Executar GREEN**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~ApplicationSurfaceInventoryTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add tests/Nofarma.UnitTests/Desktop/ApplicationSurfaceInventoryTests.cs tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj docs/development/full-app-qa-matrix.md
git commit -m "test: inventory every Nofarma application surface"
```

### Task 2: Testes automatizados dos quatro estados de licença

**Files:**
- Create: `tests/Nofarma.UnitTests/Desktop/LicenseStateJourneyTests.cs`
- Create: `tests/Nofarma.UnitTests/TestSupport/Desktop/QaJourney.cs`
- Modify: `tests/Nofarma.UnitTests/Desktop/InventoryViewModelTests.cs`
- Modify: `tests/Nofarma.UnitTests/Desktop/CashViewModelTests.cs`
- Modify: `tests/Nofarma.UnitTests/Desktop/LicenseViewModelTests.cs`

**Interfaces:**
- Consumes: motor e política de licença concluídos.
- Produces: evidência automática de `Missing`, `Valid`, `Grace` e `ExpiredReadOnly` em cada operação implementada.

- [ ] **Step 1: Escrever testes de jornada antes de qualquer correcção**

```csharp
[Theory]
[InlineData(LicenseState.Missing, false)]
[InlineData(LicenseState.Valid, true)]
[InlineData(LicenseState.Grace, true)]
[InlineData(LicenseState.ExpiredReadOnly, false)]
public async Task ProtectedPagesNeverReportSuccessWhenWriteIsBlocked(
    LicenseState state,
    bool expectedWrite)
{
    QaJourney journey = await QaJourney.StartAsync(state);
    QaWriteResults results = await journey.AttemptAllImplementedWritesAsync();
    Assert.All(results.Items, item => Assert.Equal(expectedWrite, item.WasCommitted));
    Assert.All(results.BlockedItems, item => Assert.Contains("licença", item.UserMessage, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~LicenseStateJourneyTests`

Expected: FAIL nas páginas ou mensagens que ainda não obedecem à matriz.

- [ ] **Step 3: Corrigir apenas discrepâncias demonstradas**

Para cada falha, criar ou ajustar o view-model para preservar rascunhos, limpar `IsBusy`, não emitir mensagem de sucesso e expor código de recuperação localizado. Não alterar estados que já passam.

`QaJourney` compõe os view-models reais com stores temporários e uma implementação `FixedLicensedOperationPolicy`. `AttemptAllImplementedWritesAsync` devolve resultados literais para abertura de caixa, movimento manual, confirmação de stock, recepção de compra e confirmação de importação. Não inclui páginas planeadas.

- [ ] **Step 4: Executar GREEN e regressão**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~LicenseStateJourneyTests`

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~Desktop`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add tests/Nofarma.UnitTests/Desktop tests/Nofarma.UnitTests/TestSupport/Desktop src/Nofarma.Desktop
git commit -m "test: verify licensed and read-only journeys"
```

### Task 3: Robustez, acessibilidade estrutural e layout

**Files:**
- Create: `tests/Nofarma.Desktop.UiTests/Nofarma.Desktop.UiTests.csproj`
- Create: `tests/Nofarma.Desktop.UiTests/AccessibilitySurfaceTests.cs`
- Create: `tests/Nofarma.Desktop.UiTests/WinUiHarness.cs`
- Modify: `Nofarma.slnx`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/AppShellPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/ProductsPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/StockPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/PurchasesPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/SuppliersPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/InventoryImportPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/CashPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/UsersPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/SettingsPage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Views/LicensePage.xaml`
- Potentially modify after a demonstrated RED: `src/Nofarma.Desktop/Design/ThemeResources.xaml`

**Interfaces:**
- Consumes: XAML e nomes de automação reais.
- Produces: garantias estruturais para labels, automação, alvos, scroll e estados sem depender apenas de pesquisa textual.

- [ ] **Step 1: Escrever testes de comportamento acessível**

Montar as páginas num host WinUI de teste e consultar a árvore de automação. Para cada controlo accionável, confirmar nome não vazio e rectângulo mínimo 44 por 44. Para cada página com formulário, confirmar ordem de tabulação e foco após erro.

`Nofarma.Desktop.UiTests.csproj` usa `net10.0-windows10.0.19041.0`, x64, xUnit v3, `UseWinUI=true` e referência `Nofarma.Desktop`. `WinUiHarness.RenderAsync(Type pageType, int width, int height, double scale)` arranca uma thread STA, cria uma janela oculta com o tamanho pedido, aguarda um ciclo de layout e devolve `AutomationSnapshot`. O snapshot contém `AutomationControl(Name, Width, Height, IsKeyboardFocusable, TabIndex)` para cada botão, caixa de texto, selector e item de navegação.

```csharp
[Theory]
[MemberData(nameof(ImplementedPages))]
public async Task InteractiveControlsHaveNamesAndMinimumTargets(Type pageType)
{
    AutomationSnapshot snapshot = await WinUiHarness.RenderAsync(pageType, 1366, 768, scale: 1.25);
    Assert.All(snapshot.Interactive, control =>
    {
        Assert.False(string.IsNullOrWhiteSpace(control.Name));
        Assert.True(control.Width >= 44 && control.Height >= 44, control.Name);
    });
}
```

- [ ] **Step 2: Executar RED**

Run: `dotnet test tests/Nofarma.Desktop.UiTests/Nofarma.Desktop.UiTests.csproj --filter FullyQualifiedName~AccessibilitySurfaceTests`

Expected: FAIL apenas para problemas reais identificados pela árvore.

- [ ] **Step 3: Corrigir XAML com alterações mínimas**

Adicionar `AutomationProperties.Name`, `MinWidth`, `MinHeight`, `ScrollViewer`, `TextWrapping` ou estados visuais apenas nos controlos que falharam. Não redesenhar páginas aprovadas.

- [ ] **Step 4: Executar GREEN**

Run: `dotnet test tests/Nofarma.Desktop.UiTests/Nofarma.Desktop.UiTests.csproj --filter FullyQualifiedName~AccessibilitySurfaceTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add tests/Nofarma.Desktop.UiTests Nofarma.slnx src/Nofarma.Desktop/Views src/Nofarma.Desktop/Design
git commit -m "fix: harden desktop accessibility surfaces"
```

### Task 4: Auditoria visual real de cima a baixo

**Files:**
- Create: `artifacts/qa/full-app/` screenshots, git-ignored
- Modify: `design-qa.md`
- Modify: `docs/development/full-app-qa-matrix.md`

**Interfaces:**
- Consumes: aplicação QA compilada, início de sessão manual e licenças QA emitidas.
- Produces: capturas actuais, resultados por passo e lista de defeitos reproduzíveis.

- [ ] **Step 1: Preparar ambiente controlado**

Confirmar por caminho de processo e cabeçalho que a aplicação usa `%LOCALAPPDATA%\ABIPTOM\Nofarma-QA` e mostra `Modo QA`. Confirmar que não existe processo NôFarma comercial aberto. O início de sessão é manual e a credencial não é observada nem registada.

- [ ] **Step 2: Capturar estado sem licença**

Percorrer as 16 superfícies da matriz. Antes de cada acção, obter árvore de automação fresca. Guardar `01-missing-setup.png` até `16-missing-license.png`. Exercitar teclado, foco, scroll e uma tentativa bloqueada em cada página operacional aplicável.

- [ ] **Step 3: Capturar licença válida, tolerância e só consulta**

Importar ficheiros QA emitidos para datas controladas. Reiniciar entre estados para provar validação no arranque. Guardar capturas com prefixos `valid-`, `grace-` e `readonly-`. Usar apenas uma base QA descartável.

- [ ] **Step 4: Comparar com as fontes aprovadas**

Para inventário usar `docs/design/previews/03-products-stock-purchases-suppliers.png`. Para Caixa usar `docs/design/previews/02-dashboard-sales-invoices-cash.png`. Colocar fonte e captura na mesma comparação antes de classificar diferenças. Inspeccionar cortes, margens, tipografia, cores, raios, hierarquia, foco e mensagens.

- [ ] **Step 5: Registar resultados sem corrigir ainda**

Actualizar a matriz com `Pass`, `Blocked`, `PlannedPlaceholder` ou defeito `P0` a `P3`. `design-qa.md` inclui cada passo, captura aceite, força, risco UX, risco de acessibilidade e limite de evidência.

- [ ] **Step 6: Commit apenas do relatório sanitizado**

```powershell
git add design-qa.md docs/development/full-app-qa-matrix.md
git commit -m "docs: record full application QA findings"
```

### Task 5: Triage de defeitos e gate final

**Files:**
- Modify: `design-qa.md`
- Modify: `docs/development/full-app-qa-matrix.md`

**Interfaces:**
- Consumes: defeitos classificados da Task 4.
- Produces: gate final quando não existem defeitos bloqueantes ou um plano de correcção separado com caminhos e testes exactos.

- [ ] **Step 1: Aplicar a regra de saída da auditoria**

Se existir qualquer P0, P1 ou P2, não alterar produção nesta tarefa. Criar `docs/superpowers/plans/2026-07-28-nofarma-full-app-qa-fixes.md` com uma tarefa TDD exacta por defeito, incluindo ficheiros, teste RED, comando, implementação mínima, nova captura e commit. Marcar este plano como bloqueado até esse plano ser executado. P3 ficam registados para decisão do produto.

- [ ] **Step 2: Confirmar ausência de defeitos bloqueantes antes do gate**

Ler a matriz e confirmar que cada resultado é `Pass`, `PlannedPlaceholder`, `Blocked` com causa externa ou P3 aceite. Se houver P0, P1 ou P2, parar depois de criar o plano de correcções.

- [ ] **Step 3: Executar release gate completo**

```powershell
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx -c Release --no-restore -warnaserror -p:NofarmaLicenseChannel=Unlicensed
dotnet test Nofarma.slnx -c Release --no-build
dotnet list Nofarma.slnx package --vulnerable --include-transitive
powershell -File scripts/verify-license-channel.ps1 -Channel QA -Output artifacts/license-qa-final
rg -n --hidden "BEGIN .*PRIVATE KEY|api[_-]?key|client[_-]?secret|password\s*[:=]|token\s*[:=]" . --glob '!**/bin/**' --glob '!**/obj/**' --glob '!artifacts/**'
git diff --check
```

- [ ] **Step 4: Commit final de evidência**

```powershell
git add design-qa.md docs/development/full-app-qa-matrix.md
git commit -m "docs: complete full application QA gate"
```

## Gate de conclusão deste plano

- [ ] Todas as 16 superfícies têm resultado e evidência actual ou bloqueio nomeado.
- [ ] Todos os P0, P1 e P2 foram corrigidos, revistos e recapturados ou estão formalmente bloqueados.
- [ ] Páginas planeadas continuam marcadas como não implementadas.
- [ ] Os quatro estados de licença foram exercitados nas operações aplicáveis.
- [ ] Nenhuma credencial ou dado real entrou em captura versionada, teste, log ou Git.
- [ ] Suites, build, formatação, dependências, segredos e gate de canal passaram com saída fresca.
