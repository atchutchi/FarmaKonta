# NôFarma by ABIPTOM

## Especificação funcional, técnica e visual do piloto

Data: 27 de Julho de 2026

Estado: aprovado para planeamento. A implementação só começa depois da revisão deste ficheiro pelo responsável do produto.

## 1. Objectivo

O NôFarma é um sistema de gestão, stock, caixa e facturação para farmácias da Guiné-Bissau. A aplicação principal é nativa para Windows e trabalha offline. A cloud fornece licenciamento, sincronização, backups externos, actualizações, consulta remota e administração da ABIPTOM.

O produto deve reduzir perdas por validade, impedir vendas de lotes expirados, proteger a numeração fiscal, explicar diferenças de caixa e permitir auditoria completa.

## 2. Decisões confirmadas

- Nome: NôFarma by ABIPTOM.
- Marca: segura, rápida e clara.
- Utilizador principal: caixa da farmácia.
- Primeira versão: um computador operacional por farmácia.
- Sistemas suportados: Windows 10 22H2 de 64 bits e Windows 11.
- Política Windows 10: permitido no piloto. Novas instalações comerciais exigem Windows 11 ou actualizações de segurança estendidas activas.
- Operação offline: pelo menos 90 dias.
- Cloud: OVHcloud VPS-2 com Ubuntu Server LTS e Docker.
- Métodos de pagamento: registados manualmente, sem integração bancária ou Mobile Money.
- Alertas de stock: internos na aplicação Windows e no portal.
- Portal: uma aplicação web com áreas separadas para farmácia e ABIPTOM.
- Subscrição piloto: mensal ou anual. O anual cobra dez meses e concede doze meses.
- Facturação da subscrição: gestão manual pela ABIPTOM no piloto.
- Impressão: térmica ESC/POS de 80 mm e A4 do Windows. Validação física fica pendente de equipamento.
- Identidade: símbolo N formado por cápsulas, azul profundo e verde azulado.

## 3. Critérios de sucesso

1. Uma venda normal é concluída em menos de 30 segundos num computador de referência.
2. A falta de internet não impede login local, venda, impressão, compras, stock, caixa, relatórios ou backups locais.
3. Um lote expirado nunca pode ser vendido.
4. Uma venda não pode criar stock negativo.
5. Um número de factura nunca pode ser repetido ou reutilizado.
6. A sincronização pode retomar depois de 90 dias e não duplica operações.
7. Uma falha de impressão não perde nem duplica a venda.
8. Um backup pode ser restaurado e validado.

## 4. Âmbito funcional do piloto

### 4.1 Configuração e activação

- Dados da farmácia, NIF, endereço, contactos e logótipo.
- Moeda XOF e idioma português.
- Autorização DGCI, série, intervalo autorizado, QR e URL fiscal.
- Impressora térmica e impressora A4.
- Activação online ou por ficheiro de licença assinado.
- Assistente de diagnóstico inicial.

### 4.2 Utilizadores e permissões

Perfis iniciais:

- Administrador da farmácia.
- Gestor.
- Farmacêutico.
- Responsável de stock.
- Caixa.
- Auditor.
- Suporte ABIPTOM temporário.

O caixa não consulta preço de compra ou margem e não altera preços. Descontos, anulações, notas de crédito e ajustamentos exigem permissões específicas ou aprovação de supervisor.

### 4.3 Produtos e unidades

- Nome comercial e princípio activo.
- Dosagem, forma farmacêutica, fabricante e categoria.
- Vários códigos de barras por produto.
- Preço de compra, preço de venda e stock mínimo.
- Taxa fiscal ou fundamento de isenção.
- Estado activo e exigência configurável de receita.
- Caixa, frasco, embalagem, blister, comprimido, ampola e unidade.
- Conversões exactas entre unidades de compra e de venda.

### 4.4 Compras e fornecedores

- Cadastro de fornecedores.
- Registo de encomenda e recepção.
- Factura de fornecedor, custos, descontos e pagamentos registados.
- Criação obrigatória de lotes e validades na entrada aplicável.
- Dívidas a fornecedores e devoluções.

### 4.5 Stock, lotes e validade

