# NôFarma: desenho técnico para concluir o piloto

Data: 5 de Agosto de 2026

Estado: proposta para aprovação

## 1. Objectivo

Concluir o piloto do NôFarma sem reduzir o âmbito já aprovado. A aplicação Windows continua a ser a fonte operacional e deve funcionar sem internet durante pelo menos 90 dias. A cloud continua incluída na primeira versão para licenciamento, sincronização, backups externos, consulta remota e administração da ABIPTOM.

O trabalho será entregue por gates pequenos e verificáveis. Cada gate termina com testes, auditoria do diff, commit e validação funcional antes de avançar.

## 2. Estado confirmado da aplicação

A base actual inclui:

- Configuração inicial da farmácia e administrador.
- Login administrativo e acesso rápido de Caixa.
- Recuperação offline de acesso, actualmente na PR 8.
- Utilizadores e permissões locais.
- Licenciamento assinado com estados de ausência, actividade, tolerância e expiração.
- Produtos, fornecedores, importação de inventário, compras, lotes e stock.
- Alertas internos de stock baixo, esgotado e validade.
- Abertura, movimentos manuais e fecho de turnos de caixa.
- Configuração local da farmácia e URLs DGCI.
- Auditoria técnica persistida.

Continuam incompletos:

- Painel operacional.
- Ponto de venda.
- Facturas, recibos, notas de crédito e impressão.
- Relatórios e consulta visual da auditoria.
- Configuração da autorização, série e intervalo DGCI.
- Backup e restauro.
- API operacional, sincronização e persistência PostgreSQL multi-tenant.
- Portal da farmácia e área ABIPTOM.
- Instalador, actualizações e validação com impressora real.

## 3. Estratégia aprovada

A conclusão segue cinco gates.

### Gate 1: venda local completa

Entrega o POS, carrinho, pagamentos, venda suspensa, integração com caixa, movimentos FEFO, recibo interno e outbox local.

### Gate 2: fiscalidade e impressão

Entrega configuração DGCI, séries, intervalos, facturas imutáveis, notas de crédito, impressão térmica simulada, A4, PDF e reimpressão.

### Gate 3: controlo local

Entrega Painel, relatórios, auditoria visual, backup, restauro e exportações.

### Gate 4: cloud e portais

Entrega API, PostgreSQL, sincronização retomável, backup externo, portal da farmácia e área ABIPTOM.

### Gate 5: distribuição e piloto

Entrega instalador, actualizações, endurecimento do VPS, observabilidade, documentação e testes com equipamento real.

Um gate não avança quando uma regra crítica do gate anterior falha.

## 4. Arquitectura do Gate 1

O Gate 1 mantém o monólito modular existente.

### 4.1 Domínio

O domínio recebe os agregados e valores seguintes:

- `Sale`, como raiz da venda.
- `SaleLine`, com produto, unidade, quantidade, preço, desconto e custo capturado.
- `SalePayment`, com método, valor e referência opcional.
- `SuspendedSale`, para guardar um carrinho sem afectar stock nem caixa.
- `Receipt`, como documento interno não fiscal.
- `SaleNumber`, como referência operacional local distinta da numeração fiscal.

As entidades usam identificadores globais e valores em XOF sem casas decimais. Nenhuma regra monetária usa `double`.

### 4.2 Aplicação

Os casos de uso principais são:

- Pesquisar produtos disponíveis para venda.
- Adicionar, alterar e remover linhas do carrinho.
- Aplicar desconto com autorização.
- Suspender e retomar uma venda.
- Preparar o pagamento e validar totais.
- Concluir uma venda numa única transacção.
- Reabrir o recibo interno para impressão.

A interface recebe respostas próprias para apresentação. Não recebe entidades do Entity Framework nem executa regras de domínio.

### 4.3 Persistência local

A migração local acrescenta tabelas para vendas, linhas, pagamentos, vendas suspensas, recibos internos e a relação entre venda e movimentos de stock.

A conclusão de uma venda grava na mesma transacção SQLite:

1. Venda e linhas.
2. Pagamentos.
3. Movimentos de saída do stock.
4. Movimento financeiro do turno.
5. Recibo interno ou reserva fiscal quando aplicável num gate posterior.
6. Evento de auditoria.
7. Evento outbox.

Se uma destas operações falhar, nenhuma alteração é confirmada.

### 4.4 Interface Windows

O POS substitui o placeholder existente em `Vendas`.

O ecrã usa três zonas:

- Pesquisa e resultados à esquerda.
- Carrinho fixo à direita.
- Total e acção de pagamento na base do carrinho.

