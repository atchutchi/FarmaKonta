# NôFarma Local Identity Design

## 1. Objectivo

Esta fase entrega a configuração inicial da farmácia, identidade local, funções, permissões, sessões e auditoria. Todo o percurso funciona sem internet.

A fase está concluída quando um administrador configura uma instalação nova, cria um caixa e confirma que o caixa consegue iniciar sessão offline sem executar acções administrativas.

## 2. Âmbito

### 2.1 Incluído

- Diagnóstico inicial do computador e do armazenamento local.
- Criação da farmácia e do primeiro estabelecimento.
- Registo do primeiro computador autorizado.
- Criação do administrador inicial.
- Palavra-passe forte para administradores.
- PIN de quatro a seis dígitos apenas para caixas.
- Código de recuperação administrativo de utilização única.
- Funções e permissões predefinidas.
- Criação, desactivação e desbloqueio de utilizadores.
- Sessões locais e bloqueio por inactividade.
- Preparação dos dados DGCI sem emissão fiscal.
- Auditoria local imutável.
- Persistência SQLite e funcionamento offline.

### 2.2 Excluído

- Activação real de licença e validação na cloud.
- Sincronização de utilizadores entre dispositivos.
- Acesso remoto da ABIPTOM.
- Emissão de facturas fiscais.
- Vendas, turnos, stock, compras e pagamentos.
- Recuperação remota de contas.
- Vários computadores operacionais na mesma farmácia.

Estes elementos pertencem a fases posteriores do roteiro.

## 3. Arquitectura

A funcionalidade permanece dentro do monólito modular. O domínio define as regras de farmácia, dispositivo, utilizador, função, permissão, sessão, recuperação e auditoria. A camada de aplicação coordena casos de uso e transacções. A infraestrutura implementa SQLite, hashing e protecção de dados. A aplicação WinUI apresenta o assistente e o acesso local.

A interface nunca consulta nem modifica SQLite directamente. Todos os pedidos passam por casos de uso da camada de aplicação. As decisões de autorização são aplicadas antes de qualquer operação protegida.

SQLite é a fonte operacional desta fase. A configuração inicial e as alterações de identidade usam transacções reais. Uma falha não pode deixar uma farmácia, um administrador ou permissões parcialmente criados.

## 4. Estado da instalação

A instalação usa os seguintes estados:

1. `NotConfigured`: não existe configuração válida.
2. `Preparing`: existe configuração local e falta activação.
3. `ReadyForActivation`: a configuração obrigatória foi validada e pode receber uma licença.
4. `Active`: existe licença válida. Este estado será efectivamente concedido na fase de licenciamento.

O assistente conduz de `NotConfigured` para `Preparing` e depois para `ReadyForActivation`. Nesta fase, nenhuma operação local cria o estado `Active`.

Em `Preparing` e `ReadyForActivation`, o administrador pode configurar a farmácia, os utilizadores, a DGCI e as impressoras. Vendas, caixa, movimentos de stock e emissão fiscal ficam bloqueados até existir uma licença válida. Testes automatizados usam instalações controladas e não dependem de uma licença comercial.

## 5. Configuração inicial

O assistente usa uma barra lateral fixa com os passos e uma área de formulário. O tema é claro, a família tipográfica é Segoe UI Variable e a cor principal é o azul definido em `DESIGN.md`. O conteúdo continua utilizável em 1366 por 768.

Os cinco passos obrigatórios são:

1. Diagnóstico do Windows, armazenamento e capacidade de criar a base local.
2. Dados da farmácia, incluindo nome, NIF, endereço, contacto e fuso horário.
3. Primeiro administrador, incluindo nome, identificador de acesso e palavra-passe forte.
4. Segurança e recuperação, incluindo geração e confirmação de guarda do código único.
5. Revisão e criação atómica da instalação.

A activação aparece depois como uma tarefa pendente. Não bloqueia a preparação da aplicação.

