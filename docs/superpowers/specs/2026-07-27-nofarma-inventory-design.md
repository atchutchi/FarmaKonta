# NôFarma Produtos, Compras e Stock

Data: 27 de Julho de 2026

Estado: aprovado em diálogo. Aguarda revisão do documento escrito antes do plano de implementação.

## 1. Objectivo

Esta fase entrega catálogo de produtos, unidades e embalagens, fornecedores, compras, recepções, lotes, validade, stock por movimentos, alertas e importação inicial por Excel ou CSV.

O resultado deve permitir preparar e controlar o inventário local de uma farmácia sem internet. O ponto de venda e a facturação continuam fora desta fase, mas recebem interfaces estáveis para consumir catálogo, preços, lotes e FEFO na fase seguinte.

## 2. Princípios obrigatórios

1. SQLite continua a ser a fonte operacional local.
2. Nenhum saldo de stock é alterado directamente.
3. Todo o stock resulta de movimentos confirmados e auditáveis.
4. Movimentos confirmados não são editados nem apagados.
5. Correcções usam movimentos compensatórios.
6. Uma operação não pode criar stock negativo.
7. Medicamentos exigem lote e validade.
8. Produtos gerais podem usar lote e validade opcionais.
9. Lotes expirados ficam bloqueados.
10. A saída futura de stock usa FEFO.
11. O Caixa não consulta custos, margens, compras ou ajustamentos.
12. A aplicação não apresenta valores ou operações simulados como reais.
13. A interface segue obrigatoriamente `docs/design/previews/03-products-stock-purchases-suppliers.png` e os tokens já implementados.
14. Todos os fluxos devem funcionar localmente sem internet.
15. Segredos, bases, ficheiros importados e dados de execução não entram no Git.

## 3. Abordagem escolhida

Foi aprovada a abordagem movimentos primeiro.

Produtos, unidades, lotes, fornecedores, recepções e inventário inicial usam um único livro imutável de movimentos. O saldo disponível de cada lote é materializado para leitura rápida, mas a soma dos movimentos continua a ser a fonte de verdade verificável.

Esta abordagem exige mais estrutura do que guardar apenas um saldo actual. Em troca, evita reconstrução futura quando vendas, devoluções, sincronização e auditoria forem ligadas ao módulo.

## 4. Limites da fase

### 4.1 Incluído

- Produtos e categorias.
- Fabricantes, princípios activos, dosagem e forma farmacêutica.
- Vários códigos de barras.
- Unidades base e embalagens.
- Preço de compra indicativo e preço de venda.
- Taxa fiscal ou fundamento de isenção.
- Exigência de receita configurável.
- Fornecedores.
- Compras em rascunho.
- Recepção parcial ou total.
- Entrada rápida autorizada.
- Lotes e validade.
- Movimentos e saldos.
- Inventário inicial.
- Perdas, danos, ajustes, expiração e devolução ao fornecedor.
- Alertas de stock e validade.
- Importação `.xlsx` e `.csv`.
- Auditoria e permissões.
- Páginas Windows funcionais.

### 4.2 Excluído

- Venda e carrinho.
- Reserva de stock por venda.
- Caixa e pagamentos.
- Facturação DGCI.
- Dívidas e contabilidade completa de fornecedores.
- Sincronização cloud.
- Consulta do stock no portal remoto.
- Medicamentos controlados e receitas clínicas.
- Transferência entre estabelecimentos, porque o piloto tem um estabelecimento.

## 5. Catálogo

### 5.1 Produto

Cada produto contém:

- identificador local estável
- código interno único na farmácia
- nome comercial
- princípio activo opcional para produtos gerais
- dosagem opcional
- forma farmacêutica opcional
- fabricante opcional
- categoria
- tipo `Medicine` ou `General`
- exigência de receita
- exigência de lote
- exigência de validade
- unidade base
- preço de venda actual em XOF
- preço de compra indicativo em XOF
- stock mínimo opcional
- taxa fiscal ou fundamento de isenção
- estado activo ou inactivo
- instantes de criação e alteração em UTC

Produtos do tipo `Medicine` exigem lote e validade. Produtos `General` começam com ambas as regras opcionais, mas o administrador pode activá-las.

O código interno pode ser introduzido pelo utilizador. Quando for omitido, a aplicação gera o próximo código no formato `PRD-000001`. A sequência pertence à farmácia, é transaccional e não reutiliza números de produtos inactivos. O código pode ser alterado antes do primeiro movimento. Depois disso, qualquer alteração exige permissão administrativa e fica na auditoria.

