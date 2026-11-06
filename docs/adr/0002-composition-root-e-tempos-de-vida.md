# ADR 0002 — Composition root e tempos de vida

- **Data:** 2026-11-06 (Roteiro 6)
- **Situação:** aceita

## Decisão

- Cada módulo expõe uma extensão de registro (`AddModuloPedidos`, `AddModuloProcessamento`,
  `AddPersistenciaDePedidos`, `AddNotificacoesPorLog`). O único lugar que chama todas é
  `ComposicaoDaAplicacao.ConfigurarServicos`, usado pelo `Program` e pelo teste de inicialização.
- `ValidateOnBuild` e `ValidateScopes` ficam ligados **em todos os ambientes**: um grafo
  inválido ou uma dependência cativa derruba a inicialização.
- Casos de uso recebem tudo por construtor. O único acesso direto ao contêiner é o worker,
  que cria um escopo por item da fila e resolve um único ponto de entrada
  (`ExecucaoDoProcessamento`): é composição, não regra de negócio.

## Tempos de vida

| Serviço | Tempo de vida | Justificativa |
|---|---|---|
| `CatalogoDeProcessadores`, `ServicoDeRecepcaoDePedidos` | singleton | Imutáveis depois de construídos; a reflexão roda uma vez |
| `ImportadorDeLotesPorFluxo`, `ProcessadorDeLotes`, opções | singleton | Sem estado entre chamadas |
| `FilaDeProcessamento`, `MetricasDoProcessamento`, `LimitadorDeAcessoAoBanco` | singleton | Estado compartilhado por requisições e consumidores, seguro para threads |
| `TimeProvider`, `INotificadorDePedidos` (log) | singleton | Sem estado |
| `ContextoDePedidos` | scoped | Não é seguro para uso concorrente; uma unidade de trabalho por requisição/item |
| `IRepositorioDePedidos`, `IConsultasDePedidos`, `IProcessamentoDePedido` | scoped | Dependem do DbContext |
| `ServicoDePedidos`, `ExecucaoDoProcessamento` | scoped | Dependem de serviços scoped; representam uma operação |
| `ProcessadorDePedidosWorker` | singleton (hosted) | Depende só de singletons e de `IServiceScopeFactory` |

Nenhum serviço é transient: não há serviço barato e sem estado que precise de instância nova
a cada resolução.

## Alternativas descartadas

- **Service locator** (`IServiceProvider` injetado nos casos de uso): esconderia
  dependências e impediria a validação do grafo.
- **Contêiner de terceiros**: o contêiner padrão atende a todos os casos atuais.