A pesquisa mantém o foco depois de cada leitura de código de barras. Os resultados mostram nome, unidade, lote, validade, quantidade e preço. Lotes expirados nunca aparecem como opção vendável.

Os atalhos mínimos são:

- `F2` para focar a pesquisa.
- `F4` para suspender a venda.
- `F6` para retomar uma venda.
- `F8` para abrir o pagamento.
- `Escape` para fechar o painel ou modal actual sem perder o carrinho.

A interface deve funcionar a 1366 por 768, por teclado e com foco visível. O estado sem internet continua informativo e não bloqueia a venda.

## 5. Regras críticas da venda

### 5.1 Pré-condições

Uma venda só pode ser concluída quando:

- Existe um utilizador autenticado com permissão.
- Existe um turno aberto para o utilizador e dispositivo.
- Existe pelo menos uma linha válida.
- O stock disponível cobre todas as linhas.
- Nenhum lote seleccionado está expirado.
- A soma dos pagamentos corresponde ao total.
- A licença permite novas operações.

### 5.2 Reserva e consumo de stock

O carrinho não reduz stock. O consumo acontece apenas na confirmação da venda.

A selecção usa FEFO. Quando uma quantidade exige mais de um lote, a aplicação divide internamente a saída por lote. O utilizador vê a quantidade agregada e pode consultar o detalhe.

Uma venda suspensa não reserva stock. Ao retomar, todas as linhas são novamente validadas. Isto evita stock fantasma durante longos períodos offline.

### 5.3 Pagamentos

O piloto aceita registos manuais de dinheiro, cartão, Mobile Money, transferência e pagamento misto.

O NôFarma não afirma que confirmou pagamentos junto de bancos ou operadores. Guarda o método, o valor e uma referência opcional.

Em dinheiro, o sistema calcula o troco. Um pagamento misto só fecha quando a soma é igual ao total.

### 5.4 Descontos

O desconto é aplicado por linha ou ao total conforme a permissão. O sistema guarda o valor original, o desconto, o valor final e o utilizador que autorizou.

O arredondamento ocorre uma vez no total final em XOF.

### 5.5 Idempotência local

O comando de conclusão recebe uma chave única gerada quando o pagamento abre. Repetir o comando com a mesma chave devolve a venda já concluída. Não cria nova venda, novo pagamento ou novo movimento de stock.

O botão de conclusão fica indisponível enquanto a operação decorre. Esta protecção visual não substitui a idempotência na aplicação e na base de dados.

## 6. Fronteira fiscal do Gate 2

Sem autorização, série e intervalo DGCI configurados, a venda produz apenas recibo interno. O documento deve apresentar claramente `Recibo interno não fiscal`.

Uma factura fiscal só pode ser criada quando:

- A farmácia registou a autorização aplicável.
- A série está activa.
- O próximo número pertence ao intervalo autorizado.
- Os campos obrigatórios estão presentes.

A farmácia trata da sua autorização com a DGCI. O NôFarma valida a configuração e bloqueia declarações fiscais sem esses dados. O sistema não inventa integração directa com o Kontaktu nem afirma certificação não comprovada.

As facturas emitidas são imutáveis. A correcção usa nota de crédito ligada ao documento original. Impressão e reimpressão não alteram a sequência.

## 7. Impressão

A venda é confirmada antes da impressão. A impressão nunca participa na transacção de venda.

O Gate 1 cria um recibo interno renderizável e um simulador de impressão. O Gate 2 acrescenta:

- ESC/POS de 80 mm.
- A4 pelo sistema de impressão do Windows.
- PDF.
- QR e URL fiscal configuráveis.
- Segunda via identificada.

Uma falha de impressão mantém a venda concluída. A interface oferece repetir, pré-visualizar ou guardar PDF. A aplicação não volta a registar a venda.

## 8. Painel, relatórios e auditoria

O Gate 3 substitui os placeholders por consultas sobre dados reais.

O Painel apresenta vendas do dia, valor por método, turno actual, stock baixo, esgotados e validade próxima. Um valor indisponível é apresentado como indisponível. Não é substituído por dado fictício.

Os relatórios usam consultas de leitura e nunca recalculam o histórico a partir de preços actuais. Custo, preço e desconto ficam capturados na venda.

A auditoria visual permite filtrar por data, utilizador, operação, entidade e resultado. Dados técnicos sensíveis não são apresentados ao utilizador comum.

## 9. Backup e restauro

O backup local usa uma cópia consistente da base SQLite e inclui manifesto, versão de esquema, hash e data.

O restauro segue quatro passos:

