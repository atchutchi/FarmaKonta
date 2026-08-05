# NôFarma POS e Vendas Locais Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar o Gate 1 aprovado com venda local completa, stock FEFO, caixa, pagamentos, vendas suspensas, recibo interno, auditoria e outbox numa única transacção SQLite.

**Architecture:** O domínio calcula linhas, descontos, pagamentos e totais sem depender de persistência. A camada de aplicação autoriza a operação, valida a licença, prepara a venda e entrega uma unidade de trabalho imutável ao `SqliteSaleStore`, que confirma todos os registos numa transacção curta com controlo optimista do stock e do turno. A interface WinUI 3 usa um `SalesViewModel` testável e mantém as entidades de Entity Framework fora da camada visual.

**Tech Stack:** .NET 10, C# 14, WinUI 3, Entity Framework Core, SQLite, xUnit v3 e GitHub Actions.

## Global Constraints

- A aplicação Windows é a fonte operacional e deve vender sem internet durante pelo menos 90 dias.
- Todos os valores monetários usam XOF inteiro em `long`. Não usar `double`, `float` ou `decimal` no domínio da venda.
- O carrinho e uma venda suspensa não reservam nem reduzem stock.
- A confirmação selecciona lotes por FEFO e bloqueia lotes expirados.
- Venda, pagamentos, stock, caixa, recibo, auditoria, comando idempotente e outbox são gravados na mesma transacção SQLite.
- A repetição da mesma chave e do mesmo pedido devolve a venda existente sem duplicar efeitos.
- A mesma chave com conteúdo diferente produz conflito explícito.
- A impressão acontece depois da confirmação e nunca volta a registar a venda.
- Sem configuração fiscal válida, o único documento do Gate 1 é `Recibo interno não fiscal`.
- O POS funciona a 1366 por 768, por teclado e com foco visível.
- O estado sem internet é informativo e não bloqueia uma venda local autorizada.
- Não guardar bases de dados, backups, certificados, códigos de recuperação, palavras-passe ou chaves no Git.
- Cada comportamento começa por um teste que falha pelo motivo esperado.

---

## Mapa de ficheiros

### Domínio

- Criar `src/Nofarma.Domain/Sales/PaymentMethod.cs` com dinheiro, cartão, Mobile Money e transferência.
- Criar `src/Nofarma.Domain/Sales/SaleStatus.cs` com concluída.
- Criar `src/Nofarma.Domain/Sales/SaleNumber.cs` para referências locais no formato `V-AAAAMMDD-NNNNNN`.
- Criar `src/Nofarma.Domain/Sales/SaleLine.cs` para quantidades, preço original, desconto, total final, custo capturado e autorizador.
- Criar `src/Nofarma.Domain/Sales/SalePayment.cs` para método, montante e referência manual.
- Criar `src/Nofarma.Domain/Sales/Sale.cs` como raiz que valida linhas, pagamentos, troco e total.
- Criar `src/Nofarma.Domain/Sales/SuspendedSale.cs` para um carrinho local sem efeitos operacionais.
- Criar `src/Nofarma.Domain/Sales/Receipt.cs` para o recibo interno imutável.
- Modificar `src/Nofarma.Domain/Inventory/StockMovementType.cs` para incluir `Sale = 11`.
- Modificar `src/Nofarma.Domain/Inventory/StockLedger.cs` para tratar `Sale` como saída.
- Modificar `src/Nofarma.Domain/Identity/Capability.cs` para incluir `ApplySaleDiscount = 25`.
- Modificar `src/Nofarma.Domain/Identity/RolePermissions.cs` para permitir desconto a Administrador e Gerente.

### Aplicação

- Criar `src/Nofarma.Application/Abstractions/ISaleStore.cs` como fronteira única de leitura e escrita da venda.
- Criar `src/Nofarma.Application/Sales/SaleRequests.cs` com pedidos de pesquisa, suspensão e conclusão.
- Criar `src/Nofarma.Application/Sales/SaleDtos.cs` com resultados próprios da interface.
- Criar `src/Nofarma.Application/Sales/SaleCompletion.cs` com a unidade de trabalho imutável entregue à persistência.
- Criar `src/Nofarma.Application/Sales/SaleCommandFingerprint.cs` com SHA-256 determinístico.
- Criar `src/Nofarma.Application/Sales/SaleConflictException.cs` para reutilização incompatível de chave.
- Criar `src/Nofarma.Application/Sales/SaleConcurrencyException.cs` para alterações concorrentes de stock ou turno.
- Criar `src/Nofarma.Application/Sales/SaleOperationBlockedException.cs` para licença ou pré-condição operacional.
- Criar `src/Nofarma.Application/Sales/SaleService.cs` para autorização, licença, FEFO, construção e confirmação.

### Persistência

- Criar os registos `SaleRecord`, `SaleLineRecord`, `SalePaymentRecord`, `SaleStockAllocationRecord`, `SuspendedSaleRecord`, `SuspendedSaleLineRecord`, `ReceiptRecord` e `SaleCommandRecord` em `src/Nofarma.Infrastructure/Persistence/Records`.
- Criar `src/Nofarma.Infrastructure/Persistence/Configurations/SaleConfigurations.cs`.
- Criar `src/Nofarma.Infrastructure/Persistence/SqliteSaleStore.cs`.
- Modificar `src/Nofarma.Infrastructure/Persistence/NofarmaDbContext.cs` com os novos `DbSet` e protecção append-only.
- Criar `src/Nofarma.Infrastructure/Persistence/Migrations/20260805090000_AddLocalSales.cs` e o respectivo ficheiro Designer.
- Modificar `src/Nofarma.Infrastructure/Persistence/Migrations/NofarmaDbContextModelSnapshot.cs`.
- Modificar `src/Nofarma.Infrastructure/Composition/ServiceCollectionExtensions.cs` para registar `ISaleStore` e `SaleService`.

### Interface Windows

- Criar `src/Nofarma.Desktop/ViewModels/SalesViewModel.cs`.
- Criar `src/Nofarma.Desktop/Views/SalesPage.xaml`.
- Criar `src/Nofarma.Desktop/Views/SalesPage.xaml.cs`.
- Criar `src/Nofarma.Desktop/Views/ReceiptPreviewDialog.xaml`.
- Criar `src/Nofarma.Desktop/Views/ReceiptPreviewDialog.xaml.cs`.
- Modificar `src/Nofarma.Desktop/Views/AppShellPage.xaml.cs` para encaminhar `Vendas`.
- Modificar `src/Nofarma.Desktop/Composition/ServiceCollectionExtensions.cs` para registar operações e view model.
- Modificar `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj` para ligar `SalesViewModel.cs` aos testes de unidade.