Um produto inactivo permanece em documentos e movimentos históricos. Não aceita novas compras, entradas rápidas ou movimentos normais até ser reactivado.

### 5.2 Categorias

As categorias pertencem à farmácia. O nome é único sem distinguir maiúsculas de minúsculas. Uma categoria usada não pode ser apagada. Pode ser desactivada.

### 5.3 Códigos de barras

Um produto pode ter vários códigos de barras. Cada código identifica uma embalagem específica e é único em toda a farmácia.

A pesquisa por código exige correspondência exacta. A pesquisa textual usa código interno, nome comercial, princípio activo e fabricante.

## 6. Unidades e conversões

O stock é guardado na menor unidade vendável, chamada unidade base. Cada embalagem define um factor inteiro positivo para a unidade base.

Exemplo:

```text
Comprimido = 1 unidade base
Blister = 10 comprimidos
Caixa = 10 blisters = 100 comprimidos
```

Uma conversão nunca usa factor fraccionário. Quantidades importadas ou recebidas devem produzir um número inteiro de unidades base.

Depois do primeiro movimento de um produto, a unidade base e os factores usados no histórico ficam imutáveis. Uma correcção exige nova embalagem ou movimento compensatório. O sistema não reinterpreta movimentos antigos.

## 7. Dinheiro e arredondamento

Valores em XOF são guardados como inteiros, porque a moeda não usa casas decimais nas operações normais do produto.

Custos unitários que resultem da divisão de uma embalagem podem precisar de precisão intermédia. O sistema guarda o custo da linha e a quantidade base. Os relatórios calculam custo médio com decimal exacto e arredondam para XOF apenas na apresentação ou no total documental. Não são usados `float` ou `double` para dinheiro.

## 8. Fornecedores

Cada fornecedor contém nome, NIF opcional, telefone, email, endereço, notas e estado.

O nome é obrigatório. Um fornecedor usado numa compra ou lote não pode ser apagado. Pode ser desactivado. Fornecedores inactivos permanecem pesquisáveis no histórico, mas não podem ser escolhidos em novas compras normais.

## 9. Compras e recepções

### 9.1 Compra completa

Uma compra começa em `Draft`. Contém fornecedor, número do documento opcional até à recepção, data do documento, linhas, quantidades, unidades de compra, custos, descontos e notas.

Estados:

- `Draft`
- `PartiallyReceived`
- `Received`
- `Cancelled`

Cancelar um rascunho não cria stock. Uma compra com recepção confirmada não é apagada. A devolução usa movimentos próprios.

### 9.2 Recepção

Cada linha recebida indica quantidade, embalagem, custo, lote e validade quando aplicável. Uma recepção pode ser parcial.

A confirmação grava na mesma transacção:

- recepção
- linhas recebidas
- criação ou reutilização válida do lote
- movimentos de entrada
- saldos materializados
- estado da compra
- auditoria

Se qualquer gravação falhar, nada é confirmado.

### 9.3 Entrada rápida

A entrada rápida suporta:

- inventário inicial
- oferta
- transferência de origem externa
- documento ainda não recebido
- outra entrada justificada

Exige motivo, utilizador autorizado e auditoria. Não substitui silenciosamente uma compra. Quando um documento chegar, pode ser associado sem duplicar o movimento.

## 10. Lotes e validade

Cada lote contém produto, número, validade, fornecedor de origem opcional, custo de origem, quantidade recebida, saldo disponível e instante de primeira entrada.

Para o mesmo produto, o número de lote identifica uma única validade. Uma tentativa de reutilizar o número com validade diferente é conflito e exige revisão.

Medicamentos não podem ser recebidos sem número de lote e validade. Produtos gerais seguem as regras configuradas no produto.

Quando a embalagem apresenta apenas mês e ano, o lote fica vendável até ao último dia desse mês e é bloqueado no início do mês seguinte no fuso `Africa/Bissau`. Quando existe dia exacto, o lote fica bloqueado no início do dia indicado.

FEFO ordena lotes válidos pela primeira data de bloqueio. Em igualdade, usa a primeira entrada. Produtos sem validade usam a primeira entrada.

## 11. Movimentos de stock

Tipos suportados:

- `OpeningInventory`
- `PurchaseReceipt`
- `QuickEntry`
- `PositiveAdjustment`
- `NegativeAdjustment`
- `Loss`
- `Damage`
- `Expiration`
- `SupplierReturn`
- `Compensation`

