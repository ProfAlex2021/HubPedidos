# Roteiro 5 — evidências de diagnóstico

Todas as execuções: API em Release (ambiente Production), SQLite em arquivo com 5.000 pedidos (cópia limpa por execução),
carga de 75 req/s (30% POST), 10 s de aquecimento e 30 s de medição, timeout 2 s. Máquina: Windows 11 10.0.26300, Intel Core i5-13420H (12 núcleos lógicos), .NET 10.0.12. Percentis nearest-rank incluindo erros e timeouts.

## Medições antes/depois (sem ferramentas de diagnóstico ativas)

| Execução | Rota | Req. | Sucesso | Erros | Timeouts | Não enviadas | Vazão (req/s) | Média (ms) | p50 (ms) | p95 (ms) | p99 (ms) | Máx. (ms) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| antes (DELETE) | GET /api/pedidos | 1574 | 1574 | 0 | 0 | 0 | 51.3 | 167.3 | 14.1 | 800.1 | 1258.7 | 1881.1 |
| antes (DELETE) | POST /api/pedidos | 676 | 658 | 0 | 18 | 0 | 21.4 | 257.3 | 23.1 | 1269.1 | 2016.2 | 2027.2 |
| depois (WAL) | GET /api/pedidos | 1574 | 1574 | 0 | 0 | 0 | 52.5 | 8.8 | 8.6 | 15.9 | 17.6 | 34.5 |
| depois (WAL) | POST /api/pedidos | 676 | 676 | 0 | 0 | 0 | 22.5 | 11.7 | 11.8 | 19.1 | 21.6 | 33.9 |
| antes-2 (DELETE) | GET /api/pedidos | 1574 | 1522 | 0 | 52 | 0 | 49.5 | 605.0 | 448.5 | 1779.9 | 2018.6 | 2027.5 |
| antes-2 (DELETE) | POST /api/pedidos | 676 | 559 | 0 | 117 | 0 | 18.2 | 852.8 | 656.4 | 2019.3 | 2026.4 | 2027.9 |
| depois-2 (WAL) | GET /api/pedidos | 1574 | 1574 | 0 | 0 | 0 | 52.5 | 8.8 | 8.6 | 16.0 | 17.5 | 19.6 |
| depois-2 (WAL) | POST /api/pedidos | 676 | 676 | 0 | 0 | 0 | 22.5 | 11.4 | 11.3 | 18.7 | 20.6 | 32.0 |

## Execuções de diagnóstico (com dotnet-counters 30 s e dotnet-trace 10 s ativos)

| Execução | Rota | Req. | Sucesso | Erros | Timeouts | Não enviadas | Vazão (req/s) | Média (ms) | p50 (ms) | p95 (ms) | p99 (ms) | Máx. (ms) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| antes-diag (DELETE) | GET /api/pedidos | 1574 | 1567 | 0 | 7 | 0 | 50.8 | 521.5 | 390.2 | 1423.9 | 1774.7 | 2024.7 |
| antes-diag (DELETE) | POST /api/pedidos | 676 | 609 | 0 | 67 | 0 | 19.8 | 711.9 | 542.4 | 2015.4 | 2024.0 | 2028.2 |
| depois-diag (WAL) | GET /api/pedidos | 1574 | 1574 | 0 | 0 | 0 | 52.4 | 11.3 | 8.8 | 16.2 | 107.0 | 386.6 |
| depois-diag (WAL) | POST /api/pedidos | 676 | 676 | 0 | 0 | 0 | 22.5 | 20.6 | 12.0 | 20.7 | 359.9 | 857.7 |

## Carga em degraus (extensão)