### Testes

- Criar `tests/Nofarma.UnitTests/Domain/Sales/SaleTests.cs`.
- Criar `tests/Nofarma.UnitTests/Domain/Sales/SaleNumberTests.cs`.
- Modificar `tests/Nofarma.UnitTests/Domain/Inventory/StockLedgerTests.cs`.
- Criar `tests/Nofarma.UnitTests/Application/Sales/SaleServiceTests.cs`.
- Criar `tests/Nofarma.IntegrationTests/Sales/SaleTransactionTests.cs`.
- Criar `tests/Nofarma.UnitTests/Desktop/SalesViewModelTests.cs`.
- Criar `tests/Nofarma.UnitTests/Composition/SurfaceSalesTests.cs`.

---

### Task 1: Modelo de venda e regras monetárias

**Files:**

- Create: `src/Nofarma.Domain/Sales/PaymentMethod.cs`
- Create: `src/Nofarma.Domain/Sales/SaleStatus.cs`
- Create: `src/Nofarma.Domain/Sales/SaleNumber.cs`
- Create: `src/Nofarma.Domain/Sales/SaleLine.cs`
- Create: `src/Nofarma.Domain/Sales/SalePayment.cs`
- Create: `src/Nofarma.Domain/Sales/Sale.cs`
- Create: `src/Nofarma.Domain/Sales/SuspendedSale.cs`
- Create: `src/Nofarma.Domain/Sales/Receipt.cs`
- Create: `tests/Nofarma.UnitTests/Domain/Sales/SaleTests.cs`
- Create: `tests/Nofarma.UnitTests/Domain/Sales/SaleNumberTests.cs`

**Interfaces:**

- Consumes: `Money.Xof(long)`, `EntityId`, `UtcInstant` e `SalesValidationException`.
- Produces: `Sale.Create`, `SaleLine.Create`, `SalePayment.Create`, `SaleNumber.Create`, `SuspendedSale.Create` e `Receipt.Create`.
- `Sale.Create(EntityId id, EntityId pharmacyId, EntityId deviceId, EntityId userId, EntityId cashShiftId, SaleNumber number, IReadOnlyCollection<SaleLine> lines, Money totalDiscount, EntityId? totalDiscountAuthorizedByUserId, IReadOnlyCollection<SalePayment> payments, UtcInstant completedAt)` devolve `Sale`.
- `Sale.Total` devolve `Money`.
- `Sale.CashReceived` devolve `Money`.
- `Sale.Change` devolve `Money`.
- `SaleLine.Create(EntityId id, EntityId productId, EntityId packageId, string description, string unitName, long packageFactor, long quantityPackages, Money unitPrice, Money discount, Money capturedCost, EntityId? discountAuthorizedByUserId)` devolve `SaleLine`.

- [ ] **Step 1: Escrever testes de linha e total que falham**

Adicionar casos exactos:

```text
2 caixas x 1 500 XOF com desconto de 200 XOF resulta em 2 800 XOF
Desconto negativo falha com SalesValidationException
Desconto superior ao bruto falha com SalesValidationException
Desconto total superior ao subtotal depois dos descontos de linha falha com SalesValidationException
Todo desconto positivo exige o identificador do utilizador autorizador
Factor de embalagem zero falha com SalesValidationException
Quantidade zero falha com SalesValidationException
Custo capturado negativo falha com SalesValidationException
```

- [ ] **Step 2: Executar os testes e confirmar falha por tipos inexistentes**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~Domain.Sales.SaleTests
```

Resultado esperado: falha de compilação porque `Sale`, `SaleLine` e `SalePayment` ainda não existem.

- [ ] **Step 3: Implementar os tipos mínimos e imutáveis**

Usar estas regras exactas:

```text
LineGrossXof = checked(UnitPriceXof * QuantityPackages)
LineNetXof = LineGrossXof - LineDiscountXof
SubtotalAfterLineDiscountsXof = soma de LineNetXof
TotalXof = SubtotalAfterLineDiscountsXof - TotalDiscountXof
PaidXof = soma de todos os pagamentos
CashReceivedXof = soma apenas de PaymentMethod.Cash
ChangeXof = PaidXof - TotalXof
Aceitar PaidXof superior ao total apenas quando existe pagamento em dinheiro
Rejeitar PaidXof diferente do total quando não existe dinheiro
Não alterar o total para calcular troco
```

Os valores enumerados ficam fixos:

```text
PaymentMethod.Cash = 1
PaymentMethod.Card = 2
PaymentMethod.MobileMoney = 3
PaymentMethod.BankTransfer = 4
SaleStatus.Completed = 1
```

- [ ] **Step 4: Escrever testes de pagamento simples, misto e troco**

Adicionar casos exactos:

```text
Total 2 800 e dinheiro 3 000 produz troco 200
Total 2 800 e cartão 2 800 produz troco zero
Total 2 800 e cartão 1 000 mais dinheiro 2 000 produz troco 200
Total 2 800 e cartão 3 000 é rejeitado
Total 2 800 e pagamentos 2 700 é rejeitado
Referência com mais de 160 caracteres é rejeitada
```

- [ ] **Step 5: Implementar `SalePayment` e validação final em `Sale.Create`**

Normalizar referências com `Trim`. Guardar `null` quando a referência fica vazia. Exigir montante positivo em XOF.

- [ ] **Step 6: Escrever e executar os testes de `SaleNumber`**

```text
SaleNumber.Create(2026-08-05, 42) devolve V-20260805-000042
Sequência zero é rejeitada
Sequência 1 000 000 é rejeitada
```

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~Domain.Sales
```

Resultado esperado: todos os testes de domínio de vendas passam.

- [ ] **Step 7: Implementar venda suspensa e recibo interno**

`SuspendedSale` guarda identificador, farmácia, dispositivo, utilizador, nome opcional limitado a 120 caracteres, linhas e instante UTC. `Receipt` copia número, venda, farmácia, linhas, pagamentos, total, troco e texto `Recibo interno não fiscal`. Nenhum destes tipos executa persistência ou impressão.

