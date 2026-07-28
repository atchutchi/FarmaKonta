# NôFarma by ABIPTOM

Sistema de gestão, stock, caixa e facturação para farmácias da Guiné-Bissau.

O produto é uma aplicação Windows nativa, offline-first, acompanhada por serviços cloud para sincronização, licenciamento, backups, actualizações e consulta remota.

Estado actual: fundação técnica, identidade local e gestão inicial de inventário implementadas. A aplicação Windows permite configurar a farmácia, criar o administrador principal, guardar um código de recuperação offline, iniciar sessão, gerir utilizadores, produtos, fornecedores, stock por lote, compras e inventário inicial por Excel ou CSV.

Documentos principais:

- [Contexto do produto](PRODUCT.md)
- [Sistema visual](DESIGN.md)
- [Especificação funcional e técnica](docs/superpowers/specs/2026-07-27-nofarma-product-design.md)
- [Verificação da fundação](docs/development/verification.md)
- [Identidade local e operação offline](docs/development/local-identity.md)
- [Inventário, compras e importação inicial](docs/development/inventory.md)
- [Modelo CSV de inventário](docs/development/inventory-import-template.csv)
- [Atlas visual obrigatório](docs/design/previews/README.md)

## Desenvolvimento local

### Aplicação Windows

1. Executar `dotnet restore Nofarma.slnx --locked-mode`.
2. Compilar com `dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj --configuration Debug --no-restore -warnaserror`.
3. Iniciar `src/Nofarma.Desktop/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Nofarma.Desktop.exe`.
4. Numa instalação nova, concluir o assistente e guardar o código de recuperação fora do computador.

A aplicação Windows é executada sem MSIX durante o desenvolvimento. Isto evita depender do Modo de Programador do Windows. Os dados operacionais ficam no perfil local e não no repositório.

### Serviços cloud em desenvolvimento

1. Copiar `deploy/.env.example` para `deploy/.env` e definir uma palavra-passe local forte.
2. Iniciar PostgreSQL com `docker compose -f deploy/compose.dev.yml --env-file deploy/.env up -d`.
3. Iniciar a API com `dotnet run --project src/Nofarma.Api --urls http://127.0.0.1:5080`.
4. Importar a colecção e o ambiente da pasta `postman/`.

Nunca guardar o ficheiro `deploy/.env` no Git.
