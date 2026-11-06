# Roteiro 4 — Channels, SemaphoreSlim, coleções concorrentes e backpressure

**Objetivo:** processar pedidos por uma fila limitada, controlando produtores,
consumidores, acesso ao banco e encerramento da aplicação.

## Como executar

```bash
dotnet test
dotnet run --project src/HubPedidos.Api --launch-profile http   # o worker sobe junto com a API

# Experimento de backpressure (etapa 4), em Release
dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --experimento-fila
```

A configuração fica na seção `Fila` do `appsettings.json` e pode ser sobrescrita por
variáveis de ambiente, por exemplo `Fila__Capacidade=3`:

| Chave | Padrão | Significado |
|---|---|---|
| `Capacidade` | 100 | Itens aguardando leitura (limite de memória da fila) |
| `ModoQuandoCheia` | `Wait` | `Wait`, `DropNewest`, `DropOldest` ou `DropWrite` |
| `Consumidores` | 4 | Tarefas consumidoras permanentes |
| `LimiteDeAcessoAoBanco` | 1 | Operações simultâneas no banco (SemaphoreSlim) |
| `DuracaoSimuladaEmMs` | 20 | Trabalho externo simulado por pedido (fora do banco) |
| `PrazoDeEncerramentoEmSegundos` | 15 | Prazo para drenar a fila ao encerrar |

## Arquivos produzidos

| Arquivo | Conteúdo |
|---|---|
| `Application/Processamento/Fila/FilaDeProcessamento.cs` | `ProcessarPedidoJob`, `OpcoesDaFila`, `Channel` limitado, `TryWrite`/`WriteAsync`, descarte contado |
| `Application/Processamento/Fila/ProcessadorDePedidosWorker.cs` | `BackgroundService` com N consumidores, escopo de DI por item, encerramento por drenagem |
| `Application/Processamento/Fila/LimitadorDeAcessoAoBanco.cs` | `SemaphoreSlim` com pico observado e devolução no `finally` |
| `Application/Processamento/Fila/MetricasDoProcessamento.cs` | Contadores com `Interlocked` e estado por pedido em `ConcurrentDictionary` |
| `Infrastructure/Processamento/ProcessamentoDePedidoEfCore.cs` | Processamento idempotente (`UPDATE ... WHERE Situacao='Recebido'`) |
| `Infrastructure/Persistencia/Migracoes/*_Processamento.cs` | Coluna `ProcessadoEmUtc` ([SQL](resultados/roteiro-04-migracao-processamento.sql)) |
| `Api/Endpoints/ProcessamentoEndpoints.cs` | Produtores (interativo e em lote) e métricas |
| `benchmarks/.../Experimentos/ExperimentoDeBackpressure.cs` | Carga controlada acima da capacidade, 4 configurações |
| `tests/.../Processamento/` | 15 testes novos + ambiente com processamento falso controlável |

## Decisões técnicas

1. **O item da fila é mínimo.** `ProcessarPedidoJob` carrega só o `PedidoId` e o instante
   de admissão. Nenhum `DbContext`, entidade ou DTO entra na fila: o consumidor busca o
   que precisa no próprio escopo, e cada item retém poucos bytes.
2. **Fila cheia: `Wait` + duas formas de admissão.** Pedidos não podem ser descartados
   em silêncio, por isso o modo é `Wait`.
   - A fronteira HTTP interativa (`POST /api/pedidos/{id}/processamento`) usa
     `TryWrite`: com a fila cheia responde **429 Too Many Requests com `Retry-After: 1`**
     na hora, sem prender a requisição.
   - O produtor em lote (`POST /api/processamento/pendentes`) usa `WriteAsync`, que
     **aguarda** espaço (backpressure até o cliente HTTP) e respeita o cancelamento da
     requisição. Um item cancelado não é contado como aceito.
   - Durante o encerramento, a resposta é 503. Os modos de descarte estão implementados
     e testados, e o callback `itemDropped` os conta separadamente das rejeições.
3. **Consumidores.** O `BackgroundService` inicia N tarefas, e cada uma lê com
   `ReadAllAsync`. Cada item ganha um **escopo de DI próprio** (`CreateAsyncScope`), então
   o `DbContext` scoped nunca é compartilhado entre consumidores. Sucesso, falha e
   duração são registrados com o `PedidoId` (log estruturado). A falha de um item fica
   nas métricas e no log, e o consumidor continua.
