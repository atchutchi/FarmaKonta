# Verificação do POS e vendas locais

Data da execução: 5 de Agosto de 2026

Branch: `codex/pos-sales-plan`

Commit funcional verificado: `f9d7fa4`

Sistema: Microsoft Windows 11 Home Single Language, versão 10.0.26200, 64 bits

SDK .NET: 10.0.302

## Estado do Gate 1

O Gate 1 permanece aberto. A implementação funcional está concluída e os conjuntos automatizados relevantes para vendas passam. Falta concluir o cenário manual offline numa sessão Release autenticada, com licença QA activa, perfil de Caixa, turno aberto e dados descartáveis aprovados. A aplicação Release foi deixada no ecrã de acesso para início de sessão manual.

## Implementação verificada

O fluxo local inclui pesquisa de produtos vendáveis, carrinho, descontos, pagamento em dinheiro, cartão, dinheiro móvel, transferência e pagamento misto. Inclui também cálculo de troco, suspensão e retoma, FEFO, efeitos de stock e caixa, auditoria, outbox e recibo interno não fiscal.

A conclusão usa uma transacção SQLite única. Os testes de integração confirmam rollback sem efeitos parciais. A interface bloqueia submissões repetidas e reutiliza a mesma chave idempotente numa repetição sem alteração do pedido.

O simulador de impressão altera apenas o estado visual para `Enviado ao simulador`. Não cria ficheiros, nova venda ou novo movimento.

## Comandos e resultados

`dotnet format Nofarma.slnx --verify-no-changes --no-restore`

Resultado: código 0 e nenhuma alteração de formatação.

`dotnet build Nofarma.slnx -c Release --no-restore`

Resultado: aprovado, zero avisos e zero erros.

`dotnet test tests/Nofarma.ArchitectureTests/Nofarma.ArchitectureTests.csproj -c Release --no-build`

Resultado: 3 aprovados, zero falhas e zero ignorados.

`dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj -c Release --no-build`

Resultado: 511 aprovados, zero falhas e zero ignorados.

`dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName!~Licensing"`

Resultado: 81 aprovados, zero falhas e zero ignorados.

`dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~LicensePersistenceTests|FullyQualifiedName~LicenseOperationPolicyTests|FullyQualifiedName~LicensedOperationServiceTests"`

Resultado: 30 aprovados, zero falhas e zero ignorados.

O conjunto agregado de licenciamento não foi declarado aprovado nesta execução. Os testes `QaIssuerTests` e `PublishedChannelTests` têm um problema anterior de sincronização e duração quando executados em conjunto. Esta limitação não foi escondida nem atribuída ao POS.

`dotnet list Nofarma.slnx package --vulnerable --include-transitive`

Resultado: nenhum pacote vulnerável conhecido nas fontes NuGet consultadas.

A pesquisa de padrões de segredos não encontrou credenciais. As únicas correspondências fora da documentação são duas asserções negativas que confirmam que publicações não contêm chaves privadas.

## Verificação visual

A página Vendas foi aberta numa sessão autenticada e maximizada a 1536 por 816. A estrutura mantém a barra lateral e o cabeçalho azuis, pesquisa e resultados à esquerda e carrinho fixo à direita. O total e o botão de pagamento permanecem visíveis. O estado vazio usa dados honestos e não apresenta medicamentos ou valores fictícios.

Foi detectado texto cortado no estado vazio do carrinho numa janela menor. O texto recebeu quebra de linha e os testes e a compilação voltaram a passar. A captura final da versão Release após esta correcção ainda depende do início de sessão manual.

## Cenário manual offline

Estado actual: pendente.

Ainda falta validar na mesma base descartável: login de Caixa com PIN, abertura de turno, pesquisa por nome, leitura simulada por código, venda em dinheiro com troco, venda mista, suspensão e retoma após alteração de stock, clique repetido em concluir, recibo interno e fecho do turno.

O computador está sem licença na instalação operacional observada. Uma compilação sem canal de confiança não deve aceitar uma licença assinada. O cenário deve usar a compilação QA e os directórios isolados `Nofarma-QA`, sem alterar dados reais da farmácia.

## Limitações declaradas

Não foi validada uma impressora física. Essa prova pertence ao Gate 5.

Não foi marcada a configuração fiscal como concluída. O recibo permanece interno e não fiscal. A entrega à DGCI continua a ser responsabilidade da farmácia.

Não foi iniciado o Gate 2 porque o critério de passagem manual do Gate 1 ainda não está completo.