O passo seguinte só fica disponível quando o passo actual é válido. Os rótulos permanecem visíveis. Os erros aparecem junto ao campo e explicam a correcção necessária. Existe uma única acção principal por ecrã. Voltar a um passo não apaga dados já introduzidos.

## 6. Modelo funcional

### 6.1 Farmácia e dispositivo

Uma instalação do piloto corresponde a uma farmácia, um estabelecimento e um computador operacional. O dispositivo recebe um identificador globalmente único gerado localmente. O nome técnico do dispositivo e os dados de diagnóstico não incluem credenciais nem informação pessoal desnecessária.

### 6.2 Utilizador

Cada utilizador tem identificador, farmácia, nome apresentado, identificador de acesso normalizado, estado, função atribuída, tipo de credencial, instante de criação e instante da última alteração.

O identificador de acesso é único dentro da farmácia. Um utilizador desactivado não inicia sessão. A desactivação não apaga o utilizador nem o seu histórico.

### 6.3 Funções e permissões

As funções predefinidas são:

- Administrador.
- Gestor.
- Farmacêutico.
- Responsável de stock.
- Caixa.
- Auditor.
- Suporte ABIPTOM.

O sistema aplica negação por omissão. Uma função recebe permissões explícitas e a autorização verifica a permissão exigida pelo caso de uso.

O Administrador configura a farmácia, cria utilizadores, atribui funções, desbloqueia caixas e prepara os dados DGCI. O Caixa inicia sessão, bloqueia a sessão e recebe apenas as futuras permissões de venda e caixa. Não consulta preços de compra, margens, utilizadores, permissões ou configuração fiscal. O Auditor consulta a auditoria sem modificar dados.

O Suporte ABIPTOM fica desactivado e sem acesso permanente. A autorização temporária e auditada será implementada com a cloud numa fase posterior.

## 7. Credenciais e recuperação

Administradores, gestores, farmacêuticos, responsáveis de stock e auditores usam palavra-passe forte. Caixas usam PIN de quatro a seis dígitos. O tipo de credencial deriva da função e não pode ser escolhido pelo utilizador. O primeiro Administrador criado pelo assistente fica identificado como administrador principal da instalação.

Palavra-passe, PIN e código de recuperação nunca são guardados em texto legível. A infraestrutura guarda um hash com salt, versão do algoritmo e parâmetros necessários para actualização futura. O valor introduzido é descartado depois da verificação.

O código de recuperação é gerado durante a configuração, mostrado uma única vez e guardado fora do computador pelo administrador. A aplicação guarda apenas o material necessário para o validar. Uma recuperação válida:

1. Invalida o código usado.
2. Invalida as sessões administrativas existentes.
3. Obriga à criação de uma nova palavra-passe.
4. Gera um novo código de recuperação.
5. Cria um registo de auditoria.

Não existe uma credencial permanente da ABIPTOM nem uma porta de acesso universal.

## 8. Início de sessão e bloqueio

O ecrã de acesso permite escolher uma conta local. Todos os utilizadores excepto o Caixa introduzem a palavra-passe. O Caixa introduz o PIN. A mensagem de falha não revela se o identificador, a palavra-passe ou o PIN estava errado.

Depois de cinco tentativas inválidas, a conta fica bloqueada durante 15 minutos. Um Administrador autenticado pode desbloquear um Caixa. O administrador principal recupera o acesso através do código de recuperação.

A sessão administrativa bloqueia depois de 15 minutos sem actividade. A sessão do Caixa permanece activa durante o turno, mas pode ser bloqueada manualmente ou por troca de utilizador. Enquanto ainda não existir o módulo de turnos, a sessão do Caixa termina por encerramento explícito ou reinício da aplicação.

Uma acção administrativa rejeita sessões expiradas ou bloqueadas e exige nova autenticação.

## 9. Auditoria

Cada evento de auditoria inclui:

- Identificador globalmente único.
- Farmácia e dispositivo.
- Utilizador responsável quando existe.
- Tipo de acção.
- Tipo e identificador do objecto afectado.
- Instante UTC.
- Resultado da operação.
- Código de diagnóstico quando ocorre uma falha auditável.
- Dados mínimos da alteração, sem segredos.

São auditadas tentativas de acesso falhadas, bloqueios, desbloqueios, recuperações, criação e desactivação de utilizadores, alterações de funções, alterações de configuração e conclusão do assistente.

Credenciais, hashes, PIN, códigos de recuperação, tokens e dados pessoais desnecessários não entram nos detalhes da auditoria. Os eventos são acrescentados e não editados ou apagados pela aplicação normal.

## 10. Operações transaccionais

As seguintes operações são atómicas:

- Configuração inicial da farmácia, dispositivo, funções e administrador.
- Criação ou desactivação de utilizador.
- Alteração de função.
- Recuperação da conta administrativa.
- Desbloqueio de Caixa.

Uma falha reverte todos os efeitos da operação. O registo de falha, quando necessário, é produzido de forma controlada sem expor dados sensíveis.

## 11. Tratamento de erros

As mensagens apresentam causa compreensível, impacto e acção de recuperação. Detalhes internos ficam fora da interface. Cada falha inesperada recebe um código de diagnóstico que pode ser comunicado ao suporte.

Se SQLite não puder ser criado ou aberto, o assistente não avança e explica como libertar espaço ou corrigir permissões. Se uma transacção falhar, o utilizador pode repetir a operação depois de resolver a causa. Repetir a conclusão do assistente não duplica a farmácia nem o administrador.

## 12. Acessibilidade e interacção

- Navegação completa por teclado.
- Ordem de foco equivalente à ordem visual.
- Foco sempre visível.
- Contraste mínimo de 4,5:1 para texto normal.
- Estado comunicado por texto e símbolo além da cor.
- Alvos interactivos grandes e consistentes.
- Rótulos persistentes nos campos.
- Validação junto ao campo.
- Transições de estado entre 150 e 250 milissegundos.
- Redução de movimento respeitada.
- Nenhuma animação decorativa ou bloqueadora.

## 13. Estratégia de testes

### 13.1 Unidade

- Normalização e unicidade do identificador de acesso.
- Regras de palavra-passe e PIN.
- Aplicação de permissões com negação por omissão.
- Contagem de tentativas e bloqueio temporário.
- Expiração de sessão administrativa.
- Utilização única do código de recuperação.
- Transições válidas do estado da instalação.

### 13.2 Integração com SQLite real

- Criação e migração da base local.
- Configuração inicial transaccional.
- Rejeição de uma segunda configuração.
- Persistência depois de reiniciar os serviços da aplicação.
- Criação, desactivação e desbloqueio de utilizadores.
- Recuperação administrativa com invalidação de sessões.
- Auditoria imutável das acções sensíveis.
- Ausência de credenciais legíveis nas tabelas persistidas.

### 13.3 Aplicação e autorização

- Um Administrador configura a farmácia offline.
- Um Administrador cria um Caixa.
- Um Caixa inicia sessão offline com PIN.
- Um Caixa não executa casos de uso administrativos.
- Cinco tentativas inválidas provocam bloqueio durante 15 minutos.
- Um código de recuperação só funciona uma vez.
- Uma falha durante a configuração não deixa dados parciais.

## 14. Critério de conclusão

A fase termina apenas quando:

1. O percurso completo funciona sem internet.
2. O Administrador configura a farmácia e cria um Caixa.
3. O Caixa inicia sessão e não consegue executar acções administrativas.
4. As credenciais nunca são persistidas em texto legível.
5. A recuperação funciona uma única vez e roda o código.
6. As operações sensíveis produzem auditoria.
7. Os testes unitários e de integração passam.
8. O build não produz erros nem avisos.
9. A pesquisa de segredos não encontra valores reais.
10. A documentação de verificação está actualizada.