4. **Encerramento: drenar, com prazo.** No `StopAsync`, a fila é concluída (admissão
   fecha e devolve 503) e os consumidores esvaziam o que já foi aceito. Eles **não**
   observam o `stoppingToken`, que é cancelado logo no início do encerramento e
   impediria a drenagem; observam um token forçado, cancelado quando o prazo do host
   (`HostOptions.ShutdownTimeout`) termina. Os pedidos que sobram continuam `Recebido` no
   banco e podem ser reenviados pelo produtor em lote: o processamento é idempotente.
5. **Defeito encontrado pelos testes.** No .NET 10, o `BackgroundService` executa o
   `ExecuteAsync` em segundo plano. Com os consumidores criados ali, um `StopAsync` logo
   depois do `StartAsync` via a lista de consumidores ainda vazia e encerrava **sem
   drenar**. Os consumidores passaram a ser criados de forma síncrona em `StartAsync`; o
   teste `Encerramento_impede_novos_itens_e_drena_os_aceitos` cobre o caso, e a suíte
   rodou 5 vezes seguidas sem falha.
6. **SemaphoreSlim só no trecho do banco.** O trabalho externo simulado (20 ms) fica
   **fora** do limitador; apenas o `UPDATE` entra. `WaitAsync` vem **antes** do `try`
   (um cancelamento durante a espera não devolve uma permissão que não foi adquirida), e
   `Release` fica no `finally` (falha e cancelamento devolvem a permissão).
7. **Limite do semáforo × pool de conexões.** O SQLite aceita **um escritor por vez**;
   mais escritas simultâneas só criam espera por bloqueio dentro do banco. Por isso o
   padrão é 1, e o experimento C confirma que 8 acessos simultâneos pioram vazão e
   latência. Em um SQL Server, o limite seria escolhido abaixo do pool de conexões
   (padrão de 100 por connection string), reservando conexões para as rotas de leitura
   (painel, detalhe) que usam o mesmo pool.
8. **Coleções concorrentes, só com operações atômicas.** Os contadores usam
   `Interlocked` (inclusive um laço de compare-and-swap para pico e máximo). O
   `ConcurrentDictionary` guarda os pedidos em andamento com `TryAdd` e `TryRemove`, e as
   contagens por resultado com `AddOrUpdate`. Nunca se usa "verificar e depois inserir"
   em duas chamadas. O `TryAdd` é o que impede que o mesmo pedido, enfileirado duas vezes,
   seja processado em paralelo por dois consumidores.
9. **Configuração didática escolhida.** Fila de 100, 4 consumidores e 1 acesso ao banco.
   Com o SQLite, o banco é o gargalo (cerca de 90 pedidos/s no experimento): 4
   consumidores bastam para manter o banco ocupado enquanto outros fazem o trabalho
   externo. Uma fila de 100 dá no máximo ~1 s de espera antes da rejeição, um atraso
   aceitável para quem acompanha o pedido.

## Experimento de backpressure (etapa 4)

300 pedidos/s durante 5 s (1.500 pedidos, admissão por `TryWrite`), contra a fila e o
worker reais e um SQLite real em arquivo. Resultados completos em
[`resultados/roteiro-04-backpressure.md`](resultados/roteiro-04-backpressure.md).

| Configuração | Aceitos | Rejeitados | Descartados | Processados | Espera média / máx. na fila | Duração média do item | Pico no banco | Vazão |
|---|---:|---:|---:|---:|---|---:|---:|---:|
| A: fila 50, 2 consumidores, banco 1 | 345 | 1.155 | 0 | 345 | 767 / 889 ms | 33,7 ms | 1 | 59/s |
| B: fila 500, 8 consumidores, banco 1 | 965 | 535 | 0 | 965 | 3.318 / 5.588 ms | 87,4 ms | 1 | 91/s |
| C: fila 500, 8 consumidores, **banco 8** | 888 | 612 | 0 | 888 | 3.956 / 6.249 ms | 101,5 ms | 8 | 78/s |
| D: fila 50, 2 consumidores, DropOldest | 1.500 | 0 | **1.217** | 283 | 253 / 1.113 ms | 43,4 ms | 1 | 46/s |

Leitura dos resultados:

- **A × B:** a fila maior aceitou mais, mas a vazão subiu só até o limite do banco
  (~90/s), e a espera na fila passou de ~0,8 s para até 5,6 s. Fila maior não cria
  capacidade; ela troca rejeição por atraso.
- **B × C:** aumentar o paralelismo no banco **piorou** a vazão (91 → 78/s) e a
  duração do item (87 → 101 ms): as escritas competem pelo bloqueio único do SQLite.