- Número de lote, validade, fornecedor, custo e quantidade disponível.
- Saída automática FEFO, primeiro a expirar e primeiro a sair.
- Bloqueio absoluto de lotes expirados.
- Alertas configuráveis de 30, 60 e 90 dias.
- Stock mínimo por produto e valor global por omissão.
- Estado esgotado quando a quantidade chega a zero.
- Inventários, diferenças, perdas, danos e ajustamentos aprovados.
- Histórico imutável de movimentos.

### 4.6 Ponto de venda

- Pesquisa por código de barras, nome, princípio activo e categoria.
- Venda por diferentes unidades com conversão automática.
- Carrinho fixo, alteração de quantidade e remoção antes da conclusão.
- Desconto sujeito a permissão.
- Dinheiro, cartão, Mobile Money, transferência e pagamento misto como registos manuais.
- Suspender e retomar venda.
- Cliente e referência de receita opcionais.
- Protecção contra duplo clique e submissão repetida.
- Atalhos completos de teclado.

### 4.7 Caixa

- Abertura de turno e valor inicial.
- Entradas e saídas autorizadas.
- Vendas por método de pagamento.
- Valor esperado, valor contado e diferença.
- Fecho e relatório de turno.
- Aprovação de diferença por supervisor quando configurada.

### 4.8 Facturação

O NôFarma pode emitir facturas fiscais oficiais apenas depois de o administrador configurar a autorização DGCI, a série e o intervalo da farmácia. Sem estes dados, o sistema permite vendas e recibos internos e bloqueia a factura fiscal.

- Factura térmica de 80 mm.
- Factura A4 e PDF.
- Numeração sequencial protegida.
- Letra e identificadores exigidos para programa informático quando aplicável.
- QR e URL fiscal configuráveis.
- Segunda via identificada.
- Nota de crédito ligada à factura original.
- Proibição de edição ou eliminação física de documento emitido.
- Registo de cancelamento, impressão e reimpressão.

A farmácia é responsável por pedir a autorização de emissão à DGCI. A ABIPTOM é responsável por não apresentar como fiscal um documento sem a configuração necessária e por manter o software tecnicamente coerente com as regras adoptadas.

### 4.9 Relatórios

- Vendas diárias e mensais.
- Vendas por caixa, produto, categoria e método de pagamento.
- Valor médio por venda e unidades por venda.
- Custo dos produtos vendidos, lucro bruto e margem.
- Stock ao custo e ao preço de venda.
- Stock baixo, esgotado, sem movimento, expirado e próximo da validade.
- Valor em risco por validade.
- Compras e dívidas por fornecedor.
- Diferenças de caixa, descontos, devoluções e reimpressões.
- Sequência fiscal, facturas canceladas e notas de crédito.
- Exportação para Excel, CSV e PDF.

### 4.10 Backups e recuperação

- Backup local automático diário.
- Backup antes de actualizações.
- Cópia manual para disco externo.
- Cópia cifrada para armazenamento externo quando existir internet.
- Retenção diária de 30 dias, semanal de 12 semanas e mensal de 12 meses.
- Documentos e registos fiscais mantidos durante pelo menos cinco anos.
- Teste de integridade e assistente de restauro.

## 5. Fora do piloto

- Vários computadores na mesma farmácia.
- Várias filiais e transferências entre armazéns.
- Venda a crédito e contas correntes de clientes.
- Pagamentos integrados com bancos ou operadores móveis.
- Receitas clínicas e medicamentos controlados.
- Seguradoras, entregas e comércio electrónico.
- Aplicação móvel.
- Inteligência artificial e previsão de procura.
- Integração directa com Kontaktu sem documentação oficial de API.
- Cobrança automática das subscrições.

## 6. Arquitectura

Adopta-se um monólito modular offline-first.

### 6.1 Aplicação local

- C# e .NET 10.
- WinUI 3 e Windows App SDK estável.
- SQLite local.
- Entity Framework Core.
- Fila outbox persistente.
- Serviço de backup e restauro.
- Camada de impressão independente do fornecedor.
- Licença assinada validada localmente.

SQLite é a fonte operacional durante o trabalho da farmácia. A conclusão de uma venda não depende da API.

### 6.2 Cloud

