# NôFarma Implementation Roadmap

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement each detailed plan. Every plan uses tests first, review gates and frequent commits.

**Goal:** Entregar o piloto NôFarma em blocos que produzam software funcional, testável e passível de revisão independente.

**Architecture:** Monólito modular offline-first. A aplicação WinUI 3 conclui operações em SQLite sem depender da cloud. ASP.NET Core, PostgreSQL e Blazor fornecem licenciamento, sincronização, backups e portais no OVHcloud.

**Tech Stack:** C# 14, .NET 10, WinUI 3, Windows App SDK, SQLite, Entity Framework Core, ASP.NET Core, PostgreSQL 18, Blazor, xUnit v3, Docker Compose e GitHub Actions.

## Global Constraints

- Marca apresentada ao utilizador: `NôFarma by ABIPTOM`.
- Namespaces e nomes técnicos: `Nofarma` sem acento.
- Windows mínimo do piloto: Windows 10 22H2 de 64 bits.
- Windows recomendado: Windows 11.
- SDK de desenvolvimento fixado: .NET SDK 10.0.302.
- Operação local sem internet: pelo menos 90 dias.
- Uma farmácia, um estabelecimento e um computador operacional no piloto.
- SQLite é a fonte operacional local.
- PostgreSQL não é exposto directamente à internet.
- Facturas emitidas e movimentos de stock são imutáveis.
- Nenhum segredo, certificado, backup ou base de dados entra no Git.
- Todos os valores monetários usam XOF e aritmética inteira.
- Todas as datas persistidas usam UTC. A interface converte para o fuso configurado da farmácia.
- Todo o código novo começa por um teste que falha quando a unidade é testável.
- Cada plano termina com build, testes, revisão de segurança e commit.

## Sequência de planos

### Plano 1: Fundação técnica

Ficheiro: `docs/superpowers/plans/2026-07-27-nofarma-foundation.md`

Entrega uma solução compilável com fronteiras modulares, testes, API de saúde, shell WinUI, shell Blazor, Docker de desenvolvimento, CI e colecção Postman sem segredos.

### Plano 2: Identidade, configuração e auditoria local

Entrega activação da farmácia, utilizadores, funções, permissões, PIN, sessões, configuração DGCI, dispositivo, auditoria e protecção local.

Critério de conclusão: administrador configura uma farmácia e cria um caixa. O caixa inicia sessão offline e não consegue executar acções administrativas.

### Plano 3: Catálogo, compras, lotes e stock

Entrega produtos, códigos de barras, unidades e conversões, fornecedores, compras, lotes, validade, FEFO, movimentos, stock mínimo, inventários e alertas internos.

Critério de conclusão: uma compra aumenta stock por lote e uma saída respeita FEFO. Lotes expirados e stock negativo são bloqueados.

### Plano 4: Caixa, venda, fiscalidade e impressão

Entrega turnos, ponto de venda, pagamentos registados, venda suspensa, descontos autorizados, facturas, séries, notas de crédito, recibo térmico, A4, PDF e simulador ESC/POS.

Critério de conclusão: uma venda completa actualiza caixa, stock, factura, auditoria e outbox numa transacção. Uma falha de impressão não duplica a venda.

### Plano 5: Cloud, licenciamento, sincronização e backups

Entrega API protegida, PostgreSQL multi-tenant, licenças online e offline, idempotência, sincronização retomável, backups cifrados, retenção e restauro.

Critério de conclusão: 90 dias de eventos simulados sincronizam sem duplicação. Uma licença assinada não funciona noutro dispositivo.

### Plano 6: Portal da farmácia e área ABIPTOM

Entrega portal Blazor com áreas separadas, KPIs sincronizados, estado de backups, licenças, dispositivos, versões e suporte temporário auditado.

Critério de conclusão: um proprietário só vê a sua farmácia. A ABIPTOM não vê dados operacionais sem autorização temporária.

### Plano 7: Empacotamento, actualizações e piloto

Entrega MSIX assinado, actualizações com reversão, endurecimento do VPS, monitorização, testes Windows 10 e 11, testes com impressora real, manuais e procedimento de piloto.

Critério de conclusão: instalação limpa, actualização, reversão, backup, restauro e venda offline são demonstrados num computador semelhante ao da farmácia.

## Gate entre planos

Um plano só começa quando o anterior tem:

1. Testes previstos a passar.
2. `dotnet build` sem avisos.
3. Pesquisa de segredos sem resultados reais.
4. Documentação actualizada.
5. Commit publicado.
6. Revisão do diff concluída.

## Decisões que não bloqueiam a fundação

- Modelo físico da impressora térmica.
- Credenciais e autorização de uma farmácia piloto na DGCI.
- Compra e configuração final do VPS.
- Certificado comercial para assinatura do MSIX.

Estas decisões são necessárias antes do piloto e têm gates próprios nos planos 4, 5 e 7.
