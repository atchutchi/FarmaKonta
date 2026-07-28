# NôFarma design QA

Fonte visual: `docs/design/previews/02-dashboard-sales-invoices-cash.png`, painel inferior direito “4. CAIXA - ABRIR TURNO E FECHAR TURNO”.

Implementação: `src/Nofarma.Desktop/Views/CashPage.xaml`.

Captura da implementação: não disponível. As duas instâncias abertas da aplicação permanecem no ecrã `NôFarma | Acesso local`. A autenticação não foi automatizada e não foram criadas credenciais, turnos ou valores operacionais artificiais.

Viewport pretendido: 1366 por 768, tema claro, escala do sistema não confirmada.

Dimensões da fonte: 1536 por 1024 píxeis. O painel Caixa ocupa o quadrante inferior direito da composição. Não existe captura normalizada da implementação para comparação de densidade.

Estado pretendido: sem turno, turno aberto, validação de movimentos e pré-visualização do fecho.

## Evidência de comparação

A fonte visual foi aberta e inspeccionada. A implementação não pôde ser capturada depois da autenticação. Por isso, não existe comparação visual válida do ecrã completo nem das regiões focadas.

## Superfícies de fidelidade

Tipografia: o código usa os recursos existentes de Segoe UI Variable. A fidelidade renderizada ainda não foi confirmada.

Espaçamento e composição: o XAML segue a divisão do mockup entre resumo e movimentos à esquerda e fecho à direita. A ausência de cortes a 1366 por 768 ainda não foi confirmada.

Cores e tokens: o azul e os estados semânticos usam os recursos do projecto. O texto verde de sucesso tem contraste calculado de 4,96:1 sobre o fundo suave. A aparência renderizada ainda não foi comparada.

Imagens e recursos: o ecrã Caixa não contém imagens específicas além dos recursos da aplicação existentes.

Texto e conteúdo: rótulos, XOF, F2, F4, entradas, saídas, esperado, contado e diferença estão implementados. Os valores vêm do estado local e não de dados de demonstração.

## Interacções e acessibilidade

Os testes e a leitura do código confirmam validação, bloqueio de dupla submissão, atalhos F2 e F4, nomes de automação e mensagens textuais para a diferença. O percurso real por teclado, o foco visível e os estados renderizados não foram inspeccionados.

## Pendência

Iniciar sessão manualmente numa janela NôFarma já aberta. Depois abrir Caixa e capturar os estados reais disponíveis a 1366 por 768. A fonte e a captura devem ser colocadas na mesma comparação. Qualquer diferença P0, P1 ou P2 deve ser corrigida e comparada novamente.

final result: blocked

## Histórico de QA do inventário

### Âmbito

Comparação do quadro aprovado em `docs/design/previews/03-products-stock-purchases-suppliers.png` com a implementação nativa Windows de Produtos, Stock, Compras, Fornecedores e Importação de Inventário Inicial.

### Evidência inspeccionada em 2026-07-28

A aplicação nativa foi inspeccionada ao vivo com Windows Graphics Capture a 1011 por 634, 1536 por 816 e no tamanho físico aprovado de 1366 por 768. Com a escala do Windows a 125 por cento, a janela de 1366 por 768 corresponde aproximadamente a 1080 por 608 píxeis lógicos. As capturas foram apresentadas durante a sessão activa de QA mas não foram guardadas no repositório. Foram inspeccionados estes estados:

1. Estado vazio de Produtos e formulário de novo produto.
2. Estado vazio de Fornecedores.
3. Estado vazio de Stock.
4. Estado vazio de Compras e formulário de recepção desactivado.
5. Selecção do ficheiro de importação, mapeamento automático das colunas e resultado da validação.

A aplicação foi recompilada depois da correcção do cabeçalho responsivo. Um administrador concluiu o início de sessão manualmente. O shell corrigido e as cinco páginas de inventário foram revistos no tamanho físico aprovado sem automatizar credenciais.

### Resultado da comparação

Produtos: bom nos tamanhos inspeccionados, incluindo 1366 por 768. O título, a acção principal, a pesquisa persistente, o estado vazio e o editor em linha seguem a estrutura aprovada. O formulário mantém deslocamento vertical e não insere medicamentos ou totais fictícios.

Fornecedores: bom no estado vazio. A acção principal, a pesquisa e a explicação do estado vazio são claras. Os painéis detalhados de dívida e compras dependem de dados reais.

Stock: bom no estado vazio. A página mantém o título, a pesquisa e a estrutura tabular aprovada. Os alertas reais aparecem no shell. As linhas de lote, validade e FEFO dependem de dados reais.

Compras: bom a 1536 por 816 e utilizável com deslocamento a 1011 por 634 e a 1366 por 768 com escala de 125 por cento. Nos tamanhos lógicos menores, as duas colunas comprimem a tabela e exigem deslocamento no painel de recepção. Todos os campos continuam acessíveis.

Importação inicial: bom até à validação. O indicador de quatro passos, a indicação de ficheiro local, o mapeamento automático e o fluxo persistente são claros. O modelo CSV oficial vazio é processado pelo leitor de produção e um teste confirma os cabeçalhos obrigatórios sem linhas de dados.

### Acessibilidade observada

A navegação, os campos e os botões foram expostos pela automação do Windows com nomes úteis. A acção de recepção permaneceu desactivada até existirem valores obrigatórios. Os controlos usam rótulos visíveis e alvos grandes. A navegação por teclado foi exercitada e o foco permaneceu visível. Leitor de ecrã completo, rácios de contraste e zoom de 200 por cento não foram medidos.

### Correcções realizadas durante o QA

Foi criado um estado compacto do shell abaixo de 1450 píxeis. O botão de terminar sessão recebeu uma coluna dedicada. Os nomes dinâmicos ganharam limites e reticências. Os estados secundários ficam ocultos em modo compacto. Foi removida a alteração de visibilidade em código que anulava os estados responsivos. Foi acrescentado o teste do CSV oficial vazio. A janela inicial passou a usar 1366 por 768 e recebeu protecção por teste.

### Limitação do inventário

Os estados vazios e iniciais foram comparados directamente e o defeito responsivo encontrado foi corrigido. A comparação pixel a pixel dos estados preenchidos continua dependente de registos representativos fornecidos ou aprovados como dados descartáveis de QA.
