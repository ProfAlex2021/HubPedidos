# Roteiro 5 — BenchmarkDotNet, counters, traces, p95/p99 e testes de carga

**Objetivo:** diagnosticar o Hub sob carga, estabelecer metas de latência e investigar
gargalos com evidências reproduzíveis.

## Como reproduzir

```bash
dotnet tool restore
dotnet build -c Release

# 1. Banco de carga separado (5.000 pedidos), criado com o journal DELETE (padrão do SQLite)
#    PowerShell: $env:ASPNETCORE_ENVIRONMENT="Production"; $env:ConnectionStrings__HubPedidos="Data Source=dados/carga.db"; ...
ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__HubPedidos="Data Source=dados/carga.db" \
  Banco__AplicarMigracoesNaInicializacao=true Banco__ModoJournal=DELETE \
  src/HubPedidos.Api/bin/Release/net10.0/HubPedidos.Api.exe --semear 5000 --somente-preparar-banco

# 2. API em Release, fora do depurador (troque DELETE por WAL para o "depois")
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://localhost:5090 \
  ConnectionStrings__HubPedidos="Data Source=dados/carga.db" Banco__ModoJournal=DELETE \
  src/HubPedidos.Api/bin/Release/net10.0/HubPedidos.Api.exe

# 3. Carga versionada (outro terminal)
tools/HubPedidos.Carga/bin/Release/net10.0/HubPedidos.Carga.exe --url http://localhost:5090 --taxa 75 --aquecimento 10 --duracao 30 --rotulo antes --saida resultados-carga
tools/HubPedidos.Carga/bin/Release/net10.0/HubPedidos.Carga.exe --url http://localhost:5090 --degraus 50,100,200,400 --aquecimento 5 --duracao 15

# 4. Diagnóstico (em execução SEPARADA da medição; PID do processo HubPedidos.Api)
dotnet tool run dotnet-counters -- monitor -p <PID> --counters System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft.AspNetCore.Server.Kestrel,HubPedidos
dotnet tool run dotnet-counters -- collect -p <PID> --counters System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft.AspNetCore.Server.Kestrel,HubPedidos --format csv -o counters.csv --duration 00:00:00:30
dotnet tool run dotnet-trace -- collect -p <PID> --profile dotnet-common,dotnet-sampled-thread-time --duration 00:00:00:10 -o trace.nettrace
dotnet tool run dotnet-trace -- report trace.nettrace topN -n 25 --inclusive

# 5. Benchmark do método crítico
dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*MapeamentoDePedido*"
```

O arquivo `.nettrace` (15 MB) e os CSVs de amostras não são versionados. Os resultados
transcritos, os relatórios `topN` e os CSVs de counters estão em
[`resultados/roteiro-05-diagnostico.md`](resultados/roteiro-05-diagnostico.md),
`resultados/roteiro-05-counters-antes.csv` e `resultados/roteiro-05-counters-depois.csv`.
O trace também pode ser aberto no Visual Studio (*Arquivo > Abrir*) ou no PerfView.

## Plano de medição (etapa 1)

| Item | Definição |
|---|---|
| Rota de criação | `POST /api/pedidos` (valida, classifica e grava com `SaveChanges`) |
| Rota de consulta | `GET /api/pedidos?pagina=N&tamanho=20` (painel: `COUNT` + página projetada) |
| Método isolável | `MapeadorDePedido.ParaDominio` (BenchmarkDotNet) |
| Hipótese | Sob carga mista de leitura e escrita, a cauda de latência (p95/p99) é dominada pela **espera de bloqueio do SQLite em journal `DELETE`**, em que um escritor bloqueia leitores e outros escritores. A CPU e as alocações do código C# não são o gargalo. |
| Por que a hipótese importa | O EF Core cria bancos SQLite já em WAL (visto no log: `PRAGMA journal_mode = 'wal'`), mas um banco criado pelo script de `dotnet ef migrations script`, aplicado por outra ferramenta, fica em `DELETE`, o padrão do SQLite. O comportamento da aplicação dependia de como o banco foi criado. |
| Intervenção prevista | Fixar `PRAGMA journal_mode=WAL` na inicialização (`Banco:ModoJournal`), uma mudança de configuração pequena e reversível |
| Metas didáticas a 75 req/s | p95 < 100 ms e p99 < 250 ms em cada rota; erros + timeouts < 1%; vazão igual à oferecida |
| Critério de sucesso | Manter a intervenção só se atender às metas **e** os 146 testes continuarem passando |
| Carga | Modelo **aberto** a taxa fixa (75 req/s; 30% POST, 70% GET), 10 s de aquecimento descartado, 30 s de medição, timeout de 2 s por requisição |
| Amostra | ~2.250 requisições por execução (1.574 GET + 676 POST); percentis nearest-rank **incluindo** erros e timeouts |
| Ambiente | Windows 11 10.0.26300, Intel Core i5-13420H (12 núcleos lógicos), .NET SDK 10.0.401 / runtime 10.0.12, API em Release (ambiente Production, log do EF em Warning), SQLite em arquivo local com 5.000 pedidos (cópia limpa a cada execução), fila de processamento ociosa (nenhum pedido enfileirado) |