- [ ] **Step 8: Executar todos os testes de domínio**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~Domain
```

Resultado esperado: PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/Nofarma.Domain/Sales tests/Nofarma.UnitTests/Domain/Sales
git commit -m "feat: add local sale domain model"
```

---

### Task 2: Saída FEFO e autorização de descontos

**Files:**

- Modify: `src/Nofarma.Domain/Inventory/StockMovementType.cs`
- Modify: `src/Nofarma.Domain/Inventory/StockLedger.cs`
- Modify: `src/Nofarma.Domain/Identity/Capability.cs`
- Modify: `src/Nofarma.Domain/Identity/RolePermissions.cs`
- Modify: `tests/Nofarma.UnitTests/Domain/Inventory/StockLedgerTests.cs`
- Modify: `tests/Nofarma.UnitTests/Domain/Identity/RolePermissionsTests.cs`

**Interfaces:**

- Consumes: `FefoAllocator.Allocate(long, IReadOnlyCollection<StockLotAvailability>, DateOnly)`.
- Produces: `StockMovementType.Sale = 11` e `Capability.ApplySaleDiscount = 25`.
- Administrador e Gerente recebem `ApplySaleDiscount`. Farmacêutico e Caixa não recebem esta capacidade.

- [ ] **Step 1: Escrever teste de movimento de venda que falha**

```text
Um StockLedger com saldo 10 aplica StockMovementType.Sale de -3 e termina em 7
Uma venda de -11 falha por stock negativo
```

- [ ] **Step 2: Executar o teste isolado**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~StockLedgerTests
```

Resultado esperado: falha porque `Sale` não é reconhecida como saída.

- [ ] **Step 3: Adicionar o tipo e a regra de saída**

Adicionar `Sale = 11` sem renumerar os valores existentes. Incluir `StockMovementType.Sale` na colecção privada `OutgoingTypes` de `StockLedger`.

- [ ] **Step 4: Escrever testes de permissões de desconto**

```text
Administrator pode ApplySaleDiscount
Manager pode ApplySaleDiscount
Pharmacist não pode ApplySaleDiscount
Cashier não pode ApplySaleDiscount
Todos mantêm CreateSale conforme as regras existentes
```

- [ ] **Step 5: Implementar e executar os testes**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter "FullyQualifiedName~StockLedgerTests|FullyQualifiedName~RolePermissionsTests"
```

Resultado esperado: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Domain/Inventory src/Nofarma.Domain/Identity tests/Nofarma.UnitTests/Domain
git commit -m "feat: add sale stock movement and discount permission"
```

---

### Task 3: Contratos e serviço de aplicação

**Files:**

- Create: `src/Nofarma.Application/Abstractions/ISaleStore.cs`
- Create: `src/Nofarma.Application/Sales/SaleRequests.cs`
- Create: `src/Nofarma.Application/Sales/SaleDtos.cs`
- Create: `src/Nofarma.Application/Sales/SaleCompletion.cs`
- Create: `src/Nofarma.Application/Sales/SaleCommandFingerprint.cs`
- Create: `src/Nofarma.Application/Sales/SaleConflictException.cs`
- Create: `src/Nofarma.Application/Sales/SaleConcurrencyException.cs`
- Create: `src/Nofarma.Application/Sales/SaleOperationBlockedException.cs`
- Create: `src/Nofarma.Application/Sales/SaleService.cs`
- Create: `tests/Nofarma.UnitTests/Application/Sales/SaleServiceTests.cs`

**Interfaces:**

- Consumes: `AuthorizationService`, `ILicensedOperationPolicy`, `IUtcClock`, `FefoAllocator`, `CashShift` e os tipos da Task 1.
- Produces: `SaleService.SearchProductsAsync`, `SaleService.GetSuspendedAsync`, `SaleService.SuspendAsync`, `SaleService.DeleteSuspendedAsync`, `SaleService.CompleteAsync` e `SaleService.GetReceiptAsync`.
- `CompleteSaleRequest(IReadOnlyCollection<CompleteSaleLineRequest> Lines, long TotalDiscountXof, EntityId? TotalDiscountAuthorizedByUserId, IReadOnlyCollection<SalePaymentRequest> Payments, string IdempotencyKey)`.
- `CompleteSaleLineRequest(EntityId ProductId, EntityId PackageId, long QuantityPackages, long DiscountXof, EntityId? DiscountAuthorizedByUserId)`.
- `SalePaymentRequest(PaymentMethod Method, long AmountXof, string? Reference)`.
- `SaleActorContext(EntityId PharmacyId, EntityId DeviceId, EntityId ActorUserId)`.
- `SaleProductResult(EntityId ProductId, EntityId PackageId, string Code, string Name, string PackageName, long PackageFactor, long AvailableQuantityBase, long SalePriceXof, DateOnly? EarliestExpiry, bool RequiresPrescription)`.
- `SaleSummary(EntityId Id, string Number, long TotalXof, long PaidXof, long ChangeXof, UtcInstant CompletedAtUtc, EntityId ReceiptId)`.
- `SuspendedSaleSummary(EntityId Id, string? Name, int LineCount, long EstimatedTotalXof, UtcInstant SuspendedAtUtc)`.
- `ReceiptLineDetails(string Description, string UnitName, long QuantityPackages, long UnitPriceXof, long DiscountXof, long TotalXof)`.
- `ReceiptPaymentDetails(PaymentMethod Method, long AmountXof, string? Reference)`.
- `ReceiptDetails(EntityId Id, EntityId SaleId, string Number, string PharmacyName, string OperatorName, UtcInstant CreatedAtUtc, IReadOnlyList<ReceiptLineDetails> Lines, IReadOnlyList<ReceiptPaymentDetails> Payments, long TotalXof, long ChangeXof, string DocumentLabel)`.
- `SaleProductSnapshot(EntityId ProductId, EntityId PackageId, string Code, string Name, string PackageName, long PackageFactor, long SalePriceXof, bool RequiresPrescription, IReadOnlyList<SaleLotSnapshot> Lots)`.
- `SaleLotSnapshot(StockLot Lot, long AvailableQuantityBase, long Version)`.
- `SaleStockAllocation(EntityId SaleLineId, EntityId ProductId, EntityId LotId, long QuantityBase, long OriginUnitCostXof, long ExpectedLotVersion, long PreviousLotBalance, long ResultingLotBalance)`.
- `SaleCommandEnvelope(string IdempotencyKey, string RequestFingerprint)`.
- `SaleCommandResult(string RequestFingerprint, SaleSummary Result)`.
- `SaleOutboxEvent(EntityId Id, EntityId PharmacyId, EntityId DeviceId, string EventType, EntityId AggregateId, string PayloadJson, UtcInstant OccurredAtUtc)`.
- `SaleCompletion(SaleActorContext Context, Sale Sale, Receipt Receipt, IReadOnlyList<SaleStockAllocation> Allocations, StoredCashShift Shift, CashMovement? CashMovement, long ExpectedCashShiftVersion, SaleCommandEnvelope Command, AuditEvent Audit, SaleOutboxEvent Outbox)`.
- `ISaleStore.CompleteAsync(SaleCompletion completion, CancellationToken cancellationToken)` devolve `SaleSummary`.

- [ ] **Step 1: Escrever testes de autorização e pré-condições**

Criar um `FakeSaleStore`, relógio fixo e política de licença controlável. Cobrir:

```text
Utilizador sem CreateSale recebe AuthorizationException antes de consultar stock
Licença bloqueada recebe SaleOperationBlockedException com o código da política
Sem turno aberto recebe SalesValidationException
Carrinho vazio recebe SalesValidationException
Desconto de linha sem ApplySaleDiscount recebe AuthorizationException
Desconto total sem ApplySaleDiscount recebe AuthorizationException
```

- [ ] **Step 2: Executar os testes e confirmar falha de compilação**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~SaleServiceTests
```

