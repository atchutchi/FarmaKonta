# NôFarma Products, Purchases and Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task by task. Use superpowers:test-driven-development for every production behaviour and superpowers:verification-before-completion before declaring the phase complete.

**Goal:** Entregar catálogo, fornecedores, compras, recepções, lotes, livro de movimentos, alertas e inventário inicial importado por Excel ou CSV, com funcionamento local, permissões e interface Windows fiel ao desenho aprovado.

**Architecture:** O domínio contém regras puras de catálogo, unidades, lotes, validade, movimentos e alertas. A camada de aplicação expõe comandos, consultas e contratos de persistência. A infraestrutura usa Entity Framework Core e SQLite, mantendo documentos, movimentos, saldos, auditoria e idempotência na mesma transacção. A aplicação WinUI consome apenas os casos de uso e substitui os estados vazios de Produtos, Stock, Compras e Fornecedores.

**Tech Stack:** C# 14, .NET 10.0.302, WinUI 3, Windows App SDK 2.3.1, Entity Framework Core 10.0.10, SQLite, DocumentFormat.OpenXml 3.5.1, xUnit v3 e GitHub Actions.

## Restrições globais

- A especificação em `docs/superpowers/specs/2026-07-27-nofarma-inventory-design.md` é normativa.
- A interface segue `docs/design/previews/03-products-stock-purchases-suppliers.png` e os tokens existentes.
- SQLite continua a ser a fonte operacional local.
- Todo o stock nasce de movimentos confirmados.
- Movimentos confirmados não são editados nem apagados.
- O saldo materializado deve ser igual à soma dos movimentos.
- Quantidades persistidas usam a menor unidade vendável e números inteiros.
- Valores monetários em XOF usam inteiros. Cálculos intermédios usam `decimal`.
- Datas de negócio usam o fuso `Africa/Bissau`. Instantes persistidos usam UTC.
- O stock nunca pode ficar negativo, mesmo com duas confirmações concorrentes.
- Medicamentos exigem lote e validade.
- Produtos gerais seguem as regras configuradas.
- O Caixa não lê custos, margens, fornecedores, compras ou operações administrativas.
- Catálogo, fornecedores e rascunhos podem ser preparados sem licença activa.
- Confirmações de stock em produção exigem licença activa.
- A política de teste não é seleccionável na interface nem incluída numa compilação Release de produção.
- Excel e CSV são processados localmente. O ficheiro original não é persistido.
- Não são apresentados dados de demonstração como dados reais.
- Bases, ficheiros importados, segredos, backups e dados de execução não entram no Git.
- Cada comportamento novo começa por um teste que falha.
- Cada tarefa termina com testes focados, revisão do diff e um commit.

## Task 1: Primitivos do catálogo, unidades e produtos

**Files:**

- Create: `src/Nofarma.Domain/Catalog/ProductType.cs`
- Create: `src/Nofarma.Domain/Catalog/ProductCategory.cs`
- Create: `src/Nofarma.Domain/Catalog/ProductPackage.cs`
- Create: `src/Nofarma.Domain/Catalog/ProductBarcode.cs`
- Create: `src/Nofarma.Domain/Catalog/Product.cs`
- Create: `src/Nofarma.Domain/Catalog/ProductCode.cs`
- Create: `src/Nofarma.Domain/Catalog/ProductValidationException.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Catalog/ProductTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Catalog/ProductPackageTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Catalog/ProductCodeTests.cs`

**Interfaces:**

- Produces: `Product.CreateMedicine(...)` e `Product.CreateGeneral(...)`.
- Produces: `Product.AddPackage(...)`, `Product.AddBarcode(...)`, `Product.ChangePrices(...)`, `Product.Deactivate(...)`.
- Produces: `ProductCode.Parse(...)` e `ProductCode.FromSequence(long sequence)`.
- Produces: leitura de `BaseUnit`, `RequiresLot`, `RequiresExpiry`, `MinimumStock` e `HasMovements`.

- [ ] **Step 1: Escrever testes que falham para produto e código interno**

Testar código introduzido, geração exacta de `PRD-000001`, rejeição de sequência zero, nome vazio, preço negativo, limite negativo e categoria vazia.

- [ ] **Step 2: Executar a falha correcta**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Domain.Catalog" --no-restore`

