# Roteiro 4 — experimento de backpressure

Comando: `dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --experimento-fila` (2026-11-05).

Carga: 300 pedidos/s durante 5 s (admissão por TryWrite). Trabalho externo simulado: Task.Delay(20 ms) por pedido; banco: UPDATE real no SQLite em arquivo.
Ambiente: Microsoft Windows NT 10.0.26300.0, 12 núcleos lógicos, .NET 10.0.12.

| Configuração | Aceitos | Rejeitados | Descartados | Processados | Profundidade máx. | Espera média na fila (ms) | Espera máx. (ms) | Duração média do item (ms) | Pico banco | Vazão (pedidos/s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A: fila 50, 2 consumidores, banco 1, Wait | 345 | 1155 | 0 | 345 | 50 | 767 | 889 | 33.7 | 1 | 59 |
| B: fila 500, 8 consumidores, banco 1, Wait | 965 | 535 | 0 | 965 | 500 | 3318 | 5588 | 87.4 | 1 | 91 |
| C: fila 500, 8 consumidores, banco 8, Wait | 888 | 612 | 0 | 888 | 500 | 3956 | 6249 | 101.5 | 8 | 78 |
| D: fila 50, 2 consumidores, banco 1, DropOldest | 1500 | 0 | 1217 | 283 | 50 | 253 | 1113 | 43.4 | 1 | 46 |