- [ ] **Step 3: Definir `ISaleStore` com contratos exactos**

```text
GetActorContextAsync(EntityId userId, CancellationToken) -> SaleActorContext?
SearchProductsAsync(EntityId pharmacyId, string query, DateOnly businessDate, int limit, CancellationToken) -> IReadOnlyList<SaleProductResult>
GetOpenShiftAsync(EntityId pharmacyId, EntityId deviceId, CancellationToken) -> StoredCashShift?
GetProductSnapshotAsync(EntityId pharmacyId, EntityId productId, EntityId packageId, DateOnly businessDate, CancellationToken) -> SaleProductSnapshot?
GetCommandResultAsync(EntityId pharmacyId, string idempotencyKey, CancellationToken) -> SaleCommandResult?
GetNextSaleSequenceAsync(EntityId pharmacyId, DateOnly businessDate, CancellationToken) -> long
CompleteAsync(SaleCompletion completion, CancellationToken) -> SaleSummary
GetSuspendedAsync(EntityId pharmacyId, EntityId deviceId, CancellationToken) -> IReadOnlyList<SuspendedSaleSummary>
SaveSuspendedAsync(SuspendedSale sale, AuditEvent auditEvent, CancellationToken) -> SuspendedSaleSummary
DeleteSuspendedAsync(EntityId pharmacyId, EntityId deviceId, EntityId suspendedSaleId, AuditEvent auditEvent, CancellationToken) -> bool
GetReceiptAsync(EntityId pharmacyId, EntityId receiptId, CancellationToken) -> ReceiptDetails?
```

- [ ] **Step 4: Implementar normalização e fingerprint determinístico**

Ordenar linhas por `ProductId` e `PackageId`. Manter a ordem dos pagamentos por método e referência normalizada. Serializar apenas campos do pedido e contexto que alteram o efeito. Calcular SHA-256 em UTF-8 e devolver 64 caracteres hexadecimais minúsculos.

- [ ] **Step 5: Escrever testes de idempotência**

```text
Resultado existente com a mesma fingerprint é devolvido sem validar licença ou stock novamente
Resultado existente com fingerprint diferente lança SaleConflictException
Chave vazia é rejeitada
Chave com 161 caracteres é rejeitada
```

- [ ] **Step 6: Implementar preparação FEFO e captura de preço e custo**

Para cada pedido, carregar o snapshot actual. Calcular `requiredQuantityBase` com multiplicação verificada. Executar FEFO sobre os lotes vendáveis. Capturar no `SaleLine` o preço actual por embalagem e o custo total exacto dos lotes seleccionados, em XOF inteiro. Calcular esse custo como soma de `OriginUnitCostXof * QuantityBase` sem média nem divisão. Guardar cada escolha como `SaleStockAllocation` com custo de origem, versão e saldos anterior e resultante.

- [ ] **Step 7: Escrever testes de stock e concorrência lógica**

```text
Uma linha que atravessa dois lotes produz duas alocações FEFO
Lote expirado não aparece nas alocações
Stock insuficiente impede chamada a CompleteAsync
Factor de embalagem é aplicado à quantidade base
Preço e custo capturados não mudam quando o snapshot falso muda depois da preparação
```

- [ ] **Step 8: Implementar caixa, auditoria, recibo e outbox na unidade de trabalho**

`SaleCompletion` contém contexto, venda, recibo, alocações, turno com `CashMovementType.Sale` apenas pelo dinheiro retido depois do troco, versão esperada do turno, comando, auditoria `sale.completed` e outbox `sale.completed.v1`. O montante do movimento é `CashReceivedXof - ChangeXof`. Se o resultado for zero, não criar movimento de caixa nem alterar o valor esperado do turno. `SaleService` repete até três vezes quando recebe `SaleConcurrencyException` por colisão de sequência, mas volta a preparar stock e turno em cada tentativa.