| Execução | Rota | Req. | Sucesso | Erros | Timeouts | Não enviadas | Vazão (req/s) | Média (ms) | p50 (ms) | p95 (ms) | p99 (ms) | Máx. (ms) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| degraus DELETE @50/s | GET /api/pedidos | 524 | 524 | 0 | 0 | 0 | 34.9 | 10.2 | 9.3 | 17.1 | 19.3 | 194.9 |
| degraus DELETE @50/s | POST /api/pedidos | 226 | 226 | 0 | 0 | 0 | 15.1 | 18.4 | 17.8 | 25.8 | 55.9 | 68.7 |
| degraus DELETE @100/s | GET /api/pedidos | 1050 | 804 | 0 | 246 | 0 | 51.5 | 1284.6 | 1376.0 | 2020.0 | 2024.6 | 2030.9 |
| degraus DELETE @100/s | POST /api/pedidos | 450 | 302 | 0 | 148 | 0 | 19.3 | 1390.3 | 1555.0 | 2021.9 | 2025.3 | 2027.2 |
| degraus DELETE @200/s | GET /api/pedidos | 2100 | 307 | 0 | 1793 | 0 | 18.0 | 1872.0 | 2012.6 | 2023.8 | 2027.7 | 2036.6 |
| degraus DELETE @200/s | POST /api/pedidos | 900 | 122 | 0 | 778 | 0 | 7.2 | 1884.8 | 2012.7 | 2023.7 | 2027.0 | 2038.7 |
| degraus DELETE @400/s | GET /api/pedidos | 4200 | 169 | 0 | 2575 | 1456 | 9.9 | 1971.1 | 2013.9 | 2025.8 | 2034.6 | 2042.7 |
| degraus DELETE @400/s | POST /api/pedidos | 1800 | 64 | 0 | 1111 | 625 | 3.8 | 1975.8 | 2013.8 | 2025.4 | 2033.8 | 2043.0 |
| degraus WAL @50/s | GET /api/pedidos | 524 | 524 | 0 | 0 | 0 | 35.0 | 8.6 | 8.7 | 15.6 | 16.6 | 17.6 |
| degraus WAL @50/s | POST /api/pedidos | 226 | 226 | 0 | 0 | 0 | 15.1 | 11.7 | 11.5 | 18.9 | 20.2 | 23.4 |
| degraus WAL @100/s | GET /api/pedidos | 1050 | 1050 | 0 | 0 | 0 | 70.0 | 8.7 | 8.5 | 15.9 | 17.1 | 18.3 |
| degraus WAL @100/s | POST /api/pedidos | 450 | 450 | 0 | 0 | 0 | 30.0 | 11.2 | 11.3 | 18.3 | 20.6 | 21.1 |
| degraus WAL @200/s | GET /api/pedidos | 2100 | 2100 | 0 | 0 | 0 | 137.4 | 8.6 | 8.5 | 15.7 | 16.9 | 26.5 |
| degraus WAL @200/s | POST /api/pedidos | 900 | 900 | 0 | 0 | 0 | 58.9 | 84.6 | 15.1 | 318.8 | 485.1 | 1101.3 |
| degraus WAL @400/s | GET /api/pedidos | 4200 | 4200 | 0 | 0 | 0 | 266.2 | 23.1 | 17.5 | 65.9 | 98.7 | 139.0 |
| degraus WAL @400/s | POST /api/pedidos | 1800 | 1797 | 0 | 3 | 0 | 113.9 | 140.2 | 30.5 | 629.9 | 1110.2 | 2022.9 |
| degraus WAL @800/s | GET /api/pedidos | 8400 | 5308 | 0 | 228 | 2864 | 324.8 | 855.8 | 787.5 | 1769.4 | 2024.5 | 2041.5 |
| degraus WAL @800/s | POST /api/pedidos | 3600 | 2182 | 0 | 198 | 1220 | 133.5 | 947.9 | 866.5 | 2015.4 | 2025.2 | 2041.7 |

## dotnet-trace — top 25 por tempo inclusivo (antes, DELETE)