Expected: FAIL porque os tipos de catálogo ainda não existem.

- [ ] **Step 3: Implementar o produto mínimo**

Usar entidades seladas e invariantes no domínio. `Medicine` activa sempre lote e validade. `General` permite activar as duas regras. Desactivar preserva o histórico. Não incluir Entity Framework no domínio.

- [ ] **Step 4: Escrever e passar testes de embalagens e códigos de barras**

Testar factor inteiro positivo, unidade base com factor um, vários códigos por produto, rejeição de duplicados no mesmo produto e bloqueio da alteração da unidade base ou de um factor usado depois de `MarkHasMovements()`.

- [ ] **Step 5: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Domain.Catalog" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Domain/Catalog tests/Nofarma.UnitTests/Domain/Catalog
git commit -m "feat: add product catalog domain"
```

## Task 2: Fornecedores, lotes, validade e alertas

**Files:**

- Create: `src/Nofarma.Domain/Supply/Supplier.cs`
- Create: `src/Nofarma.Domain/Inventory/ExpiryDate.cs`
- Create: `src/Nofarma.Domain/Inventory/StockLot.cs`
- Create: `src/Nofarma.Domain/Inventory/StockAlert.cs`
- Create: `src/Nofarma.Domain/Inventory/StockAlertLevel.cs`
- Create: `src/Nofarma.Domain/Inventory/StockAlertCalculator.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Supply/SupplierTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Inventory/ExpiryDateTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Inventory/StockLotTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Inventory/StockAlertCalculatorTests.cs`

**Interfaces:**

- Produces: `Supplier.Create(...)`, `Supplier.Update(...)`, `Supplier.Deactivate(...)`.
- Produces: `ExpiryDate.ForMonth(...)`, `ExpiryDate.ForDay(...)`, `ExpiryDate.GetBlockingInstant(...)`.
- Produces: `StockLot.Create(...)` e identidade de lote por produto, número e validade.
- Produces: `StockAlertCalculator.CalculateStock(...)` e `CalculateExpiry(...)`.

- [ ] **Step 1: Escrever testes de validade que falham**

Testar Fevereiro de ano bissexto, fim do mês, data exacta, bloqueio no início do dia e conversão no fuso `Africa/Bissau`.

- [ ] **Step 2: Implementar `ExpiryDate` sem dependência de cultura**

Guardar ano, mês e dia opcional. Calcular a data de bloqueio sem usar a data actual do sistema dentro da entidade.

- [ ] **Step 3: Escrever testes de fornecedor e lote que falham**

Testar nome obrigatório, preservação de fornecedor inactivo, medicamento sem lote, reutilização do mesmo número com validade diferente e lote expirado bloqueado.

- [ ] **Step 4: Implementar fornecedor e lote mínimos**

O domínio expõe a identidade do lote e rejeita saldo inicial directo. O saldo será alterado apenas pelo livro de movimentos na Task 3.

- [ ] **Step 5: Escrever e passar testes de alertas**

Testar limite global 10, substituição por produto, `Normal`, `LowStock`, `OutOfStock` e validade aos 90, 60 e 30 dias. Testar as fronteiras exactas.

- [ ] **Step 6: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Domain.Inventory|FullyQualifiedName~Domain.Supply" --no-restore`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/Nofarma.Domain/Inventory src/Nofarma.Domain/Supply tests/Nofarma.UnitTests/Domain
git commit -m "feat: add suppliers lots and alerts"
```

## Task 3: Livro imutável de movimentos e FEFO

**Files:**

- Create: `src/Nofarma.Domain/Inventory/StockMovementType.cs`
- Create: `src/Nofarma.Domain/Inventory/StockMovement.cs`
- Create: `src/Nofarma.Domain/Inventory/StockLedger.cs`
- Create: `src/Nofarma.Domain/Inventory/StockOperation.cs`
- Create: `src/Nofarma.Domain/Inventory/FefoAllocator.cs`
- Create: `src/Nofarma.Domain/Inventory/InsufficientStockException.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Inventory/StockLedgerTests.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Inventory/FefoAllocatorTests.cs`

**Interfaces:**

- Produces: `StockLedger.CreateMovement(...)` com quantidade base assinada.
- Produces: `StockLedger.Compensate(...)` sem editar o movimento original.
- Produces: `FefoAllocator.Allocate(requiredQuantity, lots, businessDate)`.

- [ ] **Step 1: Escrever testes de movimentos que falham**

Cobrir todos os tipos aprovados, motivo obrigatório para ajuste, perda, dano e compensação, referência de origem, chave idempotente e rejeição de quantidade zero.

- [ ] **Step 2: Implementar o movimento imutável mínimo**

Não expor métodos de edição. Usar um movimento novo para compensar. A entidade não calcula o saldo consultando persistência.

- [ ] **Step 3: Escrever testes de saldo e FEFO que falham**

Testar stock negativo, lote expirado, ordenação por data de bloqueio, desempate por primeira entrada e FIFO para produto sem validade.

- [ ] **Step 4: Implementar o cálculo puro**

`FefoAllocator` devolve uma lista de parcelas por lote. Não grava dados. Rejeita a operação inteira quando a soma válida é insuficiente.

- [ ] **Step 5: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~StockLedgerTests|FullyQualifiedName~FefoAllocatorTests" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Domain/Inventory tests/Nofarma.UnitTests/Domain/Inventory
git commit -m "feat: add immutable stock ledger"
```

