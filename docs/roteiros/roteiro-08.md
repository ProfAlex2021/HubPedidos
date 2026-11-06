# Roteiro 8 — WebApplicationFactory, bancos efêmeros, analisadores e revisão automatizada

**Objetivo:** criar uma rede de segurança executável para contratos, persistência,
concorrência e estilo, integrada ao fluxo de revisão.

## Como executar

Revisão completa, igual à do CI (restore → build com analisadores → `dotnet format
--verify-no-changes` → testes com TRX e cobertura):

```bash
powershell -ExecutionPolicy Bypass -File scripts/revisao.ps1
```

Repetir a suíte (detecta dependência de ordem e instabilidade):

```bash
powershell -ExecutionPolicy Bypass -File scripts/revisao.ps1 -Repeticoes 3
```

Provar que a revisão bloqueia (falha de analisador, falha de teste e, por fim, revisão normal):

```bash
powershell -ExecutionPolicy Bypass -File scripts/revisao.ps1 -VerificarBloqueios
```

Só os testes, sem o script:

```bash
dotnet test
```

No Linux/macOS e no GitHub Actions o mesmo script roda com `pwsh scripts/revisao.ps1`.
Os resultados ficam em `artefatos/` (ignorada pelo Git): `testes-N/*.trx`,
`testes-N/*.cobertura.xml`, `format/format-report.json` e, na verificação de bloqueios,
`bloqueio-*.log`.

## Arquivos produzidos ou alterados

| Arquivo | Papel |
|---|---|
| `tests/HubPedidos.Integracao.Testes/` | Projeto de integração (novo), no padrão `Pedidos.Validacao.Testes` da Unidade IV |
| `.../Infraestrutura/FabricaDaAplicacao.cs` | `WebApplicationFactory<Program>` customizada: ambiente, banco em memória, substituições, clientes HTTP |
| `.../Infraestrutura/ProcessamentoObservavel.cs` | Consumidor falso e observável da fila (sinais, sem atrasos) |
| `.../Infraestrutura/Snapshot.cs`, `Contratos/Snapshots/*.json` | Teste de contrato por snapshot com aprovação explícita (extensão) |
| `.../Infraestrutura/FabricaTestes.cs` | Testes da própria fixture (estado inicial, isolamento, vida útil, proteção do banco) |
| `.../Pedidos/CriacaoEConsultaTestes.cs` | Criação e consulta com persistência real do ambiente de teste |
| `.../Processamento/FilaHttpTestes.cs` | Produtor da fila pela API: 202, 429 + Retry-After, 404 |
| `.../Contratos/`, `Cache/`, `TempoReal/` | Testes HTTP dos Roteiros 6 e 7, movidos de `HubPedidos.Tests` com `git mv` |
| `.../Demonstracoes/` | Exemplo da Unidade IV (`ExemploComProblemas` / `ExemploCorrigido`) e o teste de falha controlada |
| `Directory.Build.props` | `EnableNETAnalyzers`, `AnalysisLevel`, `EnforceCodeStyleInBuild`, elevação seletiva de CS8602 |
| `.editorconfig` | Política de severidades, com escopo por pasta/arquivo |
| `scripts/revisao.ps1` | Revisão automatizada reproduzível localmente |
| `.github/workflows/revisao.yml` | Mesma revisão no GitHub Actions + publicação condicionada |
| `ProcessadorDePedidosWorker.cs`, `NotificadorPorLog.cs`, `NotificadorDaApi.cs`, `RegistrosDeInicializacao.cs` | Logs com `[LoggerMessage]` (CA1848/CA1873) |
| `ContextoDePedidos.cs` | Nome de parâmetro igual ao da classe base (CA1725) |
| `ComposicaoDaAplicacao.cs` | Ordem dos `using` (apontada pelo `dotnet format`) |

## Decisões técnicas

### Etapa 1 — Fábrica de testes

- **Projeto separado.** `HubPedidos.Integracao.Testes` hospeda a API; `HubPedidos.Tests`
  fica com os testes unitários e de arquitetura e deixou de depender de
  `Mvc.Testing` e do cliente SignalR. Os testes HTTP dos Roteiros 6 e 7 foram movidos com
  `git mv`, preservando o histórico.
- **Composição real, substituição mínima.** A fábrica parte do `Program` real e troca só o
  banco e, quando o teste pede, o processamento da fila. Quanto mais a composição do teste
  se afasta da do produto, menos erros de registro a suíte encontra (Unidade IV, 8.1).