- [ ] **Step 9: Executar testes de aplicação**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~Application.Sales
```

Resultado esperado: PASS.

- [ ] **Step 10: Commit**

```powershell
git add src/Nofarma.Application tests/Nofarma.UnitTests/Application/Sales
git commit -m "feat: add sale application workflow"
```

---

### Task 4: Esquema SQLite para vendas locais

**Files:**

- Create: `src/Nofarma.Infrastructure/Persistence/Records/SaleRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/SaleLineRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/SalePaymentRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/SaleStockAllocationRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/SuspendedSaleRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/SuspendedSaleLineRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/ReceiptRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/SaleCommandRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/SaleConfigurations.cs`
- Modify: `src/Nofarma.Infrastructure/Persistence/NofarmaDbContext.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Migrations/20260805090000_AddLocalSales.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Migrations/20260805090000_AddLocalSales.Designer.cs`
- Modify: `src/Nofarma.Infrastructure/Persistence/Migrations/NofarmaDbContextModelSnapshot.cs`
- Create: `tests/Nofarma.IntegrationTests/Sales/SaleTransactionTests.cs`

**Interfaces:**

- Consumes: tipos e enums das Tasks 1 a 3.
- Produces: modelo relacional e índices usados por `SqliteSaleStore`.

- [ ] **Step 1: Escrever teste de migração de instalação existente**

O teste cria uma base na migração `20260729103917_AddOperationRequestFingerprints`, insere farmácia, dispositivo, utilizador, produto, embalagem e lote, executa a migração actual e confirma:

```text
Os registos antigos permanecem
As oito tabelas novas existem
Não existe venda criada pela migração
O lote antigo mantém o saldo e a versão
```

- [ ] **Step 2: Executar o teste e confirmar falha por tabelas inexistentes**

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SaleTransactionTests.Migration
```

- [ ] **Step 3: Criar registos com colunas exactas**

`SaleRecord` inclui `Id`, `PharmacyId`, `DeviceId`, `UserId`, `CashShiftId`, `Number`, `BusinessDate`, `DailySequence`, `Status`, `GrossSubtotalXof`, `LineDiscountXof`, `TotalDiscountXof`, `TotalDiscountAuthorizedByUserId`, `TotalXof`, `PaidXof`, `ChangeXof` e `CompletedAtUtc`.

`SaleLineRecord` inclui identificadores, sequência, descrição capturada, embalagem capturada, factor, quantidade de embalagens, quantidade base, preço unitário, bruto, desconto, líquido, custo total capturado e autorizador do desconto.

`SalePaymentRecord` inclui sequência, método, montante e referência.

`SaleStockAllocationRecord` inclui venda, linha, produto, lote, quantidade base, custo unitário de origem capturado, saldo anterior e saldo resultante.

`ReceiptRecord` inclui venda, número, tipo fixo `InternalNonFiscal`, conteúdo JSON e instante de criação.

`SaleCommandRecord` inclui farmácia, chave, fingerprint, venda, resultado JSON e instante de criação.

- [ ] **Step 4: Configurar integridade e índices**

Criar índices únicos para:

```text
Sales em PharmacyId + BusinessDate + DailySequence
Sales em PharmacyId + Number
SaleLines em SaleId + Sequence
SalePayments em SaleId + Sequence
SaleStockAllocations em SaleId + SaleLineId + StockLotId
SaleCommands em PharmacyId + IdempotencyKey
```

Usar `DeleteBehavior.Restrict` em vendas concluídas. Usar cascata apenas entre `SuspendedSales` e as suas linhas porque apagar uma suspensão é a operação prevista e não altera o histórico operacional.

- [ ] **Step 5: Proteger histórico append-only**

Em `EnsureAppendOnlyRecords`, rejeitar `Modified` e `Deleted` para `SaleRecord`, `SaleLineRecord`, `SalePaymentRecord`, `SaleStockAllocationRecord`, `ReceiptRecord` e `SaleCommandRecord`. Permitir alteração e remoção de vendas suspensas.

- [ ] **Step 6: Gerar e rever a migração**

```powershell
dotnet ef migrations add AddLocalSales --project src/Nofarma.Infrastructure/Nofarma.Infrastructure.csproj --startup-project src/Nofarma.Infrastructure/Nofarma.Infrastructure.csproj --output-dir Persistence/Migrations
```

Renomear o identificador gerado para `20260805090000_AddLocalSales` em ficheiro, atributo e snapshot para manter a referência exacta deste plano. Confirmar que a migração não elimina nem altera dados existentes.

- [ ] **Step 7: Executar teste de migração e modelo**

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SaleTransactionTests.Migration
```

Resultado esperado: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src/Nofarma.Infrastructure/Persistence tests/Nofarma.IntegrationTests/Sales/SaleTransactionTests.cs
git commit -m "feat: add local sales database schema"
```

---

### Task 5: Confirmação atómica e idempotente em SQLite

**Files:**

- Create: `src/Nofarma.Infrastructure/Persistence/SqliteSaleStore.cs`
- Modify: `src/Nofarma.Infrastructure/Composition/ServiceCollectionExtensions.cs`
- Modify: `tests/Nofarma.IntegrationTests/Sales/SaleTransactionTests.cs`

**Interfaces:**

- Consumes: `ISaleStore` e `SaleCompletion` da Task 3 e o esquema da Task 4.
- Produces: implementação transaccional de todas as operações de `ISaleStore`.

- [ ] **Step 1: Escrever teste de venda completa em SQLite real**

Preparar uma farmácia com produto, duas embalagens, dois lotes, turno aberto e utilizador Caixa. Confirmar uma venda com dinheiro e cartão. Verificar exactamente:

```text
1 Sale
1 SaleLine
2 SalePayments
2 SaleStockAllocations quando a quantidade atravessa lotes
2 StockMovements do tipo Sale
1 CashMovement do tipo Sale apenas pelo dinheiro recebido menos o troco
1 Receipt
1 SaleCommand
1 AuditEvent sale.completed
1 OutboxEvent sale.completed.v1
```

- [ ] **Step 2: Executar o teste e confirmar falha por store inexistente**

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SaleTransactionTests.Complete
```

- [ ] **Step 3: Implementar leituras sem rastreamento**

Aplicar sempre `PharmacyId` e `DeviceId` nos filtros. A pesquisa aceita nome, código e código de barras, usa `Take(limit)` com limite máximo 50 e devolve apenas produtos activos com saldo vendável positivo. A validade mínima ignora lotes bloqueados na data do negócio.

- [ ] **Step 4: Implementar transacção de confirmação**

Executar por esta ordem dentro de `BeginTransactionAsync`:

```text
Consultar SaleCommand pela chave dentro da transacção
Devolver duplicado compatível ou lançar conflito
Actualizar cada StockLot com condição Id + PharmacyId + RowVersion + AvailableQuantityBase esperado
Inserir Sale e linhas
Inserir pagamentos e alocações
Inserir StockMovements com chaves sale:{saleId}:lot:{lotId}
Actualizar CashShift por RowVersion quando existe componente em dinheiro
Inserir CashMovement com SourceSaleId
Inserir Receipt
Inserir AuditEvent
Inserir OutboxEvent
Inserir SaleCommand
Guardar alterações
Confirmar transacção
```

Se qualquer actualização condicional afectar zero linhas, fazer rollback e lançar `SaleConcurrencyException`.

- [ ] **Step 5: Escrever teste de rollback total**

Forçar falha depois de preparar a venda através de uma alteração concorrente do saldo do segundo lote. Confirmar que não existem venda, linhas, pagamentos, novos movimentos de stock, movimento de caixa, recibo, comando, auditoria nem outbox. Confirmar que o primeiro lote também não foi reduzido.

- [ ] **Step 6: Implementar controlo optimista e executar o teste**

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SaleTransactionTests.Rollback
```