- ASP.NET Core Web API.
- PostgreSQL.
- Portal Blazor.
- Docker Compose em Ubuntu Server LTS.
- Proxy reverso com HTTPS.
- Backups cifrados fora do VPS.
- Monitorização de disponibilidade, disco, memória, base de dados e certificados.

O VPS-2 é adequado para o piloto. É um único ponto de falha e não representa alta disponibilidade. O crescimento para vários servidores ou base gerida será decidido por métricas de carga e requisitos de continuidade.

### 6.3 Estrutura proposta

```text
Nofarma
├── src
│   ├── Nofarma.Domain
│   ├── Nofarma.Application
│   ├── Nofarma.Infrastructure
│   ├── Nofarma.Contracts
│   ├── Nofarma.Desktop
│   ├── Nofarma.Api
│   ├── Nofarma.AdminWeb
│   └── Nofarma.Sync
├── tests
│   ├── Nofarma.UnitTests
│   ├── Nofarma.IntegrationTests
│   ├── Nofarma.SyncTests
│   ├── Nofarma.PrintingTests
│   └── Nofarma.EndToEndTests
├── deploy
├── postman
└── docs
```

Os nomes internos usam `Nofarma` sem acento para evitar incompatibilidades em namespaces, URLs e nomes de ficheiros. A marca apresentada ao utilizador mantém `NôFarma`.

## 7. Integridade de dados

### 7.1 Venda atómica

Venda, linhas, pagamento, factura, caixa, movimentos de stock, auditoria e evento outbox são gravados numa única transacção SQLite. Se qualquer parte crítica falhar, nenhuma parte é confirmada.

A impressão acontece depois da confirmação. Uma falha de impressão permite nova tentativa e não anula a venda.

### 7.2 Stock por movimentos

O saldo de stock não é alterado isoladamente. Compra, venda, devolução, perda, dano, inventário e ajuste criam movimentos identificados. Correcções criam movimentos compensatórios.

### 7.3 Facturas imutáveis

Uma factura emitida não é editada nem apagada. A série e o número são reservados dentro da transacção. Um número cancelado não regressa à sequência. A correcção usa nota de crédito.

### 7.4 Sincronização

Cada dispositivo gera identificadores globalmente únicos. Cada evento inclui farmácia, dispositivo, sequência local, tipo, versão de esquema, instante local, instante UTC e hash de integridade.

A API aceita reenvio seguro através de chave de idempotência. Confirma lotes de eventos e mantém cursor de sincronização. A aplicação pode interromper e retomar sem reiniciar todo o histórico.

Na primeira versão só existe um computador operacional por farmácia. Isto remove conflitos de escrita entre caixas. O portal web é de consulta para dados operacionais.

## 8. Licenciamento

- Uma licença do piloto corresponde a uma farmácia, um estabelecimento e um computador.
- A licença é um documento assinado pela ABIPTOM.
- A chave privada de assinatura existe apenas no servidor protegido.
- A aplicação guarda a licença e a chave do dispositivo com protecção do Windows.
- A renovação acontece por internet ou importação de ficheiro offline.
- O sistema e a fila de sincronização devem tolerar pelo menos 90 dias sem internet.
- O plano mensal e o anual são geridos manualmente no portal ABIPTOM.
- A licença mensal é renovada em cada período por internet ou por ficheiro offline e inclui sete dias de tolerância.
- A licença anual pode cobrir os doze meses contratados e concede doze meses pelo valor de dez meses.
- A capacidade de trabalhar 90 dias sem internet não prolonga automaticamente uma subscrição que não foi renovada. A renovação offline não exige internet no computador da farmácia.
- Depois do fim da licença e da tolerância, a aplicação entra em modo de consulta, exportação e reimpressão.
- Os dados da farmácia nunca são apagados ou retidos como forma de cobrança.

## 9. Portal web

### 9.1 Área da farmácia

O proprietário consulta apenas dados já sincronizados:

- Vendas e caixa.
- Stock e valor do stock.
- Produtos com stock baixo.
- Produtos expirados e próximos da validade.
- Margens e produtos mais vendidos.
- Estado da sincronização e do backup.
- Exportações autorizadas.

### 9.2 Área ABIPTOM

