# Licenciamento assinado do NôFarma

O NôFarma separa três canais de compilação. `Unlicensed` não incorpora uma
chave de licenciamento e serve para desenvolvimento sem activação. `QA`
incorpora apenas a chave pública QA fixa e usa a pasta de dados
`%LOCALAPPDATA%\ABIPTOM\Nofarma-QA`. `Commercial` incorpora apenas a chave
pública comercial aprovada e usa os dados normais do NôFarma.

A chave pública QA está em
`build/keys/nofarma-qa-public.spki.b64`. A chave privada QA nunca pertence ao
repositório. No posto emissor autorizado, fica protegida por DPAPI
`CurrentUser` em
`%LOCALAPPDATA%\ABIPTOM\Nofarma-QA\issuer\qa-signing-key.bin`.

Nunca envies a chave privada, a palavra-passe de um utilizador ou um código de
recuperação. Um pedido `.nofarma-request` identifica a instalação e deve ser
tratado como informação interna. Uma licença `.nofarma-license` deve ser
entregue apenas à farmácia e ao computador a que pertence. Nenhum destes dois
ficheiros deve ser guardado no Git.

## Limite operacional do emissor QA

O emissor QA é uma ferramenta de consola para uma sessão interactiva Windows.
O mutex com namespace `Local` coordena apenas processos dentro da mesma sessão
interactiva. Tarefas agendadas, serviços Windows e qualquer execução fora dessa
sessão não são suportados. Esses modos podem ficar fora do mesmo mutex e não
devem ser usados para provisionar, rodar ou utilizar a chave QA.

Executa o emissor apenas no posto autorizado e com a conta Windows que possui
o blob DPAPI. Não copies o blob para outro utilizador ou computador. DPAPI
`CurrentUser` não transforma essa cópia numa migração válida.

## Provisionamento e rotação QA

O provisionamento inicial ou a reexportação idempotente da chave pública usa:

```powershell
dotnet run --project tools/Nofarma.Licensing.Qa/Nofarma.Licensing.Qa.csproj --configuration Release --no-launch-profile -- provision --public-output build/keys/nofarma-qa-public.spki.b64
```

Se a chave privada já existir, o comando mantém a mesma chave e só aceita uma
chave pública equivalente. Não substitui um output público diferente.

A rotação é uma operação excepcional. Exige `--rotate` e a confirmação exacta
`ROTATE-QA-KEY` no terminal. Uma rotação invalida a confiança anterior e deve
ser coordenada com uma nova publicação QA. Não rodes a chave apenas para
resolver um erro local.

## Pedido, emissão e importação QA

Na aplicação QA, abre Licença e exporta o pedido para uma pasta fora do
repositório. Transfere esse pedido para o posto emissor por um canal interno.
No posto emissor, define caminhos absolutos fora do repositório e executa:

```powershell
$requestPath = "C:\Nofarma-QA\exchange\incoming\request.nofarma-request"
$licensePath = "C:\Nofarma-QA\exchange\outgoing\license.nofarma-license"
dotnet run --project tools/Nofarma.Licensing.Qa/Nofarma.Licensing.Qa.csproj --configuration Release --no-launch-profile -- issue --request $requestPath --plan Monthly --valid-from "2026-08-01T00:00:00Z" --output $licensePath
```

As datas têm de indicar UTC com `Z` ou `+00:00`. `--valid-until` é opcional e
permite reduzir a validade. O plano mensal não pode ultrapassar 31 dias e o
anual não pode ultrapassar 366 dias. O emissor usa criação exclusiva e recusa
substituir uma licença já existente.

Entrega a licença ao mesmo computador que criou o pedido. Na página Licença,
selecciona Importar licença e escolhe o ficheiro `.nofarma-license`. Uma
licença QA não é válida num build Commercial e uma licença Commercial não é
válida num build QA.

## Gate de build e publicação

O script de release aceita apenas um output absoluto dedicado. A pasta deve
ser nova ou estar vazia. Uma pasta pré-existente nunca é apagada pelo script.
Se tiver conteúdo, o gate falha e preserva todos os ficheiros.

Para QA:

```powershell
$qaOutput = [IO.Path]::GetFullPath((Join-Path $PWD "artifacts\license-qa"))
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-license-channel.ps1 -Channel QA -Output $qaOutput
```

Para um novo output Unlicensed:

```powershell
$unlicensedOutput = [IO.Path]::GetFullPath((Join-Path $PWD "artifacts\license-unlicensed"))
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-license-channel.ps1 -Channel Unlicensed -Output $unlicensedOutput
```

O gate faz build e publish. Depois de cada operação lê o recurso público do
assembly `Nofarma.Desktop.dll`. Para QA e Commercial, importa a SPKI como ECDSA
P-256 e compara as coordenadas públicas com a chave fixa do canal. Também
rejeita igualdade com a chave do canal oposto. Para Unlicensed, exige a
ausência completa do recurso.

O publish é ainda pesquisado por nomes do emissor, extensões de chaves
privadas, pedidos, licenças emitidas e padrões textuais de segredos. O output
final não pode conter `Nofarma.Licensing.Qa`, `qa-signing-key.bin`, `.p8`,
`.p12`, `.pfx`, `.pem`, `.snk`, `.key`, `.nofarma-request` ou
`.nofarma-license`.

O scanner lê todos os ficheiros em streaming com um buffer único de 64 KiB.
Reconhece cabeçalhos PEM, campos de credenciais e nomes internos em UTF-8,
UTF-16LE, UTF-16BE, UTF-32LE e UTF-32BE, com ou sem BOM. A frase genérica
`PRIVATE KEY` é pesquisada em ASCII em todos os ficheiros e nas codificações
multibyte dentro de ficheiros textuais. A SPKI do canal oposto é rejeitada em
DER raw e em base64 nas mesmas codificações.

Imediatamente antes do publish, o script volta a verificar todos os componentes
existentes do caminho contra reparse points e confirma que o output continua
vazio. Uma alteração ocorrida durante o build fecha o gate e preserva o
conteúdo introduzido.

## Canal Commercial

A chave pública Commercial ainda tem de ser fornecida pelo processo autorizado
da ABIPTOM. Até lá, o canal falha fechado com `NFLC001`. Quando existir, guarda
apenas a SPKI pública em
`build/keys/nofarma-commercial-public.spki.b64`. A chave privada Commercial
nunca deve entrar neste repositório nem no posto de desenvolvimento QA.

O gate rejeita uma chave Commercial criptograficamente igual à QA com
`NFLC002`. Também rejeita chaves ausentes, inválidas, fora de ECDSA P-256 ou
recursos incorporados que não correspondam à SPKI fixa. O build usa uma cópia
validada com nome GUID em `obj` e elimina apenas essa cópia depois da
incorporação. Builds concorrentes usam nomes diferentes e não apagam o
snapshot de outra execução.

Quando a chave pública Commercial estiver provisionada, usa um output novo:

```powershell
$commercialOutput = [IO.Path]::GetFullPath((Join-Path $PWD "artifacts\license-commercial"))
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-license-channel.ps1 -Channel Commercial -Output $commercialOutput
```

Não distribuas um publish que não tenha terminado este gate com código zero.
