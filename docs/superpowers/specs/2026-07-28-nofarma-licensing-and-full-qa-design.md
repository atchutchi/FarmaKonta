# NôFarma: licenciamento assinado e QA integral

Data: 28 de Julho de 2026

Estado: aprovado quanto à arquitectura. A implementação começa apenas depois da revisão deste documento pelo responsável do produto.

## 1. Objectivo

Implementar a base de licenciamento offline do NôFarma sem criar uma porta de activação escondida. A solução deve permitir activar e renovar uma farmácia por ficheiro assinado, continuar a validar a licença sem internet e entrar em modo de consulta quando o direito de utilização terminar.

O mesmo trabalho deve permitir executar QA integral sobre todas as páginas actualmente implementadas nos estados sem licença, com licença QA válida, em tolerância e expirada. Páginas ainda planeadas ou apresentadas apenas como módulo vazio devem ser identificadas como incompletas e não podem ser declaradas funcionais.

## 2. Decisões confirmadas

- A licença comercial é assinada pela ABIPTOM.
- A chave privada comercial existe apenas na infraestrutura protegida da ABIPTOM.
- O repositório, a aplicação Desktop e os instaladores nunca contêm a chave privada comercial.
- A aplicação valida assinaturas através de chaves públicas confiáveis incorporadas no canal de compilação.
- A licença fica associada a uma farmácia, um estabelecimento e um dispositivo.
- Existem planos mensal e anual.
- Uma licença terminada concede sete dias de tolerância.
- Os 90 dias de funcionamento sem internet não prolongam uma subscrição não renovada.
- Depois da licença e da tolerância, a aplicação mantém consulta, exportação e reimpressão, mas bloqueia novas operações.
- Os dados da farmácia nunca são apagados, ocultados ou retidos por falta de renovação.
- A activação e a renovação funcionam por ficheiro offline. A futura activação online usa o mesmo modelo e não altera a semântica local.
- O QA usa uma autoridade de assinatura separada. Uma compilação comercial nunca aceita uma licença QA.
- Não existe variável de ambiente, código secreto, parâmetro de interface ou alteração simples de SQLite que active uma compilação comercial.

## 3. Limites desta fase

Esta fase implementa o motor local, a importação offline, a superfície Desktop de activação, a ferramenta local de emissão QA e os testes. Não implementa o portal ABIPTOM, pagamentos, envio por email, API cloud, PostgreSQL, sincronização nem emissão comercial de licenças.

A ferramenta QA não é distribuída com o produto. Serve apenas para validar o comportamento local até existir o serviço de licenciamento na cloud.

## 4. Arquitectura

### 4.1 Autoridade de assinatura

As licenças usam ECDSA P-256 com SHA-256. O conteúdo assinado usa uma serialização canónica e versionada. A assinatura cobre todos os campos da licença.

Cada chave pública tem um `keyId`. A aplicação mantém um conjunto pequeno de chaves públicas aceites para permitir rotação. Uma chave retirada pode continuar a validar licenças emitidas antes da sua retirada quando a política assinada o permitir.

O canal comercial aceita apenas chaves públicas comerciais. O canal QA aceita apenas chaves públicas QA e apresenta permanentemente `Modo QA`. Os dois canais usam identificadores e recursos de compilação distintos. Um ficheiro QA apresentado ao canal comercial é rejeitado.

A ABIPTOM deve fornecer a chave pública comercial através de um processo controlado antes da primeira release comercial. O gate de release comercial falha quando essa chave não está provisionada ou quando detecta qualquer chave pública QA.

### 4.2 Identidade do dispositivo

Na configuração inicial, o dispositivo gera um par de chaves local. A chave privada do dispositivo é protegida com DPAPI no contexto do Windows e nunca é exportada. A aplicação calcula a impressão digital da chave pública.