- Farmácias, planos e subscrições.
- Dispositivos e transferências de licença.
- Versão instalada e estado de actualização.
- Última sincronização e último backup válido.
- Estado técnico e diagnóstico.
- Geração de ficheiro de licença offline.
- Acesso de suporte temporário e auditado.

Por omissão, a ABIPTOM não consulta produtos, clientes, vendas ou valores operacionais. O acesso excepcional exige autorização da farmácia, prazo, motivo e auditoria.

## 10. Segurança

- Nenhum segredo é guardado no Git ou na colecção Postman.
- `.env`, certificados, chaves, bases de dados, backups e dados de execução são ignorados.
- Segredos do VPS ficam em configuração protegida e não dentro das imagens Docker.
- Palavras-passe são guardadas como hashes fortes com parâmetros actualizáveis.
- PIN de caixa tem tentativas limitadas e permissões reduzidas.
- Autenticação da API usa tokens curtos e renovação com rotação.
- Dispositivos e sessões podem ser revogados.
- HTTPS é obrigatório.
- Backups são cifrados antes do envio.
- Logs não contêm palavras-passe, tokens, chaves, dados completos de pagamento ou informação pessoal desnecessária.
- Dados PostgreSQL são isolados por farmácia e todas as consultas exigem contexto de tenant.
- Actualizações e licenças são assinadas.
- Dependências e imagens são analisadas antes da entrega.

## 11. Tratamento de erros

- Mensagens indicam causa, impacto e acção de recuperação.
- O utilizador recebe um código de diagnóstico sem detalhes sensíveis.
- Operações locais críticas usam transacções.
- Pedidos remotos repetíveis usam idempotência.
- Retentativas automáticas usam atraso progressivo e limite.
- Falha de internet mantém o evento na fila.
- Falha de impressão oferece pré-visualização, guardar PDF e repetir.
- Disco quase cheio cria alerta antes de comprometer SQLite ou backups.
- Backup inválido não substitui o último backup válido.
- Falha de actualização restaura a versão anterior.

## 12. Impressão

A camada de impressão suporta:

- ESC/POS de 80 mm por USB ou rede.
- Texto, logótipo monocromático, QR, código de barras, corte e gaveta quando suportados.
- Impressora A4 instalada no Windows.
- Pré-visualização e PDF.
- Simulador para testes sem hardware.
- Perfil configurável de página de códigos e caracteres portugueses.

A compatibilidade física só é considerada validada depois de testes com modelos reais. O piloto não deve prometer suporte universal antes desses testes.

## 13. Interface

### 13.1 Estrutura

Barra lateral:

- Painel.
- Vendas.
- Facturas.
- Produtos.
- Stock.
- Compras.
- Fornecedores.
- Caixa.
- Relatórios.
- Auditoria.
- Utilizadores.
- Configurações.

Barra superior:

- Nome da farmácia.
- Utilizador.
- Turno.
- Internet.
- Sincronização.
- Impressora.
- Licença.
- Alertas.

### 13.2 Ponto de venda

A pesquisa mantém o foco. Os resultados mostram produto, unidade, lote, validade, stock e preço. O carrinho permanece à direita. O total é dominante e existe uma única acção principal de pagamento.

O estado offline usa uma faixa informativa. Não abre modal e não bloqueia a tarefa. Stock baixo usa texto e símbolo além de cor.

### 13.3 Acessibilidade

- WCAG 2.2 AA nas superfícies aplicáveis.
- Navegação completa por teclado.
- Foco visível.
- Contraste mínimo de 4,5:1 para texto normal.
- Estados sem dependência exclusiva de cor.
- Alvos de interacção grandes.
- Redução de movimento.
- Leitura e operação em 1366 por 768.

## 14. Testes

### 14.1 Unidade

- Conversões de unidades.
- FEFO e validade.
- Preços, descontos e arredondamentos em XOF.
- Permissões.
- Caixa e diferenças.
- Numeração fiscal e notas de crédito.
- Regras de licença.

### 14.2 Integração

- SQLite e PostgreSQL reais.
- Migrações e actualizações de esquema.
- Transacção completa de venda.
- Outbox e idempotência.
- Backups e restauro.
- Autenticação, revogação e isolamento por farmácia.

### 14.3 Cenários críticos