## Task 4: Permissões e política de licença

**Files:**

- Modify: `src/Nofarma.Domain/Identity/Capability.cs`
- Modify: `src/Nofarma.Domain/Identity/RolePermissions.cs`
- Create: `src/Nofarma.Application/Abstractions/IStockOperationPolicy.cs`
- Create: `src/Nofarma.Application/Inventory/StockOperationPolicyResult.cs`
- Create: `src/Nofarma.Infrastructure/Licensing/InstallationStockOperationPolicy.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Identity/RolePermissionsTests.cs`
- Test: `tests/Nofarma.UnitTests/Application/Inventory/StockOperationPolicyTests.cs`

**Interfaces:**

- Adds: `ViewSuppliers`, `ViewPurchases`, `ImportInventory`, `AdjustStock`, `CompensateStock`.
- Produces: `IStockOperationPolicy.CanConfirmAsync(...)`.

- [ ] **Step 1: Escrever testes de negação por omissão**

Confirmar que o Caixa não pode ler custos, compras ou fornecedores. Confirmar que Farmacêutico consulta stock sem gerir. Confirmar permissões explícitas de Administrador, Gestor, Responsável de stock e Auditor.

- [ ] **Step 2: Alterar a matriz de permissões**

Enumerar cada capacidade. Não conceder todas as capacidades ao Administrador por regra implícita.

- [ ] **Step 3: Escrever testes da política de licença**

Testar que `Preparing` e `ReadyForActivation` permitem rascunhos mas bloqueiam confirmações. Testar que `Active` permite confirmação. Criar uma implementação falsa apenas no projecto de testes para percursos automatizados.

- [ ] **Step 4: Implementar a política de produção**

Consultar o estado persistido da instalação. Não aceitar variável de ambiente, parâmetro da interface ou código secreto para contornar a licença.

