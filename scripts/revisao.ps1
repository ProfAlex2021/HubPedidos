# Revisao automatizada do Hub de Pedidos (Roteiro 8).
# Mesma sequencia no computador do aluno e no GitHub Actions (.github/workflows/revisao.yml):
#   1. restore   2. build (analisadores)   3. dotnet format --verify-no-changes   4. testes
# Para na PRIMEIRA etapa que falhar, diz qual foi e devolve codigo diferente de zero.
# Resultados (TRX, cobertura Cobertura XML, relatorio do format) ficam em -Resultados.
#
# Uso (na raiz do repositorio):
#   powershell -ExecutionPolicy Bypass -File scripts\revisao.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\revisao.ps1 -Repeticoes 3
#   powershell -ExecutionPolicy Bypass -File scripts\revisao.ps1 -DemonstrarFalha analisador
#   powershell -ExecutionPolicy Bypass -File scripts\revisao.ps1 -DemonstrarFalha teste
#   powershell -ExecutionPolicy Bypass -File scripts\revisao.ps1 -VerificarBloqueios
# No Linux/macOS (e no CI):  pwsh scripts/revisao.ps1
# (Arquivo em ASCII: o PowerShell 5.1 le scripts sem BOM como ANSI.)

param(
    [ValidateSet('nenhuma', 'analisador', 'teste')]
    [string]$DemonstrarFalha = 'nenhuma',
    [ValidateRange(1, 20)]
    [int]$Repeticoes = 1,
    [string]$Configuracao = 'Release',
    [string]$Resultados = 'artefatos',
    [switch]$VerificarBloqueios
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# -VerificarBloqueios: prova que a revisao BLOQUEIA. Roda o proprio script com cada falha
# controlada, exige codigo diferente de zero na etapa esperada e, por fim, a revisao normal.
if ($VerificarBloqueios) {
    $casos = @(
        @{ Falha = 'analisador'; Etapa = 'build';  Diagnostico = 'CS8602' },
        @{ Falha = 'teste';      Etapa = 'testes'; Diagnostico = 'Falha_demonstrativa_da_revisao_automatizada' }
    )
    $script = Join-Path $PSScriptRoot 'revisao.ps1'
    # Mesmo executavel que esta rodando: powershell.exe (5.1) no Windows, pwsh no CI.
    $shell = (Get-Process -Id $PID).Path
    $ok = $true
    foreach ($caso in $casos) {
        $saidaDoCaso = Join-Path $raiz (Join-Path $Resultados ("bloqueio-" + $caso.Falha + ".log"))
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saidaDoCaso) | Out-Null
        Write-Host ""
        Write-Host ("### Verificando bloqueio: falha de " + $caso.Falha) -ForegroundColor Cyan
        # No 5.1, stderr redirecionado vira ErrorRecord; com Stop isso interromperia a verificacao.
        $ErrorActionPreference = 'Continue'
        $saida = & $shell -NoProfile -ExecutionPolicy Bypass -File $script -DemonstrarFalha $caso.Falha -Configuracao $Configuracao -Resultados $Resultados 2>&1 | Out-String
        $codigo = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        $saida | Set-Content -Path $saidaDoCaso -Encoding UTF8
        $etapaOk = $saida -match ("REVISAO REPROVADA na etapa '" + $caso.Etapa + "'")
        $diagnosticoOk = $saida -match $caso.Diagnostico
        if ($codigo -ne 0 -and $etapaOk -and $diagnosticoOk) {
            Write-Host ("OK: bloqueou na etapa '" + $caso.Etapa + "' (codigo " + $codigo + ", " + $caso.Diagnostico + " presente). Log: " + $saidaDoCaso) -ForegroundColor Green
        } else {
            Write-Host ("ERRO: a falha de " + $caso.Falha + " NAO bloqueou como esperado (codigo " + $codigo + "). Log: " + $saidaDoCaso) -ForegroundColor Red
            $ok = $false
        }
    }
    Write-Host ""
    Write-Host '### Revisao normal (sem falhas provocadas)' -ForegroundColor Cyan
    & $shell -NoProfile -ExecutionPolicy Bypass -File $script -Configuracao $Configuracao -Resultados $Resultados
    if ($LASTEXITCODE -ne 0) { $ok = $false }
    if ($ok) { Write-Host 'VERIFICACAO DE BLOQUEIOS: APROVADA' -ForegroundColor Green; exit 0 }
    Write-Host 'VERIFICACAO DE BLOQUEIOS: REPROVADA' -ForegroundColor Red
    exit 1
}

