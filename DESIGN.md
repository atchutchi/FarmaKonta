# NôFarma Design System

## Direcção

O NôFarma é uma ferramenta operacional. O design deve desaparecer durante a tarefa e tornar estados, riscos e acções inequívocos. A estratégia de cor é contida. O azul profundo identifica a marca. O verde azulado informa estados positivos. O amarelo comunica atenção e o vermelho fica reservado a erro, expiração e destruição.

Cena de utilização: um caixa trabalha num balcão iluminado durante o dia, num computador Windows de resolução modesta, com clientes à espera e ligação à internet instável.

## Marca

O logótipo usa uma letra N formada por duas cápsulas estilizadas. Uma referência discreta ao código de barras aparece na base do símbolo. A assinatura é “by ABIPTOM”. O símbolo deve funcionar sem texto no ícone da aplicação e em impressão térmica monocromática.

Não usar cruz médica, escudo, gráfico, computador e cápsula no mesmo símbolo. Não usar gradientes, sombras largas ou detalhes finos que desapareçam em tamanhos pequenos.

## Cores

Todas as implementações usam tokens semânticos em OKLCH.

```css
:root {
  --color-bg: oklch(1 0 0);
  --color-surface: oklch(0.975 0.008 245);
  --color-surface-muted: oklch(0.94 0.018 245);
  --color-ink: oklch(0.22 0.035 245);
  --color-ink-muted: oklch(0.43 0.025 245);
  --color-primary: oklch(0.43 0.16 245);
  --color-primary-dark: oklch(0.27 0.08 245);
  --color-success: oklch(0.48 0.12 185);
  --color-success-soft: oklch(0.94 0.035 185);
  --color-warning: oklch(0.72 0.13 78);
  --color-warning-soft: oklch(0.96 0.04 78);
  --color-danger: oklch(0.50 0.18 28);
  --color-danger-soft: oklch(0.95 0.035 28);
  --color-border: oklch(0.88 0.015 245);
  --color-focus: oklch(0.62 0.15 245);
}
```

Texto sobre preenchimentos saturados usa branco. Texto escuro só aparece em fundos pálidos ou neutros. Cada combinação deve ser validada antes da entrega.

## Tipografia

A aplicação Windows usa Segoe UI Variable. O portal web usa Inter com fontes do sistema como fallback. Não misturar famílias dentro da mesma superfície.

O texto base tem pelo menos 16 px no portal e tamanho equivalente legível no Windows. Tabelas podem usar uma escala mais compacta sem descer abaixo de 12 px. Valores monetários, quantidades e datas usam algarismos tabulares.

## Espaçamento e geometria

Usar uma escala de 4 e 8 unidades. Os espaçamentos principais são 8, 12, 16, 24 e 32. A barra lateral mantém largura estável. O conteúdo adapta-se estruturalmente e não através de títulos fluidos.

Cartões usam raio máximo de 12 px. Campos e botões usam 6 a 8 px. Não combinar borda fina com sombra larga. Usar cartões apenas quando agrupam uma unidade real de informação.

## Navegação

A aplicação Windows usa barra lateral com rótulo e ícone da mesma família. O ponto de venda é um destino principal. A barra superior mostra farmácia, utilizador, turno, licença, internet e sincronização.

O ponto de venda mantém a pesquisa activa, resultados no centro e carrinho fixo à direita. F2 procura, F3 altera quantidade, F4 abre pagamento, F6 suspende, F7 retoma, F9 finaliza e Esc volta.

## Estados e interacção

Cada controlo inclui estado normal, foco, hover, activo, desactivado, carregamento e erro quando aplicável. O foco nunca é removido. As acções assíncronas desactivam submissões repetidas e apresentam progresso.

As transições duram entre 150 e 250 ms e comunicam mudança de estado. Não existem animações decorativas de carregamento da página. A redução de movimento elimina deslocações e mantém apenas transições instantâneas ou dissoluções curtas.

## Componentes críticos

Botão principal: uma acção dominante por ecrã, preenchimento azul e texto branco.

Alertas: ícone, título, explicação e acção. A cor nunca é o único indicador.

Tabelas: cabeçalho persistente quando necessário, ordenação acessível, números alinhados e alternativa de exportação.

Formulários: rótulos visíveis, ajuda persistente para campos complexos, validação ao sair do campo e erro junto ao campo.

Estado vazio: explica o que falta e oferece a próxima acção. Não apresentar apenas “sem dados”.

## Temas

O piloto usa tema claro porque as farmácias trabalham sobretudo em ambientes iluminados. O tema escuro não faz parte do piloto. Os tokens ficam preparados para uma extensão futura sem duplicar cores dentro dos componentes.