- Noventa dias offline.
- Interrupção de internet durante sincronização.
- Reenvio do mesmo lote de eventos.
- Falha de energia simulada durante a venda.
- Impressora desligada ou sem papel.
- Relógio do computador alterado.
- Disco cheio.
- Tentativa de editar factura.
- Tentativa de vender lote expirado.
- Tentativa de copiar licença para outro computador.
- Backup corrompido.
- Actualização interrompida.

### 14.4 Compatibilidade

- Windows 10 22H2 de 64 bits.
- Windows 11.
- Resolução 1366 por 768 e superiores.
- Teclado sem rato nas operações principais.
- Pré-visualização térmica e A4.

## 15. Infraestrutura e operação

O VPS-2 executa proxy HTTPS, API, portal, PostgreSQL e tarefas auxiliares em contentores separados. PostgreSQL não é exposto directamente à internet. Apenas portas estritamente necessárias ficam abertas.

O backup incluído do VPS é apenas uma protecção adicional. A estratégia principal usa backups PostgreSQL cifrados, retenção própria e cópia fora do VPS. Deve existir teste regular de restauro.

Ambientes:

- Desenvolvimento local.
- Teste no VPS ou ambiente isolado equivalente.
- Produção.

Cada ambiente tem credenciais distintas. Dados reais não entram no ambiente de desenvolvimento.

## 16. Postman

O workspace actual contém apenas o pedido de exemplo para `postman-echo.com/get`. A futura colecção NôFarma será organizada por:

- Saúde e versão.
- Autenticação.
- Farmácias.
- Utilizadores e permissões.
- Licenças e dispositivos.
- Sincronização.
- Backups.
- Portal da farmácia.
- Administração ABIPTOM.

URLs, tokens e credenciais ficam em variáveis locais ou no Vault. A colecção partilhada não contém segredos.

## 17. Fases de implementação

1. Fundação do repositório, solução .NET, testes, qualidade e segurança.
2. Domínio, base de dados local e regras de integridade.
3. Utilizadores, permissões, configuração e auditoria.
4. Produtos, unidades, compras, lotes, validade e stock.
5. Caixa, ponto de venda, pagamentos e impressão simulada.
6. Facturação, séries, QR, PDF e notas de crédito.
7. API, PostgreSQL, licenciamento e sincronização.
8. Portal da farmácia e área ABIPTOM.
9. Backups, restauro, actualizações e instalador.
10. Testes críticos, equipamento real, piloto e preparação operacional.

Cada fase produz testes, documentação e um commit verificável. Funcionalidades não entram na fase seguinte com testes críticos da fase actual a falhar.

## 18. Dependências externas e limitações

1. A ABIPTOM ainda não possui documentação técnica completa da DGCI para integração directa.
2. A farmácia é responsável por obter autorização de emissão e os intervalos aplicáveis.
3. A implementação fiscal usa configuração validada e não inventa uma API do Kontaktu.
4. Não existe ainda impressora térmica para validação física.
5. O Docker não está acessível no terminal actual e precisa de configuração antes da infraestrutura local.
6. O VPS ainda precisa de aquisição, endurecimento e configuração.
7. O Windows 10 normal está fora de suporte regular e exige política de transição.

## 19. Fontes verificadas

- Microsoft, compatibilidade e versões do Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview
- Microsoft, introdução ao WinUI 3: https://learn.microsoft.com/en-us/windows/apps/get-started/winui-get-started-overview
- OVHcloud, características VPS: https://www.ovhcloud.com/en/vps/
- OVHcloud, opções de backup: https://www.ovhcloud.com/en/vps/options/
- Kontaktu, legislação e modelos: https://kontaktu.mef.gw/legislation
- Despacho MF n.º 1/2023: https://kontaktu.mef.gw/api/public_files/wF5W-YkBptJKhQVgY22d.pdf

## 20. Aprovação

Foram aprovados durante a descoberta:

- Arquitectura offline-first.
- Âmbito e exclusões do piloto.
- Regras de integridade de stock, facturação e sincronização.
- Ponto de venda.
- Segurança, tratamento de erros e estratégia de testes.
- Conceito de logótipo e direcção de cor.

O próximo passo é a revisão deste documento pelo responsável do produto. Depois da aprovação escrita será criado um plano de implementação com tarefas pequenas, testes e commits.