O pedido de activação contém apenas identificadores não secretos, versão do formato, farmácia, dispositivo e impressão digital. A licença assinada fica vinculada a esses valores. Copiar o ficheiro de licença e a base SQLite para outro computador não transfere a chave privada protegida e não activa o segundo computador.

Uma transferência legítima de dispositivo será tratada mais tarde pelo portal ABIPTOM através de uma nova licença. Não se copia a licença anterior.

### 4.3 Conteúdo da licença

O documento assinado contém:

- Versão do esquema.
- Identificador global da licença.
- Identificador da farmácia.
- Identificador do estabelecimento.
- Identificador do dispositivo.
- Impressão digital da chave do dispositivo.
- Plano `Monthly` ou `Annual`.
- Data de início em UTC.
- Data de fim em UTC.
- Fim da tolerância em UTC.
- Instante de emissão em UTC.
- Número de sequência de renovação.
- Identificador da chave de assinatura.
- Capacidades licenciadas reservadas para evolução versionada.
- Assinatura.

O ficheiro usa a extensão `.nofarma-license`. O importador impõe limites de tamanho e profundidade e rejeita campos obrigatórios ausentes, tipos inválidos, datas incoerentes, algoritmos desconhecidos e versões futuras não suportadas.

### 4.4 Persistência local

SQLite guarda o documento, o resultado da validação, o número de sequência e os instantes de importação e validação. A substituição da licença ocorre numa transacção e cria auditoria sem copiar o conteúdo completo nem a assinatura para o registo de auditoria.

O material privado do dispositivo e o marcador de tempo confiável ficam fora de SQLite, protegidos pelo Windows. A eliminação ou corrupção desse material não activa a aplicação. Produz um diagnóstico recuperável e exige reactivação.

### 4.5 Estados calculados

O estado da licença não é um booleano editável. É calculado em cada arranque e antes de operações protegidas:

1. `Missing`: não existe licença instalada.
2. `Invalid`: assinatura, formato, vínculo ou política inválidos.
3. `NotYetValid`: a data de início ainda não chegou.
4. `Valid`: período contratado activo.
5. `Grace`: período contratado terminou, mas decorrem os sete dias de tolerância.
6. `ExpiredReadOnly`: licença e tolerância terminaram.
7. `ClockRollback`: o relógio recuou de forma incompatível com o último marcador protegido.

`Valid` e `Grace` permitem novas operações. `Missing`, `Invalid`, `NotYetValid`, `ExpiredReadOnly` e `ClockRollback` bloqueiam novas operações, mas preservam as capacidades seguras definidas abaixo.

### 4.6 Política por capacidade

Sem uma licença operacional, permanecem permitidos:

- Início de sessão local.
- Configuração da farmácia, utilizadores, DGCI e impressoras.
- Preparação de catálogo, fornecedores, compras e importações sem confirmação.
- Consulta de dados existentes.
- Exportações autorizadas.
- Reimpressão de documentos já emitidos.
- Encerramento seguro de um turno já aberto.
- Instalação ou renovação de licença.
- Diagnóstico e consulta da auditoria conforme permissões.

Ficam bloqueados:

- Abertura de turno.
- Novos movimentos manuais de caixa.
- Confirmação de compras, stock e importação.
- Vendas, reembolsos e emissão de novos documentos fiscais quando esses módulos forem implementados.
- Qualquer nova operação que altere valores operacionais e esteja marcada como licenciada.

A autorização por função continua a ser aplicada antes da política de licença. Uma licença válida não concede permissões ao utilizador.

## 5. Activação offline

### 5.1 Pedido

A página de licença mostra o estado actual e permite guardar um pedido `.nofarma-request`. O pedido não contém palavra-passe, PIN, código de recuperação, chave privada, dados de vendas nem conteúdo da farmácia além dos identificadores necessários.

### 5.2 Emissão

O emissor recebe o pedido, confirma o plano e as datas e produz uma licença assinada. Nesta fase, o emissor QA é uma ferramenta separada. A sua chave privada é criada localmente e guardada fora do repositório com protecção do Windows.

