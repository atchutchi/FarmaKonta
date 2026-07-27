# NôFarma by ABIPTOM

Sistema de gestão, stock, caixa e facturação para farmácias da Guiné-Bissau.

O produto é uma aplicação Windows nativa, offline-first, acompanhada por serviços cloud para sincronização, licenciamento, backups, actualizações e consulta remota.

Estado actual: especificação de produto aprovada e fundação técnica em implementação.

Documentos principais:

- [Contexto do produto](PRODUCT.md)
- [Sistema visual](DESIGN.md)
- [Especificação funcional e técnica](docs/superpowers/specs/2026-07-27-nofarma-product-design.md)

## Desenvolvimento local

1. Copiar `deploy/.env.example` para `deploy/.env` e definir uma palavra-passe local forte.
2. Iniciar PostgreSQL com `docker compose -f deploy/compose.dev.yml --env-file deploy/.env up -d`.
3. Iniciar a API com `dotnet run --project src/Nofarma.Api --urls http://127.0.0.1:5080`.
4. Importar a colecção e o ambiente da pasta `postman/`.

Nunca guardar o ficheiro `deploy/.env` no Git.