- **D:** sem rejeições, mas 1.217 pedidos foram descartados. É aceitável para dados
  substituíveis, nunca para pedidos; por isso o padrão é `Wait`.
- A carga é uma rajada local de curta duração em uma máquina de desenvolvimento; os
  números servem para comparar as configurações, não como capacidade de produção.

## Extensão ("Aprimoramentos")

- **Idempotência persistida para reprocessamentos.** O processamento é um único
  `UPDATE ... SET Situacao='Processado', ProcessadoEmUtc=@agora WHERE Id=@id AND Situacao='Recebido'`.
  Reenviar o mesmo pedido (pelo cliente, por duplicidade na fila ou depois de um
  reinício) não altera nada e é contado como "ignorado"; um pedido cancelado nunca é
  processado. A garantia vale entre processos, não só na memória (teste
  `Idempotencia_persistida_impede_segundo_processamento...`; na execução manual, 150
  envios do mesmo pedido geraram 1 processamento).

## Testes obrigatórios do relatório

| Teste obrigatório (Relatório, Atividade 4) | Onde está |
|---|---|
| Modo Wait: produtor aguarda; descarte: política observada; rejeição: TryWrite e resposta HTTP | `FilaDeProcessamentoTestes.No_modo_Wait_o_produtor_aguarda_e_TryWrite_rejeita`, `Modos_de_descarte_seguem_a_politica...` (3 modos), `Endpoint_produtor_responde_202_429_com_Retry_After_404_e_503`; manual: 23 × 429 com `Retry-After: 1` |
| Operações no banco nunca excedem o limite | `ConsumidoresTestes.Acessos_ao_banco_nunca_excedem_o_limite_definido` (6 consumidores, limite 2, pico 2), `Excecao_na_regiao_protegida_devolve_a_permissao`, `Fluxo_completo_com_EF...` (pico 1) |
| Falha de um item não encerra os consumidores | `Falha_de_um_item_nao_encerra_os_consumidores` (9 processados, 1 falha) |
| Cancelamento impede novos itens e há estratégia explícita para pendentes | `Encerramento_impede_novos_itens_e_drena_os_aceitos`, `Encerramento_forcado_interrompe_e_deixa_pendentes_identificados`, `Cancelamento_do_produtor_nao_admite_o_item` |
| Nenhum pedido processado duas vezes | `Pedido_enfileirado_duas_vezes_nao_e_processado_em_paralelo`, `Idempotencia_persistida...`, `Fluxo_completo_com_EF_processa_cada_pedido_uma_unica_vez` (20 + 5 reenvios → 20 processados, 5 ignorados) |

## Evidências de execução manual

API em Development, banco com os 5.000 pedidos do Roteiro 3:

- `POST /api/processamento/pendentes?limite=150` → `150` em 0,76 s. As métricas logo
  depois mostravam fila com 91 itens, 55 processados, pico de 1 acesso ao banco e espera
  média de 317 ms.
- 300 `POST /api/pedidos/{id}/processamento`, 25 em paralelo → 300 × 202. Os
  consumidores esvaziaram a fila de 100 mais rápido do que os processos `curl`
  produziam.
- 150 envios do **mesmo** pedido → 1 processado; as repetições foram contadas como
  ignoradas (idempotência).
- Com `Fila__Capacidade=3`, `Fila__Consumidores=1` e `Fila__DuracaoSimuladaEmMs=300`:
  30 solicitações simultâneas → 7 × 202 e **23 × 429, todas com `Retry-After: 1`**. Os
  pedidos aceitos terminaram `Processado`.
- **Não verificado manualmente:** o encerramento gracioso com Ctrl+C. No Windows, o
  `Stop-Process` usado nesta sessão termina o processo sem sinal de encerramento. A
  drenagem e o encerramento forçado estão cobertos pelos testes do worker.

## Correções em roteiros anteriores

- `PersistenciaDePedidosTestes.Esquema_e_reproduzivel...` verificava a lista exata
  `["Inicial"]`. Agora confere que todas as migrations do assembly foram aplicadas e
  que nenhuma está pendente, o que continua válido a cada nova migration.

## Versões utilizadas

| Item | Versão |
|---|---|
| Microsoft.Extensions.Hosting.Abstractions | 10.0.13 (só abstrações, na camada de aplicação) |
| EF Core / SQLite | 10.0.13 |
| SDK / runtime | .NET 10.0.401 / 10.0.12 |
| Testes | 142 aprovados (15 novos neste roteiro), 5 execuções consecutivas sem falha |