```
Top 25 Functions (Inclusive)                                                  Inclusive           Exclusive           
1.  Threads                                                                   100%                0%                  
2.  (Non-Activities)                                                          97.64%              0%                  
3.  Thread.StartCallback()                                                    96.67%              0%                  
4.  PortableThreadPool+WorkerThread.WorkerThreadStart()                       73.33%              0%                  
5.  ThreadPoolWorkQueue.Dispatch()                                            52.48%              0%                  
6.  EndpointMiddleware.Invoke(class Microsoft.AspNetCore.Http.HttpContext)    52.44%              0%                  
7.  StatusCodePagesMiddleware+<Invoke>d__3.MoveNext()                         52.44%              0%                  
8.  ExceptionHandlerMiddlewareImpl.Invoke(class Microsoft.AspNetCore.Http.    52.44%              0%                  
9.  AsyncMethodBuilderCore.Start(!!0&)                                        52.34%              0%                  
10. SqliteDataReader.NextResult()                                             52.24%              8.1%                
11. ExecutionContext.RunFromThreadPoolDispatchLoop(class System.Threading.    48.66%              0%                  
12. Thread.Sleep(int32)                                                       44.05%              44.05%              
13. RequestDelegateFactory+<>c__DisplayClass102_2.<HandleRequestBodyAndCom    40.93%              0%                  
14. RequestDelegateFactory+<>c__DisplayClass102_2+<<HandleRequestBodyAndCo    40.93%              0%                  
15. Anonymously Hosted DynamicMethods Assembly!dynamicClass.lambda_method1    40.93%              0%                  
16. PedidosEndpoints+<ReceberPedidoAsync>d__1.MoveNext()                      40.93%              0%                  
17. RepositorioDePedidos+<AdicionarAsync>d__2.MoveNext()                      40.93%              0%                  
18. DbContext+<SaveChangesAsync>d__63.MoveNext()                              40.92%              0%                  
19. StateManager.SaveChangesAsync(class Microsoft.EntityFrameworkCore.Chan    40.91%              0%                  
20. StateManager+<SaveChangesAsync>d__117.MoveNext()                          40.91%              0%                  
21. StateManager+<SaveChangesAsync>d__113.MoveNext()                          40.9%               0%                  
22. RelationalDatabase+<SaveChangesAsync>d__8.MoveNext()                      40.9%               0%                  
23. BatchExecutor+<ExecuteAsync>d__9.MoveNext()                               40.9%               0%                  
24. StateManager.SaveChangesAsync(class System.Collections.Generic.IList`1    40.9%               0%                  
25. SqliteCommand.ExecuteNonQuery()                                           40.73%              0%                  
```

## dotnet-trace — top 25 por tempo inclusivo (depois, WAL)

```
Top 25 Functions (Inclusive)                                                  Inclusive           Exclusive           
1.  (Non-Activities)                                                          100%                0%                  
2.  Threads                                                                   100%                0%                  
3.  Thread.StartCallback()                                                    94.59%              0%                  
4.  PortableThreadPool+WorkerThread.WorkerThreadStart()                       56.7%               0%                  
5.  LowLevelLifoSemaphore.WaitForSignal(int32)                                52.74%              49.73%              
6.  LowLevelLifoSemaphore.Wait(int32,bool)                                    49.78%              0.02%               
7.  WaitHandle.WaitOneNoCheck(int32,bool,class System.Object,value class W    21.28%              21.28%              
8.  Monitor.Wait(class System.Object,int32)                                   10.77%              10.77%              
9.  ManualResetEventSlim.Wait(int32,value class System.Threading.Cancellat    10.77%              0%                  
10. PortableThreadPool+IOCompletionPoller.Poll()                              5.41%               5.41%               
11. PortableThreadPool+GateThread.GateThreadStart()                           5.41%               0%                  
12. Task.InternalWait(int32,value class System.Threading.CancellationToken    5.41%               0%                  
13. TaskAwaiter.HandleNonSuccessAndDebuggerNotification(class System.Threa    5.41%               0%                  
14. Api!Program.<Main>(class System.String[])                                 5.41%               0%                  
15. Task.InternalWaitCore(int32,value class System.Threading.CancellationT    5.41%               0%                  
16. Task.SpinThenBlockingWait(int32,value class System.Threading.Cancellat    5.41%               0%                  
17. TimerQueue.TimerThread()                                                  5.41%               0%                  
18. Heartbeat.TimerLoop()                                                     5.41%               0%                  
19. AggregationManager.CollectWorker()                                        5.41%               0%                  
20. CounterGroup.PollForValues()                                              5.41%               0%                  
21. ManualResetEventSlim.Wait(value class System.TimeSpan)                    5.35%               0%                  
22. WaitHandle.WaitOne(value class System.TimeSpan)                           5.27%               0%                  
23. ThreadPoolWorkQueue.Dispatch()                                            3.91%               0%                  
24. il!Interop+Kernel32.GetQueuedCompletionStatus(int,unsigned int32&,unsi    3.01%               3.01%               
25. ExecutionContext.RunFromThreadPoolDispatchLoop(class System.Threading.    2.7%                0%                  
```

## BenchmarkDotNet — MapeadorDePedido.ParaDominio

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                        | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |-----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| 'DTO com 2 itens'             |   243.9 ns |   6.84 ns |   3.04 ns |  1.00 |    0.02 | 0.0839 |      - |     528 B |        1.00 |
| 'DTO com 50 itens + endereço' | 3,043.0 ns | 210.24 ns | 109.96 ns | 12.48 |    0.45 | 0.8850 | 0.0076 |    5569 B |       10.55 |
| 'DTO inválido (6 erros)'      |   477.0 ns |   7.46 ns |   3.31 ns |  1.96 |    0.03 | 0.2203 | 0.0010 |    1384 B |        2.62 |