$propriedades = @()
if ($DemonstrarFalha -eq 'analisador') { $propriedades += '-p:DemonstrarFalhaDeAnalisador=true' }
if ($DemonstrarFalha -eq 'teste') { $propriedades += '-p:DemonstrarFalhaDeTeste=true' }

$pastaDeResultados = Join-Path $raiz $Resultados
New-Item -ItemType Directory -Force -Path $pastaDeResultados | Out-Null

$etapas = @('restore', 'build', 'format', 'testes')
$numero = 0

function Invocar-Etapa([string]$nome, [string]$causa, [scriptblock]$acao) {
    $script:numero++
    Write-Host ""
    Write-Host ("==> [" + $script:numero + "/" + $etapas.Count + "] " + $nome) -ForegroundColor Cyan
    $inicio = Get-Date
    & $acao
    $codigo = $LASTEXITCODE
    $segundos = [math]::Round(((Get-Date) - $inicio).TotalSeconds, 1)
    if ($codigo -ne 0) {
        Write-Host ""
        Write-Host ("REVISAO REPROVADA na etapa '" + $nome + "' (codigo " + $codigo + ", " + $segundos + " s).") -ForegroundColor Red
        Write-Host ("Causa provavel: " + $causa) -ForegroundColor Red
        Write-Host ("Resultados parciais em: " + $pastaDeResultados)
        exit $codigo
    }
    Write-Host ("    ok (" + $segundos + " s)") -ForegroundColor Green
}

Write-Host ("Revisao automatizada - configuracao " + $Configuracao + ", repeticoes de teste: " + $Repeticoes + ", falha provocada: " + $DemonstrarFalha)
& dotnet --version

Invocar-Etapa 'restore' 'pacote indisponivel ou com vulnerabilidade conhecida (NU1901-NU1904 sao erro).' {
    & dotnet tool restore
    if ($LASTEXITCODE -eq 0) { & dotnet restore HubPedidos.slnx }
}

Invocar-Etapa 'build' 'erro de compilacao ou regra elevada a erro pela politica (ex.: CS8602). Veja o identificador acima.' {
    & dotnet build HubPedidos.slnx --no-restore -c $Configuracao -nologo @propriedades
}

Invocar-Etapa 'format' 'codigo fora do .editorconfig. Corrija com: dotnet format HubPedidos.slnx' {
    & dotnet format HubPedidos.slnx --verify-no-changes --no-restore --report (Join-Path $pastaDeResultados 'format') -v q
}

Invocar-Etapa 'testes' 'teste reprovado (veja o nome e a mensagem acima e o TRX em resultados).' {
    for ($i = 1; $i -le $Repeticoes; $i++) {
        $pasta = Join-Path $pastaDeResultados ("testes-" + $i)
        Write-Host ("    execucao " + $i + " de " + $Repeticoes)
        & dotnet test --no-build -c $Configuracao --results-directory $pasta --report-trx --coverage --coverage-output-format cobertura --minimum-expected-tests 1
        if ($LASTEXITCODE -ne 0) { return }
    }
}

Write-Host ""
Write-Host ("REVISAO APROVADA. Resultados em: " + $pastaDeResultados) -ForegroundColor Green
exit 0