- **Ambiente "Testes".** User Secrets só são carregados em "Development", então a connection
  string e as senhas de desenvolvimento não entram nos testes. Os usuários de teste recebem
  senhas aleatórias novas a cada fábrica, nunca gravadas em disco.
- **Proteção contra o banco de desenvolvimento, em três camadas:**
  1. A connection string da configuração é substituída pela do banco em memória.
  2. Em `ConfigureTestServices`, as substituições do teste rodam **antes** e o registro
     do DbContext é refeito **depois**; nenhuma substituição ou variável de ambiente
     consegue desviar o banco. Esta é a proteção que impede o acesso.
  3. Em `CreateHost`, a fábrica confere qual banco o DbContext resolve e recusa tudo que
     não seja SQLite em memória. Esta conferência acontece depois que a aplicação já
     preparou o banco: ela **detecta** um erro na camada 2 e derruba o teste, mas não o
     impede. A contraprova confirmou isso: com a ordem invertida de propósito, o teste
     `Configuracao_externa_ou_substituicao_...` falhou com "só usa SQLite em memória;
     recusou o banco ...".
- **Cliente HTTP definido:** `CriarCliente()` não segue redirecionamentos e não guarda
  cookies. A autenticação é só o bearer token explícito de `CriarClienteAutenticadoAsync`.

### Etapa 2 — Banco efêmero

- **SQLite em memória, em cache compartilhado, com conexão guardiã.** O banco tem nome único
  por fábrica (`hubpedidos-testes-{guid}`, `Mode=Memory;Cache=Shared;Pooling=False`). A
  conexão guardiã é aberta no construtor e fechada só no descarte, depois do host. Enquanto
  ela existir, o banco existe; ao fechá-la, ele some.
- **Diferença em relação à Unidade IV** (uma única `SqliteConnection` passada ao
  `UseSqlite`). Este host tem um worker em segundo plano e o SignalR atendendo ao mesmo
  tempo que as requisições, e uma `SqliteConnection` não pode ser usada por duas threads.
  Com uma conexão por DbContext para o mesmo banco compartilhado, o uso concorrente é
  seguro e a regra "manter a conexão aberta" continua valendo pela guardiã. `Pooling=False`
  garante que nenhuma conexão ociosa do pool mantenha o banco vivo depois do descarte.
- **Esquema pelas migrations** (as mesmas da produção), aplicadas na inicialização, em vez de
  `EnsureCreated`. Assim, os testes também verificam que as migrations funcionam. Nenhum
  dado é semeado: cada teste cria o mínimo de que precisa (`CriarPedidoAsync`).
- **Diferenças do provedor produtivo que ainda exigem testes específicos.** O banco em
  memória tem a mesma tradução SQL do EF Core e o mesmo dialeto do arquivo SQLite, mas:
  - não tem **WAL nem journal em disco** (o `journal_mode` dos Roteiros 5 e 6 é desligado
    nos testes): o comportamento sob contenção (SQLITE_BUSY, timeouts) só foi medido com
    arquivo, no Roteiro 5;
  - no **cache compartilhado**, o bloqueio é por tabela (SQLITE_LOCKED), diferente do
    bloqueio por arquivo do banco real;
  - não exercita **E/S, fsync, tamanho do arquivo** nem a recuperação após queda;
  - se o produto migrasse para SQL Server ou PostgreSQL, tipos (decimal, datas), colação,
    funções e concorrência otimista precisariam de testes com contêiner efêmero do
    provedor real. Fidelidade limitada não é equivalência.

### Etapa 3 — Contratos críticos

- **Criação e consulta** pela rota real, seguindo o `Location` devolvido, com inspeção do
  banco (1 pedido, 2 itens) para provar que a resposta não veio só da memória do endpoint.
- **v1/v2, ETag/304 e autorização:** os testes dos Roteiros 6 e 7 agora rodam na fábrica
  nova, e a extensão de snapshot protege o corpo inteiro das respostas v1 e v2.
- **Fila sem atraso arbitrário:** `ProcessamentoObservavel` publica sinais ("iniciou",
  "concluiu") em canais. O teste de fila cheia monta o estado passo a passo (capacidade 1,
  um consumidor preso na porta, uma vaga ocupada), e o 429 é determinístico. A única
  sondagem (métricas) usa intervalos de 10 ms e termina assim que a condição vale, com
  limite de 10 s só para o caso de defeito.
- **Ordem aleatória:** o MSTest 4.5 não oferece ordem aleatória de execução (as opções do
  executor foram listadas com `--help`). A independência de ordem é verificada de outras
  formas: execução **paralela por método** (a ordem real varia a cada execução),
  **repetição** (`-Repeticoes N`) e o teste de isolamento entre fábricas.