Resultado esperado: PASS.

- [ ] **Step 7: Escrever testes de idempotência e sequência**

```text
Duas chamadas com a mesma chave e fingerprint devolvem o mesmo SaleId
A contagem de todas as tabelas operacionais fica inalterada na segunda chamada
A mesma chave com fingerprint diferente lança SaleConflictException
Duas vendas do mesmo dia recebem sequências consecutivas
Uma sequência concorrente produz retry controlado e nunca duplica Number
```

- [ ] **Step 8: Implementar leitura do comando antes e depois de conflito único**

Quando SQLite devolver violação do índice da chave ou do número, reler o comando. Devolver apenas quando a fingerprint coincide. Para colisão de sequência sem comando correspondente, lançar `SaleConcurrencyException` para o serviço repetir até três vezes com nova sequência.

- [ ] **Step 9: Testar ligações e isolamento por farmácia**

Adicionar casos que provam que uma sessão de outra farmácia não pesquisa produtos, não lê recibos e não remove vendas suspensas fora da sua farmácia.

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SaleTransactionTests
```

Resultado esperado: PASS.

- [ ] **Step 10: Registar serviços e executar testes de arquitectura**

Registar `ISaleStore` como singleton de `SqliteSaleStore` e `SaleService` como transient, seguindo o padrão de caixa.

```powershell
dotnet test tests/Nofarma.ArchitectureTests/Nofarma.ArchitectureTests.csproj -c Release
```

Resultado esperado: PASS.

- [ ] **Step 11: Commit**

```powershell
git add src/Nofarma.Infrastructure tests/Nofarma.IntegrationTests/Sales/SaleTransactionTests.cs
git commit -m "feat: persist sales atomically in sqlite"
```

---

### Task 6: Suspender, retomar e eliminar carrinhos

**Files:**

- Modify: `src/Nofarma.Application/Sales/SaleService.cs`
- Modify: `src/Nofarma.Infrastructure/Persistence/SqliteSaleStore.cs`
- Modify: `tests/Nofarma.UnitTests/Application/Sales/SaleServiceTests.cs`
- Modify: `tests/Nofarma.IntegrationTests/Sales/SaleTransactionTests.cs`

**Interfaces:**

- Consumes: `SuspendedSale` e métodos de `ISaleStore` já definidos.
- Produces: operações completas de suspensão sem efeitos de stock ou caixa.

- [ ] **Step 1: Escrever teste de aplicação para suspensão**

```text
Suspender exige pelo menos uma linha
Suspender não exige pagamentos
Retomar devolve linhas com preço actual e disponibilidade actual
Retomar marca linhas sem stock suficiente para revisão
Eliminar exige mesma farmácia e dispositivo
```

- [ ] **Step 2: Executar e confirmar falha comportamental**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter "FullyQualifiedName~SaleServiceTests&Name~Suspend"
```

- [ ] **Step 3: Implementar aplicação e persistência**

`SaveSuspendedAsync` substitui uma suspensão apenas quando recebe o mesmo identificador, farmácia e dispositivo. Não cria StockMovement, CashMovement, AuditEvent operacional de conclusão ou OutboxEvent. A gravação regista auditoria `sale.suspended`. A remoção depois da retoma regista `sale.suspension_deleted` na mesma transacção.

- [ ] **Step 4: Escrever teste SQLite de ausência de efeitos operacionais**

Suspender e retomar uma venda. Confirmar zero vendas concluídas, zero pagamentos, zero movimentos de stock, zero movimentos de caixa e saldo de lote inalterado.

- [ ] **Step 5: Executar testes de suspensão**

```powershell
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~SaleTransactionTests&Name~Suspend"
```

Resultado esperado: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Application/Sales/SaleService.cs src/Nofarma.Infrastructure/Persistence/SqliteSaleStore.cs tests/Nofarma.UnitTests/Application/Sales/SaleServiceTests.cs tests/Nofarma.IntegrationTests/Sales/SaleTransactionTests.cs
git commit -m "feat: add suspended local sales"
```

---

### Task 7: View model do POS e protecção contra submissão repetida

**Files:**

- Create: `src/Nofarma.Desktop/ViewModels/SalesViewModel.cs`
- Modify: `src/Nofarma.Desktop/Composition/ServiceCollectionExtensions.cs`
- Modify: `tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj`
- Create: `tests/Nofarma.UnitTests/Desktop/SalesViewModelTests.cs`

**Interfaces:**

- Consumes: `SaleService` e DTOs da Task 3.
- Produces: `ISalesPageOperations`, `SalesPageOperations`, `SalesViewModel`, `SaleCartLineViewModel` e `PaymentEntryViewModel`.
- `ISalesPageOperations` expõe pesquisa, suspensões, suspensão, eliminação, conclusão e recibo sem expor `LocalSession` à página.
- `PaymentEntryInput(PaymentMethod Method, string Amount, string? Reference)` contém texto ainda não validado vindo da interface.
- `SalesViewModel.SearchAsync(string query, CancellationToken)` mantém resultados reais.
- `SalesViewModel.AddProduct(SaleProductResult product)` agrega a mesma embalagem no carrinho.
- `SalesViewModel.CompleteAsync(IReadOnlyCollection<PaymentEntryInput> payments, CancellationToken)` devolve `bool`.

- [ ] **Step 1: Ligar o novo view model ao projecto de testes**

Adicionar ao `Nofarma.UnitTests.csproj`:

```xml
<Compile Include="..\..\src\Nofarma.Desktop\ViewModels\SalesViewModel.cs" Link="Desktop\SalesViewModel.cs" />
```

- [ ] **Step 2: Escrever testes de pesquisa e leitor de código**

```text
Pesquisa vazia limpa resultados sem chamar operações
Pesquisa por código devolve resultado e AddProduct cria uma linha
Adicionar a mesma embalagem aumenta a quantidade
Após AddProduct a propriedade ShouldRefocusSearch fica verdadeira
Produto sem stock não entra no carrinho
```

- [ ] **Step 3: Executar o teste e confirmar falha de compilação**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~SalesViewModelTests
```