**Escolha da taxa.** Uma carga exploratória em degraus (50/100/200/400 req/s) mostrou o
colapso entre 50 e 100 req/s no estado inicial. Escolhi 75 req/s, abaixo do colapso
total, onde a cauda já aparece. As metas foram fixadas antes das execuções comparativas.

## Script de carga (`tools/HubPedidos.Carga`)

- **Modelo aberto:** a requisição *i* é disparada no instante *i/taxa*, mesmo que as
  anteriores não tenham terminado. A latência é medida **a partir do instante
  planejado**, então a espera causada pelo sistema entra na medida (evita a "omissão
  coordenada" de geradores que só disparam a próxima depois da anterior).
- **Janelas separadas:** o aquecimento roda e é descartado; só a janela de medição é registrada.
- **Erros e timeouts contam:** status diferente de 2xx, falha de conexão, timeout (2 s) e
  "não enviada" (gerador no limite de 512 requisições em voo) são registrados e
  **entram** nos percentis, com o critério declarado no relatório.
- **Percentis nearest-rank** sem interpolação (`CalculadoraDePercentis`, mesma convenção
  do livro), testados com a amostra do livro.
- **Mistura determinística** (30% escrita), parâmetros na linha de comando e saída em
  Markdown + CSV de amostras.

## Resultados (etapas 2 a 4)

### Antes × depois a 75 req/s (medições sem ferramentas ativas)

| Execução | Rota | Erros + timeouts | p50 (ms) | p95 (ms) | p99 (ms) | Vazão (req/s) |
|---|---|---:|---:|---:|---:|---:|
| antes (DELETE) | GET | 0 | 14,1 | 800,1 | 1.258,7 | 51,3 |
| antes (DELETE) | POST | 18 (2,7%) | 23,1 | 1.269,1 | 2.016,2 | 21,4 |
| antes-2 (DELETE) | GET | 52 (3,3%) | 448,5 | 1.779,9 | 2.018,6 | 49,5 |
| antes-2 (DELETE) | POST | 117 (17,3%) | 656,4 | 2.019,3 | 2.026,4 | 18,2 |
| **depois (WAL)** | GET | 0 | 8,6 | 15,9 | 17,6 | 52,5 |
| **depois (WAL)** | POST | 0 | 11,8 | 19,1 | 21,6 | 22,5 |
| **depois-2 (WAL)** | GET | 0 | 8,6 | 16,0 | 17,5 | 52,5 |
| **depois-2 (WAL)** | POST | 0 | 11,3 | 18,7 | 20,6 | 22,5 |

- O **antes não atende às metas** e é **instável**: as duas execuções idênticas
  divergiram muito (p50 do GET de 14 ms para 449 ms), um sinal de operação na beira da
  saturação, em que uma fila de bloqueio, depois de formada, não se desfaz.
- A **média esconde a cauda**: na primeira execução a média do GET foi 167 ms, enquanto
  o p95 foi 800 ms e o p99 1,26 s.
- O **depois atende a todas as metas** com folga e é reprodutível (diferenças < 1 ms
  entre as duas execuções), com vazão igual à oferecida e nenhum erro.

### Counters durante a carga do "antes" (etapa 2)

| Sinal | Valor | Leitura |
|---|---|---|
| CPU do processo | ~0,37 s/s de 12 núcleos (≈3%) | não é CPU |
| GC | 0,34 coletas gen0/s, 0 gen1/gen2, ~10 MB/s alocados | não é GC/alocação |
| Fila do ThreadPool | média 0 (pico 35) | sem starvation sustentado |
| `http.server.active_requests` POST | média 10,9, máx. 18 | requisições **esperando** |
| `kestrel.queued_connections` | até 13 | conexões aguardando |
| Exceções | ~35 `OperationCanceledException`/s, status 499 | são os timeouts do cliente (efeito, não causa) |
| `monitor.lock_contentions` | ~13/s | contenção baixa no código .NET |

Os counters mostram **espera sem consumo de recursos**, mas não dizem onde. Por isso o
próximo diagnóstico foi o trace.

### Trace (etapa 3)

Trace de 10 s (`dotnet-common` + `dotnet-sampled-thread-time`) no meio da carga do "antes":

- **44% do tempo amostrado das threads em `Thread.Sleep` (exclusivo)**, dentro de
  `SqliteDataReader.NextResult` (52% inclusivo);
- 41% no caminho `PedidosEndpoints.ReceberPedidoAsync → RepositorioDePedidos.AdicionarAsync →
  DbContext.SaveChangesAsync → SqliteCommand.ExecuteNonQuery → NextResult → Thread.Sleep`.

`NextResult` + `Thread.Sleep` é o laço de nova tentativa do Microsoft.Data.Sqlite quando o
SQLite responde `SQLITE_BUSY` (bloqueio ocupado): a thread dorme e tenta de novo até o
`CommandTimeout`. No modo WAL, o mesmo trace mostra **2%** em `Thread.Sleep`, e o topo
passa a ser threads ociosas do ThreadPool (`LowLevelLifoSemaphore.WaitForSignal`).

**Cadeia de evidências:** cauda alta e timeouts (carga) → espera sem CPU nem GC
(counters) → threads dormindo no laço de `SQLITE_BUSY` dentro do `SaveChanges` (trace) →
intervenção na causa (modo de journal) → cauda some (carga repetida).

**Achado adicional:** o Microsoft.Data.Sqlite executa os comandos de forma síncrona,
mesmo pelas APIs `async`. A espera por bloqueio **ocupa threads do ThreadPool**, o que
explica os picos de 35 itens na fila do pool. Sob WAL isso deixou de aparecer.

**Explicações descartadas pelos dados:** CPU (≈3%), GC (sem gen1/gen2), alocação do
mapeamento (BenchmarkDotNet: 244 ns e 528 B por DTO de 2 itens, ~0,002% de um POST de
11 ms), contenção de locks .NET (13/s, baixa) e starvation do ThreadPool (fila média 0).

### Intervenção e decisão (etapa 4)

- **Mudança:** `Banco:ModoJournal = WAL` em `appsettings.json`; `Program.cs` aplica
  `PRAGMA journal_mode=...` na inicialização e registra o modo ativo no log.
- **Testes funcionais:** 146 aprovados depois da mudança.
- **Recursos:** no "depois", CPU e GC semelhantes, nenhuma exceção de cancelamento, fila
  do ThreadPool zerada.
- **Conclusão: hipótese confirmada; intervenção mantida.** As metas passaram de "não
  atendidas" para "atendidas com folga", sem regressão funcional.
- **Incertezas e limites:** máquina de desenvolvimento, cliente e servidor na mesma
  máquina, SQLite local e rajada de 30 s. O WAL cria os arquivos `-wal`/`-shm` e não
  funciona em pastas de rede. Os números não valem como capacidade de produção.

## Extensão ("Aprimoramentos")

- **Carga em degraus para localizar a saturação** (`--degraus`). No estado inicial
  (DELETE), o colapso acontece entre 50 e 100 req/s. Com WAL, a cauda do POST começa a
  crescer em ~200 req/s (p99 485 ms) e o colapso aparece em 800 req/s, com 4.084
  requisições não enviadas ou em timeout. A capacidade útil ficou **cerca de 4× maior**.
  Tabelas completas no arquivo de resultados.

## Interferência das ferramentas

As execuções com dotnet-counters e dotnet-trace ativos tiveram cauda pior que as
medições limpas (no WAL, p99 do POST de 360 ms contra 21 ms). Por isso os números de
latência vêm de execuções **sem** ferramentas, e as execuções com ferramentas servem só
para localizar a causa.

## Testes obrigatórios do relatório

| Teste obrigatório (Relatório, Atividade 5) | Onde está |
|---|---|
| Ferramentas executadas contra build Release no ambiente registrado | Passo manual: API `bin/Release`, ambiente Production, plano acima; BenchmarkDotNet em Release |
| Carga com aquecimento e janela de medição separados | `GeradorDeCargaTestes.Aquecimento_fica_fora_e_erros_e_timeouts_sao_registrados` (150 enviadas, 100 registradas); `--aquecimento`/`--duracao` |
| p95/p99 sobre amostra identificada, não pela média | `CalculadoraDePercentisTestes` (amostra do livro e caso em que a média esconde a cauda); relatórios com N por rota |
| Cenário registra erros e timeouts | Mesmo teste do gerador (30 erros 500 e timeouts contados e incluídos no p99); colunas "Erros/Timeouts/Não enviadas" |
| Intervenção mantida só se melhora a meta sem regressão | Tabela antes/depois (2 repetições) + suíte de 146 testes aprovada |

## Versões utilizadas

| Item | Versão |
|---|---|
| dotnet-counters / dotnet-trace | 10.0.750501 (ferramentas locais no `dotnet-tools.json`) |
| BenchmarkDotNet | 0.15.8 |
| EF Core / Microsoft.Data.Sqlite | 10.0.13 |
| SDK / runtime | .NET 10.0.401 / 10.0.12 |
| Testes | 146 aprovados (4 novos neste roteiro) |