### Etapa 4 — Análise e revisão automatizada

- **Analisadores do SDK, sem pacote:** `EnableNETAnalyzers`, `AnalysisLevel =
  latest-recommended` (o conjunto do SDK fixado no `global.json`) e
  `EnforceCodeStyleInBuild`.
- **O que bloqueia** (`WarningsAsErrors`, seletivo): **CS8602**, como na Unidade IV, além
  dos NU1901–NU1904 (vulnerabilidades) já existentes. Elevar todos os avisos a erro
  misturaria defeitos com recomendações.
- **O que fica visível** (`.editorconfig`): CA1822, CA1848 e CA1873 como *warning*;
  namespace com escopo de arquivo como *warning*; `var` como *suggestion*.
- **Primeira aplicação da política: 290 avisos em 6 regras.** Cada regra recebeu uma
  decisão, sem `NoWarn`, `#pragma` ou `SuppressMessage`:

| Regra | Ocorrências | Decisão | Por quê |
|---|---|---|---|
| CA1707 (sublinhado em nomes) | 222, todas em testes | `none` só em `tests/**` | `Dado_quando_entao` é a convenção de leitura dos relatórios de teste |
| CA1848 (LoggerMessage) | 26 | **corrigida**: `[LoggerMessage]` | o laço do consumidor registra um log por item; o gerador evita boxing e arrays |
| CA1873 (argumento caro no log) | 16 | **corrigida** | idem; e o resultado do PRAGMA foi para uma variável |
| CA1861 (matriz constante) | 20: 2 em migrations, 18 em testes | migrations: `generated_code = true`; testes: `suggestion` | a migration é gerada pelo dotnet-ef (uma correção à mão seria sobrescrita); nos testes, a matriz literal deixa o esperado à vista |
| CA1000 (estático em genérico) | 4 (2 únicas) | `none` só em `ResultadoDeMapeamento.cs` | `Sucesso`/`Falha` são as fábricas do tipo resultado |
| CA1725 (nome de parâmetro) | 2 (1 única) | **corrigida** | igual ao da classe base do EF Core |

  O build terminou com **0 avisos e 0 erros**, e `dotnet format --verify-no-changes`
  apontou uma ordenação de `using` (Roteiro 7), corrigida.
- **Revisão automatizada** (`scripts/revisao.ps1`): quatro etapas numeradas. A primeira que
  falhar encerra o script com o código da ferramenta e a mensagem `REVISAO REPROVADA na
  etapa 'X'` com a causa provável. O mesmo script roda no GitHub Actions, então o
  resultado é reproduzível localmente.
- **Workflow** (`.github/workflows/revisao.yml`): roda em `pull_request`, em `push` na `main`
  e manualmente. As actions oficiais são fixadas por SHA de commit, com
  `permissions: contents: read`. Os resultados são publicados como artefato **mesmo na
  falha** (`if: always()`), e o job `publicacao` (dotnet publish) depende de `revisao`
  (`needs`): se build, testes ou análise falharem, o pacote não é gerado.
- **Falhas controladas sem deixar código quebrado.** `ExemploComProblemas.cs` fica fora da
  compilação, e o teste que falha está atrás de `#if DEMONSTRAR_FALHA_DE_TESTE`. Só as
  propriedades `-p:DemonstrarFalhaDeAnalisador=true` e `-p:DemonstrarFalhaDeTeste=true`,
  passadas pelo script, os ativam (o mesmo mecanismo da Unidade IV).

## Extensões realizadas

1. **Testes de integração em paralelo:** `[assembly: Parallelize(Scope = MethodLevel)]`,
   depois de eliminar o estado compartilhado (banco, senhas e host por fábrica; o banco em
   arquivo temporário do Roteiro 7 deu lugar ao banco em memória).
2. **Cobertura e resultados como artefatos:** TRX e Cobertura XML por execução, publicados
   pelo workflow. A cobertura é usada como **mapa** do que não foi exercitado, nunca como
   meta numérica de qualidade.
3. **Contrato por snapshot com revisão explícita:** os corpos v1 e v2 aprovados ficam em
   `Contratos/Snapshots/`. Uma mudança gera `*.recebido.json` (ignorado pelo Git) e falha com
   instruções; aprovar é substituir o arquivo no mesmo commit da mudança. Os snapshots
   iniciais foram aprovados assim, depois de conferir que batiam com os contratos do
   Roteiro 6.

