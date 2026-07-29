# Task 7 implementation report

Status: complete

Base: `a3f79e5`

Commit de implementação: `6bc7b6de932db1c55cb1e492c9b1ac415340e1f0`

## RED evidence

O primeiro comando foi executado antes de existir código de produção da Task 7:

`dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter FullyQualifiedName~LicenseViewModelTests`

Resultado: falhou com exit code 1. O compilador reportou `CS0246` para
`LicensePageSnapshot` e `ILicensePageOperations`. Isto confirmou que o
contrato de apresentação e operações testado ainda não existia.

Foram observados quatro ciclos RED adicionais antes da respectiva produção:

1. `CanonicalLicenseActivationRequestJsonTests` falhou com `CS0103` e
   `CS0246` porque o serializador e o envelope versionado não existiam.
2. `LicensePageOperationsTests` falhou com `CS0234` porque o adaptador Desktop
   ainda não existia.
3. `DesktopLicenseConfigurationTests` falhou com `CS0246` porque a
   configuração compilada dos canais ainda não existia.
4. O teste de composição explícita QA falhou com `CS1501` porque
   `AddNofarmaLocalIdentity` ainda não aceitava canal e chaves públicas.

## Implementation

O view-model apresenta os sete estados com texto literal em pt-PT, plano,
datas inclusivas, tolerância e impressão digital pública do dispositivo.
Importações rejeitadas antes da persistência preservam o estado anterior.
Importações persistidas recarregam o estado e notificam o shell. Se a recarga
falhar ou for cancelada depois da persistência, a interface declara que a
licença foi instalada mas que o novo estado ainda não foi confirmado. Nunca
afirma nesse caso que o estado anterior foi mantido. Cancelamentos anteriores
à persistência são propagados e o estado ocupado é sempre reposto.

O pedido `.nofarma-request` usa JSON UTF-8 canónico com versão 1 e limite de
4096 bytes. Contém apenas canal, farmácia, estabelecimento, dispositivo e
impressão digital pública. O parser rejeita campos desconhecidos, repetidos,
identificadores vazios, versões futuras, JSON não canónico e excesso de
tamanho. Exige ainda `EstablishmentId == PharmacyId` durante o piloto e uma
impressão digital no formato exacto `SHA256:` seguido de 64 caracteres
hexadecimais maiúsculos. O formato público fica disponível para o emissor da
Task 8.

A página WinUI usa os recursos existentes, dois grupos funcionais, alvos de
44 píxeis, nomes de automação, foco nativo, `InfoBar`, `ProgressRing` e os
pickers nativos. A escrita ocorre apenas depois da escolha explícita do
utilizador. A leitura da licença é limitada a 64 KiB.

O canal `Unlicensed` é a omissão e não aceita chaves. O canal QA usa a pasta
local separada `ABIPTOM\Nofarma-QA`, mostra permanentemente `Modo QA` e só
incorpora o recurso público previsto. O canal Commercial exige o recurso
público comercial no build. Não existe selecção de canal por ambiente ou
argumento de runtime.

## Correcções após revisão independente

O directório histórico de credenciais voltou a ser
`%LOCALAPPDATA%\ABIPTOM\Nofarma\secrets`. A composição recebe agora, de forma
explícita e independente, `credentialSecretsDirectory` e
`licensingSecretsDirectory`. O teste de upgrade cria uma instalação, uma
palavra-passe, um PIN e um código de recuperação, cria também a identidade de
licença no outro directório, reinicia a composição e confirma as três formas
de acesso. Um mutation check que voltou a ligar o pepper ao directório de
licenciamento fez o teste falhar como esperado.

O carregamento da página passa por `PageLoadCancellationPolicy`. O
cancelamento provocado por `OnUnloaded` fica contido no limite do evento
`async void`, enquanto um cancelamento alheio à vida da página continua a ser
propagado. O rótulo `Modo QA` está presente na configuração inicial, login,
shell e página de licença.

## Visual QA

A janela foi configurada para 1366 por 768. A captura fornecida pelo controlo
nativo do Windows tem 1080 por 608 devido à escala do sistema. Foi comparada
com `docs/design/previews/04-reports-audit-users-settings.png`, em especial o
painel 4.

Captura final:
`.superpowers/sdd/2026-07-28-nofarma-signed-licensing/task-7-license-page.png`

Foram corrigidos o título provisório da janela, o corte da descrição do
estado, o contraste da navegação e a persistência visual do destino
`Licença`. Os pickers de exportação e importação abriram e cancelaram sem
falha. A ausência de ícones na navegação ficou classificada como melhoria P3
porque não bloqueia uso, acessibilidade ou conformidade com o âmbito.

## GREEN evidence

1. Testes focados das correcções Desktop e JSON canónico: 42 aprovados.
2. Teste focado de composição explícita QA: 1 aprovado.
3. Build Desktop Release `Unlicensed`: 0 avisos e 0 erros.
4. Build Desktop Release `Commercial`: falha esperada com o erro exacto
   `NFLC001: commercial public key is not provisioned`.
5. Build Release completo com `-warnaserror`: 0 avisos e 0 erros.
6. Suite Release completa: 3 testes de arquitectura, 450 testes unitários e
   105 testes de integração aprovados.
7. `git diff --check`: aprovado.
8. Pesquisa de material privado em código, testes e output Desktop: nenhum
   padrão ou ficheiro suspeito encontrado.
9. Pesquisa de selecção de canal por ambiente ou linha de comandos: nenhuma
   correspondência encontrada.
10. `dotnet format --verify-no-changes` limitado aos ficheiros C# desta tarefa:
    aprovado.
11. Build Desktop Release `QA`: 0 avisos e 0 erros.

A primeira execução da suite completa expôs dois resultados. O teste de
superfície detectou a alteração acidental do limiar adaptativo de 1450 para
1280. O limiar foi reposto e o contrato foi actualizado apenas para reflectir
o novo estado de licença sempre visível. Um teste de integração já existente
falhou durante a remoção de `STOCK.DB-SHM.tmp`, que estava temporariamente em
uso. O mesmo teste passou isoladamente e a suite completa passou na repetição.

## Riscos residuais

A chave pública QA só será provisionada pela Task 8. O canal comercial
continua deliberadamente bloqueado enquanto a ABIPTOM não fornecer a chave
pública comercial. Não foi possível validar uma licença QA realmente assinada
nesta tarefa porque o emissor separado pertence à Task 8. Os contratos de
pedido, importação, erro, recarga e separação de canais ficaram cobertos por
testes automatizados.

Existe um bloqueador obrigatório atribuído à Task 8. O gate anti-contaminação
tem de provar que uma chave pública QA não é aceite como chave Commercial,
mesmo que seja copiada ou fornecida no caminho comercial. Esta verificação
não foi implementada na Task 7 porque depende do emissor QA e da chave pública
provisionada pela Task 8.

O verificador global de formatação continua a apontar problemas anteriores a
esta tarefa em ficheiros de inventário, compras, vendas e testes relacionados.
Não foram feitas alterações fora do âmbito para os corrigir.
