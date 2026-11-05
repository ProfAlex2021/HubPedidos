# Configura o ambiente local de desenvolvimento (Windows PowerShell 5.1 ou PowerShell 7).
# Grava a connection string do SQLite em User Secrets, FORA do repositorio:
#   %APPDATA%\Microsoft\UserSecrets\hubpedidos-api-7d3c2a9e\secrets.json
# Uso (na raiz do repositorio):  powershell -ExecutionPolicy Bypass -File scripts\configurar-desenvolvimento.ps1
# (Arquivo mantido em ASCII: o PowerShell 5.1 le scripts sem BOM como ANSI.)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$api = Join-Path $raiz 'src\HubPedidos.Api'
$pastaDados = Join-Path $raiz 'dados'

New-Item -ItemType Directory -Force -Path $pastaDados | Out-Null
$banco = Join-Path $pastaDados 'hubpedidos.db'

dotnet user-secrets set 'ConnectionStrings:HubPedidos' "Data Source=$banco" --project $api | Out-Null
Write-Host "Connection string configurada em User Secrets (banco: $banco)."
Write-Host "Para gerar massa de dados: dotnet run --project src/HubPedidos.Api -- --semear 5000 --somente-preparar-banco"