## Testes obrigatórios

| Teste obrigatório (relatório) | Onde está |
|---|---|
| Cada teste inicia com estado conhecido e não depende da ordem | `FabricaTestes.Cada_fabrica_comeca_com_esquema_das_migrations_e_sem_dados`, `Fabricas_diferentes_nao_compartilham_dados`; paralelismo por método e `revisao.ps1 -Repeticoes N` |
| Banco efêmero preserva a conexão/vida útil necessária e é descartado ao final | `FabricaTestes.Banco_vive_enquanto_a_conexao_guardia_existe_e_some_no_descarte`, `Configuracao_externa_ou_substituicao_nao_desviam_para_um_banco_em_arquivo`, `Banco_em_arquivo_e_recusado_pela_fabrica` |
| Contrato v1 e resposta condicional continuam protegidos por integração | `ContratoPorSnapshotTestes` (v1, v2 e 304), `VersionamentoTestes`, `CacheEValidacaoCondicionalTestes` |
| Uma violação selecionada de analisador falha o build conforme a política | `revisao.ps1 -VerificarBloqueios` (caso "analisador": CS8602 vira erro na etapa build) |
| O pipeline falha e impede a publicação quando build, testes ou análise falham | `revisao.ps1 -VerificarBloqueios` (casos "analisador" e "teste"; a etapa format foi vista bloqueando na primeira execução real); `revisao.yml`: `publicacao` com `needs: revisao` |
| (Etapa 3) criação/consulta, fila com consumidor observável | `CriacaoEConsultaTestes`, `FilaHttpTestes` |

## Resultados da verificação

Executado em 06/11/2026, Windows 11, PowerShell 5.1, `scripts/revisao.ps1 -VerificarBloqueios`:

| Caso | Resultado |
|---|---|
| Falha de analisador (`-DemonstrarFalha analisador`) | **bloqueou na etapa build**, código 1: `ExemploComProblemas.cs(13,16): error CS8602` e `(11,16): warning CA1822` (um erro e um aviso, como na Unidade IV) |
| Falha de teste (`-DemonstrarFalha teste`) | **bloqueou na etapa testes**, código 2: `Falha_demonstrativa_da_revisao_automatizada` com a mensagem do teste |
| Código fora do `.editorconfig` (ocorreu de verdade) | a primeira execução real **bloqueou na etapa format**, código 2: `TempoRealTestes.cs(1,1): error IMPORTS` (ordem dos `using` após mover o arquivo). Corrigido com `dotnet format` |
| Revisão normal | restore 1,1 s · build 1,8 s · format 9,9 s · testes 5,8 s → **APROVADA** |

- Testes: **188 aprovados** (152 em `HubPedidos.Tests`, 36 em `HubPedidos.Integracao.Testes`,
  18 deles novos neste roteiro), 0 falhas, em 3 execuções seguidas com `dotnet test` e
  na revisão em Release. Nenhum arquivo de banco foi criado pelos testes.
- Contraprova da proteção do banco: com a ordem do registro invertida de propósito, 1 teste
  falhou com a mensagem de recusa (código restaurado em seguida).
- Cobertura de linhas (mapa, não meta): pelos testes unitários, Application 92,9%,
  Domain 92,0%, Infrastructure 90,8%; pelos de integração, Api 65,8% e Infrastructure
  75,7%. A Application pela integração (48,4%) mostra o que só os testes unitários
  exercitam (importação em lote, parsers). Observação: o executor grava cada arquivo
  Cobertura em duplicata (mesmo conteúdo); isso não afeta os números.

**Não verificado neste computador:** a execução no GitHub Actions (o repositório não foi
publicado nem enviado por push, por instrução do professor) e o script no `pwsh` do Linux. O
workflow só usa o próprio script e actions oficiais; a primeira execução no GitHub deve ser
conferida na aba Actions.

## Versões utilizadas

| Item | Versão |
|---|---|
| SDK / runtime | .NET 10.0.401 / 10.0.12 (`AnalysisLevel` = conjunto recomendado deste SDK) |
| MSTest (inclui TRX e cobertura do Microsoft.Testing.Platform) | 4.5.1 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.13 |
| Microsoft.AspNetCore.SignalR.Client | 10.0.13 |
| actions/checkout, actions/setup-dotnet, actions/upload-artifact | v5.1.0, v5.4.0, v5.0.0 (fixadas por SHA) |
| PowerShell | 5.1 (Windows, local); `pwsh` do runner `ubuntu-latest` no CI |
