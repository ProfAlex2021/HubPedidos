# Roteiro 3 — resultados do BenchmarkDotNet

SQLite em memória com 5.000 pedidos (massa determinística, semente 42). Release, fora do depurador.

## Consulta comum × compilada (confirmação: 5 aquecimentos, 20 iterações)

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-PTSBVR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=20  LaunchCount=1  WarmupCount=5  

```
| Method                          | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------------- |----------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| 'Detalhe: consulta comum'       | 123.76 μs | 2.016 μs | 2.157 μs |  1.00 |    0.02 | 8.7891 | 0.4883 |  55.53 KB |        1.00 |
| 'Detalhe: EF.CompileAsyncQuery' |  88.83 μs | 1.477 μs | 1.642 μs |  0.72 |    0.02 | 7.3242 | 0.4883 |  44.94 KB |        0.81 |

## Primeira execução (3 aquecimentos, 8 iterações)

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                          | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| 'Detalhe: consulta comum'       | 138.51 μs | 58.483 μs | 25.967 μs |  1.02 |    0.23 | 8.7891 | 0.4883 |  55.23 KB |        1.00 |
| 'Detalhe: EF.CompileAsyncQuery' |  89.42 μs |  2.929 μs |  1.532 μs |  0.66 |    0.09 | 7.3242 | 0.4883 |  44.94 KB |        0.81 |

## Paginação profunda: deslocamento × cursor

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                                   | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |---------:|---------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| 'Página 41 por deslocamento (com COUNT)' | 444.7 μs | 13.86 μs | 6.15 μs |  1.00 |    0.02 | 22.4609 | 1.9531 | 140.26 KB |        1.00 |
| 'Mesma página por cursor'                | 399.2 μs | 10.81 μs | 4.80 μs |  0.90 |    0.02 | 22.4609 | 1.9531 | 140.97 KB |        1.01 |