- [ ] **Step 4: Implementar estado e validação do carrinho**

Expor `SearchResults`, `CartLines`, `SuspendedSales`, `SubtotalText`, `DiscountText`, `TotalText`, `ChangeText`, `IsSearching`, `IsSubmitting`, `IsPaymentOpen`, `IsSuspendedSalesOpen`, `ErrorMessage`, `StatusMessage`, `ValidationErrors` e `LastReceipt`. Formatar XOF com separador de espaço como no `CashViewModel`.

- [ ] **Step 5: Escrever testes de desconto e pagamentos**

```text
Desconto inválido não altera a linha
Desconto autorizado actualiza total
Pagamento inferior mantém painel aberto e cria erro junto ao total
Pagamento em dinheiro calcula troco
Pagamento misto correcto permite conclusão
```

- [ ] **Step 6: Implementar chave idempotente por abertura de pagamento**

Gerar `sale-complete-ui-{Guid:N}` quando o painel de pagamento abre. Reutilizar a mesma chave após erro transitório com o mesmo carrinho e pagamentos. Invalidar a chave quando linhas ou pagamentos mudam. Manter `IsSubmitting` verdadeiro até a chamada terminar.

- [ ] **Step 7: Escrever teste de duplo clique**

Iniciar uma conclusão bloqueada no fake. Chamar `CompleteAsync` novamente. Confirmar uma chamada às operações, segundo resultado falso e mesma chave na primeira chamada.

- [ ] **Step 8: Implementar suspensão e retoma no view model**

Ao suspender com sucesso, limpar o carrinho. Ao retomar, substituir o carrinho pelo snapshot devolvido e mostrar aviso em cada linha indisponível. `Escape` apenas fecha o painel actual. Não limpa o carrinho.

- [ ] **Step 9: Executar testes do view model**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~SalesViewModelTests
```

Resultado esperado: PASS.

- [ ] **Step 10: Registar operações e view model e fazer commit**

Registar `ISalesPageOperations` e `SalesViewModel` como transient.

```powershell
git add src/Nofarma.Desktop/ViewModels/SalesViewModel.cs src/Nofarma.Desktop/Composition/ServiceCollectionExtensions.cs tests/Nofarma.UnitTests
git commit -m "feat: add point of sale view model"
```

---

### Task 8: Ecrã WinUI do POS e atalhos de teclado

**Files:**

- Create: `src/Nofarma.Desktop/Views/SalesPage.xaml`
- Create: `src/Nofarma.Desktop/Views/SalesPage.xaml.cs`
- Modify: `src/Nofarma.Desktop/Views/AppShellPage.xaml.cs`
- Create: `tests/Nofarma.UnitTests/Composition/SurfaceSalesTests.cs`

**Interfaces:**

- Consumes: `SalesViewModel` da Task 7 e recursos visuais existentes em `App.xaml`.
- Produces: página funcional encaminhada pelo botão `Vendas`.

- [ ] **Step 1: Escrever testes de superfície que falham**

Verificar no XAML e code-behind:

```text
Existe SalesPage.xaml com título Vendas
AppShellPage cria new SalesPage() para Vendas
Existem aceleradores F2, F4, F6, F8 e Escape
Pesquisa tem AutomationProperties.Name
Carrinho tem nome acessível e estado vazio honesto
Botão concluir fica ligado ao estado IsSubmitting
Não existem produtos ou valores fictícios
Existe VisualState para 1366 por 768
```

- [ ] **Step 2: Executar teste e confirmar falha por página inexistente**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~SurfaceSalesTests
```

- [ ] **Step 3: Criar estrutura visual de três zonas**

Usar o sistema visual azul existente. A coluna esquerda ocupa o espaço flexível. O carrinho tem largura entre 420 e 480 píxeis. O rodapé do carrinho mantém total e `F8 Pagamento` visíveis. Usar ícones da biblioteca WinUI quando necessário. Não criar SVG, emoji ou desenho por texto.

- [ ] **Step 4: Implementar pesquisa e carrinho por teclado**

`F2` foca e selecciona a pesquisa. `Enter` pesquisa e adiciona automaticamente quando existe correspondência exacta única. Depois de adicionar, limpar a caixa e voltar a focá-la. Quantidade, remoção e desconto têm nomes acessíveis que incluem o produto.

- [ ] **Step 5: Implementar painéis de pagamento e suspensões**

`F4` abre confirmação de suspensão. `F6` abre lista de vendas suspensas. `F8` abre pagamento. `Escape` fecha apenas o último painel aberto. O painel de pagamento permite adicionar métodos, montantes e referências e mostra total, pago, falta ou troco em texto e não apenas por cor.

- [ ] **Step 6: Implementar estados de erro, vazio e restrição**

Mostrar causa, impacto e acção possível. Exemplos exactos:

```text
Turno fechado. A venda não pode ser concluída. Abre um turno no módulo Caixa.
Stock alterado. O carrinho foi mantido. Revê as linhas assinaladas.
Licença não permite novas operações. O carrinho foi mantido. Consulta a página Licença.
Sem resultados. Confirma o código ou pesquisa pelo nome do produto.
```

