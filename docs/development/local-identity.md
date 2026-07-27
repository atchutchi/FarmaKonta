# Identidade local e operação offline

## Objectivo

Esta fase permite preparar e usar a identidade do NôFarma sem ligação à cloud. O assistente cria uma farmácia, um dispositivo local e o administrador principal. A activação da licença pode ocorrer mais tarde.

O piloto mantém uma farmácia, um estabelecimento e um computador operacional. A base SQLite é a fonte local. A aplicação foi desenhada para continuar disponível durante períodos prolongados sem internet. A futura sincronização e a regra comercial dos 90 dias não alteram as credenciais locais desta fase.

## Caminhos locais

Os dados ficam no perfil do utilizador Windows:

```text
%LOCALAPPDATA%\ABIPTOM\Nofarma\data\nofarma.db
%LOCALAPPDATA%\ABIPTOM\Nofarma\secrets\credential-pepper.bin
%LOCALAPPDATA%\ABIPTOM\Nofarma\config\fiscal-settings.json
```

A base nunca contém palavras-passe, PIN ou códigos de recuperação legíveis. O pepper fica fora da base e é protegido pelo DPAPI para o utilizador Windows actual. O ficheiro fiscal guarda apenas URLs HTTPS configuradas pela farmácia. Não deve guardar tokens, chaves ou certificados.

## Configuração inicial

O assistente recolhe os dados da farmácia e cria o administrador. A palavra-passe deve ter pelo menos 12 caracteres e incluir maiúscula, minúscula, número e símbolo.

No fim, a aplicação mostra um código de recuperação uma única vez. O administrador deve copiá-lo para papel ou para outro local seguro fora do computador. Apenas o hash fica guardado.

## Acesso e bloqueio

Os perfis que não são Caixa usam palavra-passe. O Caixa usa um PIN com quatro a seis algarismos. Depois de cinco credenciais erradas, a conta fica bloqueada durante 15 minutos. A mensagem pública não revela se o utilizador ou a credencial estavam errados.

Uma sessão administrativa expira após 15 minutos de inactividade. As permissões são definidas por função e negadas por omissão. O Caixa não consegue gerir utilizadores, permissões ou configuração fiscal.

## Recuperação offline

O código de recuperação altera a palavra-passe do administrador sem depender da ABIPTOM ou da cloud. Uma recuperação válida:

1. invalida as sessões administrativas activas
2. substitui a palavra-passe
3. marca o código antigo como usado
4. gera um novo código de utilização única
5. grava a operação na auditoria local

O novo código deve ser guardado antes de sair do ecrã. A ABIPTOM não possui uma porta de acesso permanente à instalação.

## Utilizadores

Um administrador autorizado pode criar perfis locais. A interface exige PIN para Caixa e palavra-passe forte para as restantes funções. Uma mudança de função exige uma nova credencial compatível. A desactivação e a mudança de função revogam as sessões do utilizador.

O administrador principal não pode ser desactivado nem perder a função de Administrador.

## DGCI

As URLs de testes e produção começam vazias. A farmácia é responsável por obter autorização e endereços oficiais junto da DGCI. O NôFarma não inventa endpoints e não assume a entrega de documentação fiscal em nome da farmácia.

## Limpeza exclusiva de dados de desenvolvimento

Fechar primeiro a aplicação. Confirmar que o caminho resolvido termina exactamente em `ABIPTOM\Nofarma` dentro de `%LOCALAPPDATA%`. Mover essa pasta para uma cópia de segurança controlada em vez de a apagar directamente. Nunca remover `%LOCALAPPDATA%`, a pasta `ABIPTOM` inteira ou o perfil do utilizador.

Ao remover a base sem o pepper correspondente, as credenciais deixam de ser recuperáveis. Uma limpeza cria uma instalação nova e exige outro administrador e outro código de recuperação.
