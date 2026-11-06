# Cria usuarios de teste com SENHAS ALEATORIAS e grava em User Secrets (fora do repositorio):
#   %APPDATA%\Microsoft\UserSecrets\hubpedidos-api-7d3c2a9e\secrets.json
# Para ver as senhas depois:  dotnet user-secrets list --project src/HubPedidos.Api
# Uso (na raiz do repositorio):  powershell -ExecutionPolicy Bypass -File scripts\criar-usuarios-teste.ps1
# (Arquivo em ASCII: o PowerShell 5.1 le scripts sem BOM como ANSI.)

$ErrorActionPreference = 'Stop'
$api = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\HubPedidos.Api'

function Nova-Senha {
    # 18 bytes do gerador criptografico -> 24 caracteres Base64 (sem '+', '/' e '=').
    $bytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return ([Convert]::ToBase64String($bytes)).Replace('+', 'A').Replace('/', 'B').Replace('=', '')
}

$usuarios = @(
    @{ Nome = 'ana';      Papel = 'cliente';  ClienteId = 'cliente-001' },
    @{ Nome = 'bruno';    Papel = 'cliente';  ClienteId = 'cliente-002' },
    @{ Nome = 'operador'; Papel = 'operador'; ClienteId = '' }
)

for ($i = 0; $i -lt $usuarios.Count; $i++) {
    $u = $usuarios[$i]
    dotnet user-secrets set "Autenticacao:Usuarios:${i}:Nome" $u.Nome --project $api | Out-Null
    dotnet user-secrets set "Autenticacao:Usuarios:${i}:Senha" (Nova-Senha) --project $api | Out-Null
    dotnet user-secrets set "Autenticacao:Usuarios:${i}:Papel" $u.Papel --project $api | Out-Null
    if ($u.ClienteId) {
        dotnet user-secrets set "Autenticacao:Usuarios:${i}:ClienteId" $u.ClienteId --project $api | Out-Null
    }
}

Write-Host "Usuarios ana (cliente-001), bruno (cliente-002) e operador criados com senhas aleatorias em User Secrets."
Write-Host "Consulte as senhas com: dotnet user-secrets list --project src/HubPedidos.Api"