Cada movimento contém farmácia, produto, lote quando aplicável, quantidade base assinada, tipo, motivo, documento de origem, utilizador, instante UTC e referência de compensação opcional.

O identificador da operação é idempotente. Repetir a confirmação com a mesma chave devolve o resultado anterior e não duplica stock.

Uma saída é rejeitada se exceder o saldo do lote. O saldo total do produto é a soma dos lotes disponíveis. O estado esgotado ocorre apenas quando o total é zero.

## 12. Inventário inicial e importação

### 12.1 Regra de confirmação

O Excel nunca altera o stock no momento da leitura. Cria uma importação em rascunho. O administrador revê os dados e confirma. Só então são criados produtos, lotes e movimentos `OpeningInventory` numa transacção.

### 12.2 Formatos

São aceites `.xlsx` e `.csv` até 20 MiB e 50 000 linhas. O processamento é local e nenhum ficheiro é enviado para a cloud. Ficheiros CSV devem usar UTF-8. A aplicação detecta vírgula ou ponto e vírgula como separador. Num ficheiro Excel com várias folhas, o utilizador escolhe explicitamente a folha antes de associar colunas.

Colunas suportadas:

- código interno
- código de barras
- nome comercial
- princípio activo
- dosagem
- forma
- fabricante
- categoria
- tipo de produto
- unidade base
- embalagem
- factor de conversão
- preço de compra
- preço de venda
- stock mínimo
- quantidade inicial
- lote
- validade
- fornecedor
- taxa fiscal ou isenção
- exige receita

### 12.3 Correspondência

Produtos existentes são identificados primeiro por código de barras e depois por código interno. O nome nunca actualiza automaticamente um produto existente.

Linhas duplicadas, códigos em conflito, factores inválidos, valores negativos e medicamentos sem lote ou validade ficam bloqueados para revisão.

### 12.4 Fluxo

1. Escolher ficheiro.
2. Associar colunas.
3. Validar e corrigir conflitos.
4. Confirmar produtos e inventário inicial.

Cada importação guarda identificador, hash do ficheiro, estado, data, utilizador, contagens e erros. Confirmar duas vezes a mesma importação não duplica registos. Os erros podem ser exportados para Excel.

O ficheiro original não é guardado dentro da base. Depois da leitura, a aplicação mantém apenas os dados normalizados e o hash necessário para diagnóstico e idempotência.

## 13. Alertas

O limite global de stock baixo é 10 unidades base. Cada produto pode definir outro limite.

Estados:

- `Normal`: acima do limite
- `LowStock`: igual ou abaixo do limite e acima de zero
- `OutOfStock`: zero

Validade:

- 90 dias: atenção
- 60 dias: prioridade
- 30 dias: crítico
- bloqueado: expirado

Os alertas são calculados localmente a partir dos dados confirmados. A cor nunca é o único indicador. Cada alerta apresenta texto, símbolo, produto, lote, quantidade ou data e uma acção relevante.

O painel e a barra superior mostram apenas contagens reais. Não mostram dados de demonstração.

## 14. Permissões

### Administrador

Pode gerir catálogo, fornecedores, compras, recepções, importações, inventário, ajustes e alertas.

### Gestor

Pode gerir catálogo, fornecedores, compras, recepções e consultar custos. Ajustes negativos e compensações exigem permissão explícita.

### Responsável de stock

Pode gerir catálogo, fornecedores, compras, recepções, inventário e ajustes autorizados.

### Farmacêutico

Pode consultar produtos, lotes, validade, stock e preços de venda. Não consulta custos nem confirma ajustes.

### Caixa

Pode consultar nome, apresentação, preço de venda e disponibilidade futura para venda. Não consulta custo, margem, fornecedor, compras ou movimentos administrativos.

### Auditor

Consulta catálogo, movimentos, compras e auditoria sem alterar dados.

### Suporte ABIPTOM

Não recebe acesso permanente. Mantém zero permissões operacionais por omissão.

## 15. Interface Windows

### 15.1 Produtos

Pesquisa com foco persistente, filtros compactos, lista de produtos e formulário lateral. Mostra nome, princípio activo, forma, preço, stock, estado e alertas. O detalhe gere embalagens e códigos de barras.

### 15.2 Stock