- [ ] **Step 5: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~RolePermissionsTests|FullyQualifiedName~StockOperationPolicyTests" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Domain/Identity src/Nofarma.Application src/Nofarma.Infrastructure/Licensing tests/Nofarma.UnitTests
git commit -m "feat: authorize inventory operations"
```

## Task 5: Modelo SQLite e migração de inventário

**Files:**

- Modify: `src/Nofarma.Infrastructure/Persistence/NofarmaDbContext.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/ProductCategoryRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/ProductRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/ProductPackageRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/ProductBarcodeRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/SupplierRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/PurchaseOrderRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/PurchaseOrderLineRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/GoodsReceiptRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/GoodsReceiptLineRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/StockLotRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/StockMovementRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/InventoryImportRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/InventoryImportRowRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Records/InventoryImportErrorRecord.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Configurations/*Inventory*.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/Migrations/*_AddInventory.cs`
- Test: `tests/Nofarma.IntegrationTests/Inventory/InventoryDatabaseTests.cs`

**Interfaces:**

- Adds: `DbSet` para todas as tabelas da especificação.
- Adds: índices únicos por farmácia para código interno, código de barras, sequência de código, identidade de lote e chave idempotente.

- [ ] **Step 1: Escrever o teste SQLite que falha**

Aplicar migrações numa base temporária real. Confirmar tabelas, chaves externas, índices e ausência de eliminações em cascata sobre histórico.

- [ ] **Step 2: Criar os records e configurações EF Core**

Guardar quantidades como `INTEGER`, XOF como `INTEGER`, custos médios intermédios como texto decimal invariant e instantes UTC como o padrão já usado. Incluir `RowVersion` numérico ou token equivalente para actualizações concorrentes de saldo.

- [ ] **Step 3: Proteger o livro e a auditoria**

Estender `NofarmaDbContext` para rejeitar modificação ou eliminação de `StockMovementRecord` e `AuditEventRecord`. Rejeitar eliminação de documentos confirmados no serviço e nas relações.

- [ ] **Step 4: Gerar a migração**

Run: `dotnet tool restore`

Run: `dotnet ef migrations add AddInventory --project src/Nofarma.Infrastructure --startup-project src/Nofarma.Infrastructure --output-dir Persistence/Migrations`

- [ ] **Step 5: Executar o teste SQLite**

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~InventoryDatabaseTests" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Infrastructure/Persistence tests/Nofarma.IntegrationTests/Inventory
git commit -m "feat: persist inventory records"
```

## Task 6: Casos de uso de catálogo e fornecedores

**Files:**

- Create: `src/Nofarma.Application/Abstractions/ICatalogStore.cs`
- Create: `src/Nofarma.Application/Abstractions/ISupplierStore.cs`
- Create: `src/Nofarma.Application/Catalog/ProductRequests.cs`
- Create: `src/Nofarma.Application/Catalog/ProductDtos.cs`
- Create: `src/Nofarma.Application/Catalog/ProductService.cs`
- Create: `src/Nofarma.Application/Supply/SupplierRequests.cs`
- Create: `src/Nofarma.Application/Supply/SupplierDtos.cs`
- Create: `src/Nofarma.Application/Supply/SupplierService.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/SqliteCatalogStore.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/SqliteSupplierStore.cs`
- Test: `tests/Nofarma.UnitTests/Application/Catalog/ProductServiceTests.cs`
- Test: `tests/Nofarma.UnitTests/Application/Supply/SupplierServiceTests.cs`
- Test: `tests/Nofarma.IntegrationTests/Inventory/CatalogStoreTests.cs`

**Interfaces:**

- Produces: criar, alterar, desactivar, pesquisar e obter detalhe de produto.
- Produces: gerar código interno transaccional quando omitido.
- Produces: criar, alterar, desactivar, pesquisar e obter fornecedor.
- Consumes: sessão activa e `AuthorizationService` antes de ler ou alterar dados protegidos.

- [ ] **Step 1: Escrever testes dos serviços que falham**

Testar validação, autorização, código gerado, código de barras duplicado, tentativa de editar conversão usada e desactivação sem perda histórica.

- [ ] **Step 2: Implementar casos de uso mínimos**

Separar DTOs públicos das entidades do domínio. O DTO para Caixa não contém campos de custo. Não esconder apenas na interface.

- [ ] **Step 3: Escrever testes de integração do código e unicidade**

Criar dois produtos em operações concorrentes e confirmar códigos diferentes. Testar colisões de código interno e de código de barras com mensagens estáveis.

- [ ] **Step 4: Implementar os stores SQLite**

Usar transacções curtas. Traduzir violações de índice para erros de aplicação. Incluir auditoria na mesma gravação.

- [ ] **Step 5: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~ProductServiceTests|FullyQualifiedName~SupplierServiceTests" --no-restore`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~CatalogStoreTests" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Application src/Nofarma.Infrastructure/Persistence tests
git commit -m "feat: manage products and suppliers"
```

## Task 7: Confirmação transaccional de stock

**Files:**

- Create: `src/Nofarma.Application/Abstractions/IInventoryStore.cs`
- Create: `src/Nofarma.Application/Inventory/StockEntryRequest.cs`
- Create: `src/Nofarma.Application/Inventory/StockAdjustmentRequest.cs`
- Create: `src/Nofarma.Application/Inventory/StockDtos.cs`
- Create: `src/Nofarma.Application/Inventory/InventoryService.cs`
- Create: `src/Nofarma.Application/Inventory/InventoryQueryService.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/SqliteInventoryStore.cs`
- Test: `tests/Nofarma.UnitTests/Application/Inventory/InventoryServiceTests.cs`
- Test: `tests/Nofarma.IntegrationTests/Inventory/StockTransactionTests.cs`
- Test: `tests/Nofarma.IntegrationTests/Inventory/StockConcurrencyTests.cs`

**Interfaces:**

- Produces: inventário inicial, entrada rápida, ajuste, perda, dano, expiração, devolução e compensação.
- Produces: consultas por produto e lote, saldo total, alertas e selecção FEFO.

- [ ] **Step 1: Escrever testes do caso de uso que falham**

Testar autorização, licença, motivo obrigatório, regras do produto, lote expirado, idempotência e auditoria.

- [ ] **Step 2: Implementar o serviço contra uma store falsa**

O serviço valida antes da transacção e passa uma operação completa à store. A chave idempotente é obrigatória em toda a confirmação.

- [ ] **Step 3: Escrever testes SQLite de atomicidade**

Injectar falha entre movimento, saldo e auditoria. Reabrir a base e confirmar que nada ficou gravado. Confirmar que repetir a chave devolve o resultado original.

- [ ] **Step 4: Implementar a store transaccional**

Criar ou reutilizar lote válido, inserir movimento, actualizar saldo e criar auditoria numa única transacção. Usar uma actualização condicional sobre o saldo para impedir quantidades negativas.

- [ ] **Step 5: Escrever e passar o teste de concorrência**

Executar duas saídas simultâneas que, juntas, excedem o saldo. Exactamente uma deve passar. O saldo final deve ser não negativo e igual à soma dos movimentos.

- [ ] **Step 6: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~InventoryServiceTests" --no-restore`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~StockTransactionTests|FullyQualifiedName~StockConcurrencyTests" --no-restore`

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/Nofarma.Application/Inventory src/Nofarma.Application/Abstractions/IInventoryStore.cs src/Nofarma.Infrastructure/Persistence/SqliteInventoryStore.cs tests
git commit -m "feat: confirm stock movements atomically"
```

## Task 8: Compras e recepções parciais

**Files:**

- Create: `src/Nofarma.Domain/Purchasing/PurchaseOrderStatus.cs`
- Create: `src/Nofarma.Domain/Purchasing/PurchaseOrder.cs`
- Create: `src/Nofarma.Domain/Purchasing/PurchaseOrderLine.cs`
- Create: `src/Nofarma.Application/Abstractions/IPurchaseStore.cs`
- Create: `src/Nofarma.Application/Purchasing/PurchaseRequests.cs`
- Create: `src/Nofarma.Application/Purchasing/PurchaseDtos.cs`
- Create: `src/Nofarma.Application/Purchasing/PurchaseService.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/SqlitePurchaseStore.cs`
- Test: `tests/Nofarma.UnitTests/Domain/Purchasing/PurchaseOrderTests.cs`
- Test: `tests/Nofarma.UnitTests/Application/Purchasing/PurchaseServiceTests.cs`
- Test: `tests/Nofarma.IntegrationTests/Inventory/PurchaseReceiptTests.cs`

**Interfaces:**

- Produces: criar, editar e cancelar rascunho.
- Produces: receber parcialmente, concluir recepção e consultar histórico.
- Consumes: `IStockOperationPolicy`, permissões e regras de lote.

- [ ] **Step 1: Escrever testes do agregado que falham**

Testar estados, quantidades recebidas, recepção excessiva, cancelamento de rascunho, proibição de apagar compra recebida e totais XOF sem `float` ou `double`.

- [ ] **Step 2: Implementar o agregado mínimo**

O agregado calcula `Draft`, `PartiallyReceived`, `Received` e `Cancelled`. As linhas preservam embalagem e factor documental.

- [ ] **Step 3: Escrever testes de aplicação e integração**

Testar custos ocultos por função, documento opcional até à recepção, lote obrigatório e falha atómica entre recepção e movimento.

- [ ] **Step 4: Implementar serviço e store**

Na confirmação, gravar recepção, linhas, lote, movimento, saldo, estado da compra e auditoria na mesma transacção.

- [ ] **Step 5: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Purchasing" --no-restore`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~PurchaseReceiptTests" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Nofarma.Domain/Purchasing src/Nofarma.Application/Purchasing src/Nofarma.Application/Abstractions/IPurchaseStore.cs src/Nofarma.Infrastructure/Persistence/SqlitePurchaseStore.cs tests
git commit -m "feat: add purchases and goods receipts"
```

## Task 9: Importação local de Excel e CSV

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/Nofarma.Infrastructure/Nofarma.Infrastructure.csproj`
- Create: `src/Nofarma.Application/Abstractions/IInventoryFileReader.cs`
- Create: `src/Nofarma.Application/Abstractions/IInventoryImportErrorWriter.cs`
- Create: `src/Nofarma.Application/Abstractions/IInventoryImportStore.cs`
- Create: `src/Nofarma.Application/Inventory/Import/ImportColumn.cs`
- Create: `src/Nofarma.Application/Inventory/Import/ImportDraftDtos.cs`
- Create: `src/Nofarma.Application/Inventory/Import/InventoryImportService.cs`
- Create: `src/Nofarma.Infrastructure/Import/CsvInventoryFileReader.cs`
- Create: `src/Nofarma.Infrastructure/Import/OpenXmlInventoryFileReader.cs`
- Create: `src/Nofarma.Infrastructure/Import/InventoryFileReader.cs`
- Create: `src/Nofarma.Infrastructure/Import/OpenXmlInventoryImportErrorWriter.cs`
- Create: `src/Nofarma.Infrastructure/Persistence/SqliteInventoryImportStore.cs`
- Test: `tests/Nofarma.UnitTests/Application/Inventory/InventoryImportServiceTests.cs`
- Test: `tests/Nofarma.IntegrationTests/Inventory/InventoryFileReaderTests.cs`
- Test: `tests/Nofarma.IntegrationTests/Inventory/InventoryImportConfirmationTests.cs`
- Test fixture: `tests/Nofarma.IntegrationTests/Fixtures/inventory-valid.xlsx`
- Test fixture: `tests/Nofarma.IntegrationTests/Fixtures/inventory-conflicts.csv`

**Interfaces:**

- Produces: listar folhas, ler cabeçalhos, associar colunas, validar rascunho, corrigir linha, confirmar e exportar erros.
- Adds: `DocumentFormat.OpenXml` 3.5.1 com versão centralizada e lock file actualizado.

- [ ] **Step 1: Escrever testes de limites e parsing que falham**

Testar extensão inválida, 20 MiB, 50 000 linhas, UTF-8, vírgula, ponto e vírgula, folha seleccionada, datas Excel e fórmulas sem execução.

- [ ] **Step 2: Implementar leitores em streaming**

Não carregar o livro inteiro em memória. Abrir em modo de leitura. Tratar células partilhadas, datas e números com cultura invariant. Rejeitar ficheiros encriptados ou malformados com erro público seguro.

- [ ] **Step 3: Escrever testes da correspondência**

Testar código de barras primeiro, código interno depois e nunca nome. Testar duplicados, conflito de códigos, factores inválidos, valores negativos e medicamento sem lote ou validade.

- [ ] **Step 4: Implementar rascunho e hash**

Calcular SHA-256 durante a leitura. Persistir dados normalizados, contagens, erros e hash, não o ficheiro original nem o seu caminho completo.

Implementar a exportação dos erros para um novo `.xlsx` com número da linha, campo, valor recebido, código do erro e explicação legível. O ficheiro exportado não inclui caminhos locais, hashes internos ou outros dados técnicos sensíveis.

- [ ] **Step 5: Escrever e passar testes de confirmação idempotente**

Confirmar que a leitura não altera stock. Confirmar que apenas Administrador autorizado cria produtos, lotes e movimentos `OpeningInventory`. Repetir a confirmação e obter o mesmo resultado sem duplicação.

- [ ] **Step 6: Executar testes focados**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~InventoryImportServiceTests" --no-restore`

Run: `dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~InventoryFileReaderTests|FullyQualifiedName~InventoryImportConfirmationTests" --no-restore`

Expected: PASS.

- [ ] **Step 7: Verificar dependências**

Run: `dotnet list Nofarma.slnx package --vulnerable --include-transitive`

Expected: nenhuma vulnerabilidade conhecida.

- [ ] **Step 8: Commit**

```powershell
git add Directory.Packages.props src/Nofarma.Infrastructure src/Nofarma.Application tests
git commit -m "feat: import opening inventory locally"
```

## Task 10: Páginas Windows e integração no shell

**Files:**

- Create: `src/Nofarma.Desktop/ViewModels/ProductsViewModel.cs`
- Create: `src/Nofarma.Desktop/ViewModels/StockViewModel.cs`
- Create: `src/Nofarma.Desktop/ViewModels/PurchasesViewModel.cs`
- Create: `src/Nofarma.Desktop/ViewModels/SuppliersViewModel.cs`
- Create: `src/Nofarma.Desktop/ViewModels/InventoryImportViewModel.cs`
- Create: `src/Nofarma.Desktop/Views/ProductsPage.xaml`
- Create: `src/Nofarma.Desktop/Views/ProductsPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/StockPage.xaml`
- Create: `src/Nofarma.Desktop/Views/StockPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/PurchasesPage.xaml`
- Create: `src/Nofarma.Desktop/Views/PurchasesPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/SuppliersPage.xaml`
- Create: `src/Nofarma.Desktop/Views/SuppliersPage.xaml.cs`
- Create: `src/Nofarma.Desktop/Views/InventoryImportPage.xaml`
- Create: `src/Nofarma.Desktop/Views/InventoryImportPage.xaml.cs`
- Modify: `src/Nofarma.Desktop/Views/AppShellPage.xaml`
- Modify: `src/Nofarma.Desktop/Views/AppShellPage.xaml.cs`
- Modify: `src/Nofarma.Desktop/Composition/ServiceCollectionExtensions.cs`
- Modify: `src/Nofarma.Infrastructure/Composition/ServiceCollectionExtensions.cs`
- Test: `tests/Nofarma.UnitTests/Desktop/InventoryViewModelTests.cs`
- Test: `tests/Nofarma.UnitTests/Composition/SurfaceInventoryTests.cs`

**Interfaces:**

- Replaces: estados vazios de Produtos, Stock, Compras e Fornecedores.
- Produces: assistente de importação em quatro passos.
- Produces: contagens reais de stock baixo, esgotado e validade na barra superior.

- [ ] **Step 1: Escrever testes de view model que falham**

Testar estado inicial vazio, pesquisa, validação por campo, carregamento, bloqueio de duplo clique, permissões e mensagens sem detalhes técnicos.

- [ ] **Step 2: Implementar view models pequenos**

Não colocar SQL, Entity Framework ou regras de saldo no code-behind. Usar `CancellationToken` para pesquisa e impedir que uma resposta antiga substitua uma mais recente.

- [ ] **Step 3: Implementar Produtos e Fornecedores**

Seguir a prancha 3. Usar pesquisa persistente, filtros compactos, lista e formulário lateral. Mostrar estado vazio honesto. Mostrar custos apenas a perfis autorizados.

- [ ] **Step 4: Implementar Stock e Compras**

Mostrar produto, lote, quantidade, validade, estado e fornecedor permitido. O formulário de recepção só activa confirmação quando lote, validade, custo e quantidade cumprem as regras.

- [ ] **Step 5: Implementar o assistente de importação**

Criar os quatro passos aprovados. Permitir voltar sem perder correcções. Exigir confirmação explícita. Mostrar que a confirmação está bloqueada quando não existe licença activa, sem bloquear a preparação do rascunho.

- [ ] **Step 6: Ligar shell e alertas reais**

Substituir `ModuleEmptyPage` nos quatro destinos. Mostrar contagens calculadas. Não usar números fixos nem copiar produtos da imagem.

- [ ] **Step 7: Validar build e composição**

Run: `dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~InventoryViewModelTests|FullyQualifiedName~SurfaceInventoryTests" --no-restore`

Run: `dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj --configuration Debug --no-restore -warnaserror`

Expected: PASS e build sem avisos.

- [ ] **Step 8: Commit**

```powershell
git add src/Nofarma.Desktop src/Nofarma.Infrastructure/Composition tests/Nofarma.UnitTests
git commit -m "feat: add inventory desktop experience"
```

## Task 11: Verificação visual, segurança e documentação

**Files:**

- Modify: `README.md`
- Modify: `docs/development/verification.md`
- Create: `docs/development/inventory.md`
- Create: `docs/development/inventory-import-template.csv`
- Modify: `postman/Nofarma.postman_collection.json` only if an API endpoint is deliberately added, otherwise leave unchanged.

- [ ] **Step 1: Documentar a operação local**

Explicar produto, embalagem, lote, recepção, entrada rápida, alertas, rascunho de importação e bloqueio de confirmação sem licença. Não incluir credenciais, caminhos pessoais ou dados reais de farmácia.

- [ ] **Step 2: Executar o percurso manual Windows**

Numa base de desenvolvimento descartável, criar produto geral e medicamento, fornecedor, compra parcial, entrada rápida e rascunho de importação. Usar a política de teste apenas no executável de desenvolvimento preparado para QA. Confirmar que uma compilação Release não expõe essa política.

- [ ] **Step 3: Comparar visualmente com a prancha aprovada**

Capturar Produtos, Stock, Compras, Fornecedores e Importação a 1366 por 768. Confirmar navegação por teclado, foco visível, contraste, texto sem corte, alvos adequados e ausência de dados fictícios.

- [ ] **Step 4: Executar verificação completa**

Run: `dotnet restore Nofarma.slnx --locked-mode`

Run: `dotnet format Nofarma.slnx --verify-no-changes --no-restore`

Run: `dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror`

Run: `dotnet test Nofarma.slnx --configuration Release --no-build`

Run: `dotnet list Nofarma.slnx package --vulnerable --include-transitive`

Expected: código 0, zero avisos, zero falhas e zero vulnerabilidades conhecidas.

- [ ] **Step 5: Rever segurança e dados sensíveis**

Pesquisar tokens, chaves, palavras-passe, PIN, ficheiros `.db`, `.sqlite`, Excel, CSV, peppers, certificados e backups. Confirmar que nenhum está staged. Confirmar mensagens públicas sem SQL, hashes ou caminhos pessoais.

- [ ] **Step 6: Verificar Git**

Run: `git diff --check`

Run: `git status --short`

Run: `git log --oneline --decorate -15`

- [ ] **Step 7: Commit e publicação**

```powershell
git add README.md docs/development postman .gitignore
git commit -m "docs: verify inventory workflow"
git push
```

## Revisão do plano

### Cobertura da especificação

- Catálogo e códigos internos: Tasks 1, 5 e 6.
- Unidades, embalagens e códigos de barras: Tasks 1 e 6.
- Fornecedores: Tasks 2, 5, 6 e 10.
- Lotes, validade e FEFO: Tasks 2, 3, 7 e 8.
- Livro imutável e saldo materializado: Tasks 3, 5 e 7.
- Stock não negativo e concorrência: Task 7.
- Compras e recepções parciais: Task 8.
- Inventário inicial por Excel ou CSV: Task 9.
- Alertas de stock e validade: Tasks 2, 7 e 10.
- Permissões e ocultação de custos: Tasks 4, 6 e 10.
- Licença e preparação offline: Tasks 4, 7, 9 e 10.
- Interface fiel ao desenho: Tasks 10 e 11.
- Segurança, dependências e ausência de dados fictícios: Tasks 9, 10 e 11.

### Decisões de implementação fixadas

- `DocumentFormat.OpenXml` 3.5.1 lê `.xlsx` localmente e sem exigir Microsoft Excel.
- CSV aceita UTF-8 com vírgula ou ponto e vírgula.
- Os leitores aplicam 20 MiB e 50 000 linhas antes da confirmação.
- Códigos gerados usam uma sequência transaccional por farmácia e nunca reciclam números.
- A base guarda quantidades em unidade base e XOF como inteiros.
- A leitura de custos é protegida no contrato de aplicação, não apenas escondida no XAML.
- Saldos usam actualização condicional e transacção SQLite para impedir stock negativo.
- Chaves idempotentes protegem recepções, entradas, ajustes e inventário inicial.
- A política de licença de produção depende apenas do estado persistido da instalação.
- A política de teste vive no projecto de testes ou numa composição Debug dedicada e não é activável pelo utilizador.