1. Validar ficheiro, hash e versão.
2. Criar backup de segurança do estado actual.
3. Restaurar para localização temporária e executar verificação de integridade.
4. Substituir a base apenas depois de todas as validações passarem.

Um backup inválido nunca substitui o último estado válido.

## 10. Cloud e sincronização

A cloud não participa na conclusão local de vendas.

O serviço de sincronização lê a outbox por sequência local, envia lotes e aguarda confirmação. Cada evento inclui farmácia, dispositivo, sequência, versão de esquema, instante UTC e hash de integridade.

A API aplica isolamento por farmácia e idempotência. Um evento repetido devolve a confirmação anterior. A aplicação mantém o evento até receber confirmação válida.

Depois de 90 dias offline, a sincronização retoma do cursor confirmado. Não recomeça o histórico nem duplica operações.

O portal da farmácia é de consulta para vendas, caixa, stock, validade, sincronização, backup e licença. A área ABIPTOM administra farmácias, licenças, dispositivos, versões e suporte auditado.

## 11. Tratamento de erros

Cada erro apresentado deve indicar causa, impacto e acção possível.

- Stock alterado antes da confirmação pede revisão do carrinho.
- Turno fechado bloqueia a venda e oferece abertura de turno.
- Pagamento incompleto mantém o painel aberto.
- Falha SQLite não confirma nenhuma parte da venda.
- Falha de impressão preserva a venda e oferece nova tentativa.
- Falha de internet mantém eventos na outbox.
- Intervalo fiscal esgotado bloqueia factura oficial e permite apenas o comportamento autorizado pela configuração.

Os detalhes técnicos usam códigos de diagnóstico. Credenciais, códigos de recuperação, dados completos do cliente e chaves não entram em mensagens ou logs.

## 12. Estratégia de testes

Cada comportamento novo começa com um teste que falha pelo motivo esperado.

### 12.1 Unidade

- Totais e arredondamento em XOF.
- Conversões de unidades.
- FEFO e divisão por lotes.
- Bloqueio de lote expirado.
- Descontos e permissões.
- Pagamentos simples e mistos.
- Idempotência.
- Regras fiscais e sequência.

### 12.2 Integração

- Venda atómica em SQLite real.
- Falha intermédia com rollback total.
- Concorrência e stock insuficiente.
- Movimento de caixa e stock ligados à venda.
- Auditoria e outbox na mesma transacção.
- Repetição da mesma chave sem duplicação.
- Migração de uma instalação existente.

### 12.3 Interface

- Fluxo completo apenas por teclado.
- Leitor de código de barras simulado.
- Estados vazio, normal, erro e licença restrita.
- Layout a 1366 por 768.
- Foco visível e nomes acessíveis.
- Duplo clique e submissão repetida.

### 12.4 Gates globais

- Formatação sem alterações pendentes.
- Build Release com avisos tratados como erros.
- Testes de arquitectura, unidade e integração.
- Pesquisa de segredos e pacotes vulneráveis.
- Auditoria visual da aplicação real.

## 13. Segurança

- Nenhuma base de dados, backup, certificado ou segredo entra no Git.
- Credenciais continuam protegidas com o mecanismo local existente.
- Permissões são verificadas na aplicação e não apenas na interface.
- Operações críticas produzem auditoria.
- O portal usa autenticação separada e isolamento por farmácia.
- PostgreSQL não fica exposto directamente à internet.
- Chaves de assinatura de licença e certificados de produção não entram nas imagens Docker nem no repositório.

## 14. Dependências externas

Os seguintes pontos não bloqueiam o desenvolvimento com simuladores, mas bloqueiam a declaração de piloto concluído:

- Impressora térmica ESC/POS de 80 mm para teste físico.
- Computador semelhante ao da farmácia piloto.
- Dados de autorização, série e intervalo de uma farmácia autorizada para teste fiscal controlado.
- VPS preparado e endurecido para o ambiente cloud.

## 15. Critérios de conclusão

O piloto só é considerado concluído quando:

1. Uma venda offline actualiza venda, pagamento, caixa, stock, auditoria e outbox uma única vez.
2. Uma falha de energia simulada não deixa uma venda parcial.
3. Uma falha de impressão não duplica a venda.
4. Facturas oficiais ficam bloqueadas sem configuração DGCI válida.
5. Backup e restauro são demonstrados com verificação de integridade.
6. Noventa dias de eventos sincronizam sem duplicação.
7. O portal respeita o isolamento por farmácia.
8. Instalação, actualização e reversão funcionam no equipamento do piloto.
9. O fluxo de caixa funciona a 1366 por 768 e apenas por teclado.
10. Todos os gates automatizados e visuais passam sem segredos no repositório.