- [ ] **Step 7: Executar testes de superfície e build Desktop**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter FullyQualifiedName~SurfaceSalesTests
dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj -c Release
```

Resultado esperado: PASS e zero avisos.

- [ ] **Step 8: Commit**

```powershell
git add src/Nofarma.Desktop/Views tests/Nofarma.UnitTests/Composition/SurfaceSalesTests.cs
git commit -m "feat: add keyboard first sales page"
```

---

### Task 9: Recibo interno e simulador de impressão

**Files:**

- Create: `src/Nofarma.Desktop/Views/ReceiptPreviewDialog.xaml`
- Create: `src/Nofarma.Desktop/Views/ReceiptPreviewDialog.xaml.cs`
- Modify: `src/Nofarma.Desktop/Views/SalesPage.xaml.cs`
- Modify: `tests/Nofarma.UnitTests/Composition/SurfaceSalesTests.cs`
- Modify: `tests/Nofarma.UnitTests/Desktop/SalesViewModelTests.cs`

**Interfaces:**

- Consumes: `ReceiptDetails` devolvido por `ISalesPageOperations.GetReceiptAsync`.
- Produces: pré-visualização não fiscal reabrível sem alterar a venda.

- [ ] **Step 1: Escrever testes do conteúdo do recibo**

Confirmar que `ReceiptDetails` contém número operacional, farmácia, data UTC, operador, linhas, pagamentos, total, troco e a indicação exacta `Recibo interno não fiscal`. Confirmar que não contém as palavras `Factura oficial` ou `DGCI autorizada`.

- [ ] **Step 2: Executar testes e confirmar falha da pré-visualização inexistente**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter "FullyQualifiedName~SalesViewModelTests&Name~Receipt"
```

- [ ] **Step 3: Criar diálogo de pré-visualização**

Mostrar uma folha térmica simulada de 80 mm dentro de um contentor rolável. Usar texto real do recibo e o logótipo existente apenas se o asset oficial estiver presente no projecto. O botão `Simular impressão` altera apenas o estado visual para `Enviado ao simulador`. Não cria ficheiro, movimento ou nova venda.

- [ ] **Step 4: Permitir reabrir o último recibo**

Depois de conclusão bem sucedida, limpar o carrinho e manter `LastReceipt`. Disponibilizar `Ver recibo`. Ao reabrir, consultar pelo `ReceiptId` e farmácia actual. Uma falha de pré-visualização mostra erro e mantém a venda concluída.

- [ ] **Step 5: Testar que impressão não duplica a venda**

Abrir, fechar e reabrir o diálogo duas vezes. O fake de conclusão mantém uma chamada. O fake de recibo recebe apenas leituras.

- [ ] **Step 6: Executar testes e build**

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --filter "FullyQualifiedName~SalesViewModelTests|FullyQualifiedName~SurfaceSalesTests"
dotnet build Nofarma.slnx -c Release
```

Resultado esperado: PASS e zero avisos.

- [ ] **Step 7: Commit**

```powershell
git add src/Nofarma.Desktop/Views tests/Nofarma.UnitTests
git commit -m "feat: add internal receipt preview"
```

---

### Task 10: Verificação completa do Gate 1

**Files:**

- Modify: `docs/superpowers/specs/2026-08-05-nofarma-completion-design.md` apenas para mudar o estado do Gate 1 depois de todas as verificações passarem.
- Create: `docs/development/pos-sales-verification.md` com comandos, resultados e limitações reais.

**Interfaces:**

- Consumes: todas as tarefas anteriores.
- Produces: evidência verificável para revisão e decisão de entrada no Gate 2.

- [ ] **Step 1: Executar formatação em modo de verificação**

```powershell
dotnet format Nofarma.slnx --verify-no-changes
```

Resultado esperado: código 0 e nenhuma alteração.

- [ ] **Step 2: Executar build Release**

```powershell
dotnet build Nofarma.slnx -c Release --no-restore
```

Resultado esperado: zero erros e zero avisos.

- [ ] **Step 3: Executar todos os testes**

```powershell
dotnet test tests/Nofarma.ArchitectureTests/Nofarma.ArchitectureTests.csproj -c Release --no-build
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --no-build
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --no-build
```

Resultado esperado: todos passam sem testes ignorados do Gate 1.

- [ ] **Step 4: Verificar pacotes vulneráveis e segredos**

```powershell
dotnet list Nofarma.slnx package --vulnerable --include-transitive
git grep -n -I -E "(ghp_|github_pat_|sk-[A-Za-z0-9]|BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|Password=|SecretKey=|ApiKey=)"
git status --short
```

Resultado esperado: nenhum pacote vulnerável conhecido, nenhuma credencial e apenas alterações esperadas.

- [ ] **Step 5: Executar cenário manual offline**

Iniciar a aplicação sem internet e validar nesta ordem:

```text
Login de Caixa com PIN
Abertura de turno
Pesquisa por nome
Leitura simulada por código de barras
Venda em dinheiro com troco
Venda mista
Venda suspensa e retomada após alteração de stock
Repetição rápida do botão concluir
Pré-visualização do recibo interno
Fecho de turno com os valores das vendas em dinheiro
```

- [ ] **Step 6: Auditar visualmente a 1366 por 768**

Confirmar foco visível, ausência de cortes, leitura de total, mensagens de erro, navegação apenas por teclado e consistência com o desenho NôFarma aprovado. Comparar a aplicação real com as imagens aprovadas usando o mesmo viewport.

- [ ] **Step 7: Documentar resultados reais**

Em `docs/development/pos-sales-verification.md`, registar commit, sistema operativo, versão .NET, comandos, contagens de testes, resultado visual e limitações. Não declarar impressora física validada. Essa prova pertence ao Gate 5.

- [ ] **Step 8: Marcar Gate 1 como concluído apenas se tudo passar**

Alterar o estado no documento de desenho para `Gate 1 verificado`. Se qualquer regra crítica falhar, manter o gate aberto e registar a falha exacta no documento de verificação.

- [ ] **Step 9: Commit final do gate**

```powershell
git add docs
git commit -m "docs: record gate one verification"
```

- [ ] **Step 10: Rever o diff e publicar a branch**

```powershell
git diff origin/main...HEAD --check
git status --short
git log --oneline origin/main..HEAD
git push -u origin codex/pos-sales-plan
```

Resultado esperado: diff sem erros de espaços, árvore limpa e branch publicada.

---

## Critério de passagem para o Gate 2

O Gate 2 só começa quando uma venda offline real produzir uma única venda, pagamentos, movimentos FEFO, movimento de caixa correcto, recibo interno, auditoria e outbox na mesma transacção. O teste de rollback deve provar que uma falha não deixa efeitos parciais. A interface deve concluir o fluxo a 1366 por 768 apenas por teclado. A simulação de impressão pode falhar sem repetir a venda.