A ferramenta recusa emitir licenças comerciais. Mostra claramente que o resultado é QA, exige confirmação explícita e regista apenas metadados técnicos locais.

### 5.3 Importação

A aplicação lê e valida o ficheiro antes de escrever. Uma importação inválida não altera a licença existente. Uma renovação válida deve ter número de sequência superior e não pode reduzir silenciosamente o período já concedido.

Depois da importação, a aplicação apresenta plano, validade, tolerância e dispositivo. Não mostra assinaturas nem material criptográfico como se fossem dados para o operador.

## 6. Tempo, tolerância e funcionamento offline

O motor usa UTC. Os limites são inclusivos e documentados para evitar diferenças no último segundo do período.

O último instante validado é guardado num marcador protegido. Um recuo pequeno devido a ajuste normal do sistema é tolerado. Um recuo superior ao limite técnico definido no plano de implementação coloca a licença em `ClockRollback` e bloqueia novas operações até existir um instante confiável ou uma renovação válida posterior.

Uma licença válida continua a funcionar sem internet até ao seu fim e tolerância. Os 90 dias referem-se à capacidade técnica de acumular trabalho e sincronizar mais tarde. Não acrescentam dias ao contrato.

## 7. Experiência Desktop

A barra superior substitui `Activação pendente` por um estado curto e honesto:

- `Sem licença`
- `Licença activa`
- `Tolerância: N dias`
- `Só consulta`
- `Licença inválida`
- `Verificar relógio`
- `Modo QA`, sempre visível no canal QA

A página de licença inclui estado, plano, datas, dispositivo, exportação do pedido, importação de licença e mensagens de recuperação. A acção principal depende do estado. Erros aparecem junto da operação e explicam o que fazer sem revelar detalhes criptográficos desnecessários.

Quando uma operação é bloqueada, a mensagem distingue falta de permissão de falta de licença. Um rascunho permanece guardado. A interface nunca aparenta que uma operação foi concluída quando foi bloqueada.

## 8. Segurança e ameaças

Devem ser cobertos:

- Alteração de qualquer campo depois da assinatura.
- Assinatura com chave desconhecida.
- Substituição da chave pública por configuração externa.
- Licença de outra farmácia, dispositivo ou canal.
- Cópia de base e licença para outro computador.
- Reutilização de licença antiga depois de uma renovação.
- Importação repetida do mesmo ficheiro.
- Ficheiro truncado, excessivo, malformado ou com campos duplicados.
- Datas invertidas, intervalos absurdos e sequência inválida.
- Recuo do relógio.
- Falha de energia durante importação.
- Corrupção ou perda do material protegido do dispositivo.
- Concorrência entre validação e importação.
- Tentativa de activar por edição de SQLite, variável de ambiente ou argumento de linha de comandos.
- Inclusão acidental de chaves privadas, licenças emitidas ou dados reais no Git.

Os diagnósticos não registam chaves, assinaturas completas, palavras-passe, PINs, códigos de recuperação ou dados operacionais.

## 9. Estratégia de testes

Não existe um conjunto literalmente infinito de “todos os testes possíveis”. A entrega usa uma matriz baseada em riscos, fronteiras, mutações realistas e percursos de utilização. Um comportamento crítico só é considerado coberto quando existe evidência de que o teste falha perante uma regressão concreta.

### 9.1 Unidade

- Serialização canónica determinística.
- Assinatura válida e alteração de cada campo.
- Estados temporais no instante anterior, exacto e posterior a cada limite.
- Plano mensal, anual e tolerância.
- Vínculo à farmácia, dispositivo, chave e canal.
- Sequência de renovação e prevenção de retrocesso.
- Política de capacidades para todos os estados.
- Mensagens do view-model sem segredos.

### 9.2 Integração

