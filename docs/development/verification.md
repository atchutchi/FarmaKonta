# Verificação da fundação NôFarma

Executar a partir da raiz do repositório:

```powershell
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror
dotnet test Nofarma.slnx --configuration Release --no-build
git diff --check
git status --short
```

Para validar a configuração Docker sem revelar credenciais:

```powershell
$env:NOFARMA_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString("N")
docker compose -f deploy/compose.dev.yml config --quiet
Remove-Item Env:NOFARMA_POSTGRES_PASSWORD
```

O gate passa quando todos os comandos terminam com código zero, o build não contém avisos, todos os testes passam e `git status --short` não apresenta ficheiros gerados.