Vista por produto e lote com quantidade disponível, validade, custo quando autorizado, fornecedor e estado. Lotes bloqueados ficam identificados por texto e símbolo.

### 15.3 Compras

Lista de rascunhos e recepções. O formulário de recepção exige lote, validade e custo conforme as regras antes de activar a acção de confirmação.

### 15.4 Fornecedores

Pesquisa, lista, criação, edição segura e desactivação.

### 15.5 Importação

Assistente de quatro passos. O utilizador pode voltar sem perder correcções. Um duplo clique não confirma duas vezes.

### 15.6 Resolução e acessibilidade

Todas as páginas são utilizáveis a 1366 por 768, com teclado, foco visível, alvos adequados e texto sem corte. Tabelas usam densidade moderada e acções importantes têm texto.

## 16. Persistência local

Tabelas principais:

- `ProductCategories`
- `Products`
- `ProductPackages`
- `ProductBarcodes`
- `Suppliers`
- `PurchaseOrders`
- `PurchaseOrderLines`
- `GoodsReceipts`
- `GoodsReceiptLines`
- `StockLots`
- `StockMovements`
- `InventoryImports`
- `InventoryImportRows`
- `InventoryImportErrors`

Chaves externas usam restrição. Dados históricos não usam eliminação em cascata. Índices únicos protegem código interno, código de barras e identidades de lote.

Movimento, saldo, documento de origem, auditoria e futuro evento outbox partilham a mesma fronteira transaccional. O evento outbox pode ser introduzido quando a sincronização entrar sem mudar as regras do domínio.

## 17. Licença e preparação

Catálogo, fornecedores e rascunhos de importação podem ser preparados antes da activação da licença. Em produção, a confirmação de movimentos de stock exige uma licença válida conforme a política global já aprovada.

Testes automatizados e builds de desenvolvimento usam uma política explícita de teste. Essa política não é activável pela interface e não cria uma porta de acesso em produção.

## 18. Erros e recuperação

Validações aparecem junto ao campo e também num resumo acessível.

Uma falha SQLite mantém o rascunho e não altera stock. Uma tentativa idempotente pode ser repetida. Conflitos de importação permanecem no rascunho até correcção.

Erros públicos não incluem SQL, caminhos sensíveis, hashes ou dados do pepper. Diagnósticos técnicos usam códigos internos e auditoria.

## 19. Testes obrigatórios

### Domínio

- produto medicinal exige lote e validade
- conversões inteiras entre embalagens
- imutabilidade de conversões usadas
- identidade e conflito de lote
- cálculo de validade por mês e por dia
- FEFO
- limite global e substituição por produto
- stock baixo e esgotado
- rejeição de stock negativo
- movimento compensatório

### Aplicação

- criação e desactivação de produto
- autorização antes de ler custos
- compra em rascunho
- recepção parcial e total
- entrada rápida autorizada
- inventário inicial idempotente
- correspondência por código de barras e código interno
- bloqueio de linhas inválidas

### Integração SQLite

- migração e índices
- recepção transaccional
- reversão perante falha
- saldo materializado igual à soma dos movimentos
- concorrência de duas saídas
- confirmação repetida sem duplicação
- auditoria na mesma transacção

### Interface

- fluxo por teclado
- foco visível
- 1366 por 768
- ausência de dados simulados
- custos ocultos para Caixa
- alertas com texto e símbolo

## 20. Critérios de aceitação

A fase fica concluída quando:

1. Um administrador cria um produto com embalagens e vários códigos de barras.
2. Um medicamento não pode entrar sem lote e validade.
3. Uma compra pode ser recebida parcialmente e concluir stock de forma atómica.
4. Uma entrada rápida exige motivo e permissão.
5. A importação cria rascunho, mostra conflitos e confirma inventário uma única vez.
6. O saldo nunca fica negativo.
7. Todos os movimentos permanecem no histórico.
8. Alertas de stock e validade usam dados reais.
9. O Caixa não vê custos nem operações administrativas.
10. A aplicação continua operacional sem internet.
11. Build e testes terminam sem avisos ou falhas.
12. Nenhum ficheiro importado, base ou segredo entra no Git.

## 21. Sequência de entrega

1. Domínio de catálogo e unidades.
2. Persistência e migrações.
3. Fornecedores.
4. Livro de movimentos, lotes e alertas.
5. Compras e recepções.
6. Importação em rascunho e inventário inicial.
7. Interface Produtos, Stock, Compras e Fornecedores.
8. Integração no painel e verificação final.