- Migração de uma instalação existente sem licença.
- Importação transaccional e auditoria.
- Repetição idempotente.
- Falha antes e depois da escrita, sem estado parcial.
- Licença errada não substitui licença válida.
- Renovação concorrente.
- Reinício da aplicação em cada estado.
- Perda e recuperação do marcador protegido.
- Política real aplicada a caixa, stock, compras e importação.

### 9.3 Segurança e robustez

- Fuzzing do leitor com entradas malformadas e limites de tamanho.
- Testes de adulteração do documento.
- Pesquisa de segredos no repositório e nos artefactos.
- Dependências vulneráveis.
- Permissões dos ficheiros locais.
- Verificação de que o binário comercial não contém a chave pública QA nem aceita licenças QA.

### 9.4 Desktop e acessibilidade

- Percurso por teclado e foco visível.
- Nomes de automação, leitura dos estados e mensagens de erro.
- Alvos com pelo menos 44 por 44 píxeis.
- Contraste, texto a 200 por cento e reflow.
- 1366 por 768 com escala de 100 e 125 por cento.
- Ausência de cortes, sobreposições e botões inacessíveis.

### 9.5 QA de todas as páginas

O percurso deve começar numa base controlada e descartável. O conjunto mínimo inclui:

1. Configuração inicial.
2. Início e fim de sessão.
3. Painel.
4. Vendas e Facturas, marcando honestamente módulos ainda não implementados.
5. Produtos.
6. Stock.
7. Compras.
8. Fornecedores.
9. Importação inicial.
10. Caixa sem turno.
11. Caixa com turno aberto e fecho, apenas num cenário licenciado controlado.
12. Relatórios.
13. Auditoria.
14. Utilizadores.
15. Configurações.
16. Licença.

Cada página é verificada com licença ausente e licença QA válida. As páginas que alteram operações são também verificadas em tolerância e `ExpiredReadOnly`. O relatório contém capturas actuais, estado geral por passo, defeitos encontrados, correcções, limites e funcionalidades ainda ausentes.

Não se criam vendas, facturas oficiais ou dados fiscais fictícios na base real do utilizador. Cenários destrutivos usam uma cópia controlada ou base temporária.

## 10. Critérios de aceitação

- Uma licença QA válida activa apenas a compilação QA e o dispositivo correcto.
- A mesma licença falha noutro dispositivo, farmácia, canal ou depois de adulterada.
- O canal comercial não aceita chaves ou licenças QA.
- Uma importação inválida preserva o estado anterior.
- O plano mensal, anual e os sete dias de tolerância respeitam os limites exactos.
- Depois da tolerância, novas operações ficam bloqueadas e os dados continuam consultáveis e exportáveis.
- As políticas reais de caixa, stock, compras e importação têm testes de integração.
- Não existe activação por edição de SQLite, configuração externa ou código secreto.
- A suite completa, build Release, formatação, arquitectura, análise de dependências e pesquisa de segredos passam.
- Todas as páginas existentes são percorridas e capturadas nos estados aplicáveis.
- Funcionalidades ausentes ficam explicitamente marcadas e não são simuladas como concluídas.

## 11. Entrega e separação de canais

O repositório recebe código, testes, chaves públicas não secretas e documentação. Não recebe chaves privadas, ficheiros de licença emitidos, pedidos reais de activação nem dados da farmácia.

Os artefactos QA usam identidade, faixa visual e pasta de saída próprias. O pacote comercial exclui a ferramenta emissora QA. O gate de release inspecciona o conteúdo publicado e falha se encontrar material QA no canal comercial.

## 12. Limitações conhecidas

Sem um relógio confiável de hardware ou contacto periódico com a cloud, nenhuma aplicação totalmente offline elimina todos os ataques de manipulação avançada do tempo. O marcador DPAPI, a auditoria e a renovação assinada aumentam a resistência sem impedir o funcionamento legítimo offline.

O licenciamento não substitui autorização, auditoria, backups nem segurança física do computador. Cada camada mantém responsabilidade própria.
