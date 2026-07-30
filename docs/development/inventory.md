# Inventário, compras e importação inicial

O NôFarma guarda o inventário operacional no computador da farmácia. Produtos, fornecedores, lotes, movimentos, compras, recepções e rascunhos de importação continuam disponíveis sem internet. A futura cloud deve sincronizar estes registos quando existir ligação. Não deve ser necessária para trabalhar no balcão.

## Produtos e embalagens

Cada produto tem um código interno único, nome comercial, categoria, tipo, unidade base, preço de venda e limite mínimo de stock. Um medicamento exige controlo por lote e validade. Uma embalagem define quantas unidades base entram numa caixa, frasco ou outra apresentação comercial. Os códigos de barras pertencem às embalagens e não substituem o código interno.

O preço de compra é informação sensível. A aplicação só o mostra a perfis com a permissão adequada. A página Produtos não apresenta exemplos ou quantidades fictícias quando a base está vazia.

## Stock por lote

O saldo resulta do livro de movimentos local. Não é editado directamente. Entradas, saídas, ajustes e compensações criam movimentos auditáveis. O sistema impede saldos negativos e usa FEFO para dar prioridade aos lotes com validade mais próxima.

Os alertas usam dados reais. A barra superior mostra produtos com stock baixo, produtos esgotados e lotes que exigem atenção por validade. Um produto sem qualquer lote aparece como esgotado. Estes alertas permanecem dentro da aplicação, conforme a decisão do produto.

## Fornecedores, compras e recepções

O fornecedor é registado localmente com nome e contactos opcionais. Uma compra contém linhas de produto e embalagem. A recepção pode ser parcial. Cada linha recebida exige documento, lote, validade, quantidade e custo unitário válidos antes de activar a confirmação.

A confirmação da recepção altera o stock através do serviço de inventário. O duplo clique é bloqueado e a mesma operação usa uma chave idempotente para evitar movimentos repetidos.

## Importação inicial

O assistente tem quatro passos:

1. Seleccionar um ficheiro Excel `.xlsx` ou CSV `.csv` com até 20 MiB.
2. Escolher a folha, quando aplicável, e associar as colunas obrigatórias.
3. Validar as linhas e corrigir os campos bloqueados dentro da aplicação.
4. Rever e confirmar explicitamente a aplicação do inventário ao stock local.

As colunas obrigatórias são Nome comercial, Unidade base e Quantidade inicial. O modelo inclui também código interno, código de barras, tipo, embalagem, factor, preços, stock mínimo, lote, validade e fornecedor. Medicamentos exigem lote e validade. Quantidades e factores devem ser inteiros positivos. Preços e stock mínimo não podem ser negativos.

O botão Voltar preserva o ficheiro, a folha, os mapeamentos e o rascunho. Cada correcção de linha é guardada na base local. Se a aplicação fechar depois da criação do rascunho, os dados preparados não são transformados em stock sem uma confirmação posterior.

## Licença e trabalho offline

A preparação e validação funcionam sem cloud. A confirmação do inventário e outras entradas de stock passam pela política de licença. Sem licença activa, a aplicação mostra uma mensagem simples e mantém o rascunho guardado. Não existe uma porta permanente de suporte para contornar este bloqueio.

A política normal de produção nunca activa stock por conveniência. Qualquer política de teste deve existir apenas numa compilação de desenvolvimento preparada para QA e não pode ser incluída numa compilação Release.

## Segurança dos ficheiros

Ficheiros reais de inventário podem conter informação comercial. Não devem entrar no Git. A raiz ignora Excel e CSV por defeito e permite apenas o modelo sem dados em `docs/development/inventory-import-template.csv`. Segredos, chaves, palavras-passe, PIN, bases SQLite, cópias de segurança e exportações também permanecem fora do repositório.
