# Roteiro 2 — resultados do BenchmarkDotNet

Execução em Release, fora do depurador, em 2026-11-05. Relatórios GitHub gerados pelo BenchmarkDotNet, copiados sem edição.

## Parsing de linha (após cache do delegate)

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                     | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated  | Alloc Ratio |
|--------------------------- |---------:|---------:|--------:|------:|--------:|---------:|-------:|-----------:|------------:|
| 'Inicial (Split/string)'   | 357.5 μs | 18.72 μs | 9.79 μs |  1.00 |    0.04 | 194.3359 | 0.4883 | 1193.12 KB |        1.00 |
| ReadOnlySpan<char>         | 348.5 μs | 14.23 μs | 6.32 μs |  0.98 |    0.03 |  69.8242 |      - |  429.69 KB |        0.36 |
| 'ReadOnlySpan<byte> UTF-8' | 294.0 μs |  6.88 μs | 3.60 μs |  0.82 |    0.02 |  69.8242 |      - |  429.69 KB |        0.36 |

## Parsing de linha (primeira execução, antes do ajuste)

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                     | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated  | Alloc Ratio |
|--------------------------- |---------:|---------:|--------:|------:|--------:|---------:|-------:|-----------:|------------:|
| 'Inicial (Split/string)'   | 354.3 μs |  8.47 μs | 4.43 μs |  1.00 |    0.02 | 194.3359 | 0.4883 | 1193.12 KB |        1.00 |
| ReadOnlySpan<char>         | 354.8 μs | 13.49 μs | 5.99 μs |  1.00 |    0.02 |  69.8242 |      - |  429.69 KB |        0.36 |
| 'ReadOnlySpan<byte> UTF-8' | 306.4 μs | 10.88 μs | 5.69 μs |  0.86 |    0.02 |  90.3320 |      - |  554.69 KB |        0.46 |

## Importação de lotes

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method       | ResultadosPorEscrita | Mean       | Error     | StdDev    | Gen0     | Allocated  |
|------------- |--------------------- |-----------:|----------:|----------:|---------:|-----------:|
| **GravarNdjson** | **1**                    | **111.865 ms** | **8.1508 ms** | **4.2630 ms** | **400.0000** | **2504.04 KB** |
| **GravarNdjson** | **100**                  |   **3.537 ms** | **0.2873 ms** | **0.1025 ms** |   **3.9063** |   **37.97 KB** |
```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                        | Linhas | Mean         | Error        | StdDev     | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated  | Alloc Ratio |
|------------------------------ |------- |-------------:|-------------:|-----------:|------:|--------:|----------:|----------:|---------:|-----------:|------------:|
| **'Inicial (ReadToEnd + lista)'** | **100**    |     **45.21 μs** |     **0.834 μs** |   **0.370 μs** |  **1.00** |    **0.01** |   **28.1372** |    **6.6528** |        **-** |  **172.45 KB** |        **1.00** |
| 'Fluxo + ArrayPool'           | 100    |     32.57 μs |     0.989 μs |   0.517 μs |  0.72 |    0.01 |    9.0942 |         - |        - |   55.76 KB |        0.32 |
| 'Fluxo + MemoryPool'          | 100    |     31.39 μs |     0.393 μs |   0.175 μs |  0.69 |    0.01 |    9.0942 |         - |        - |   55.78 KB |        0.32 |
|                               |        |              |              |            |       |         |           |           |          |            |             |
| **'Inicial (ReadToEnd + lista)'** | **10000**  | **20,073.34 μs** | **1,329.564 μs** | **695.388 μs** |  **1.00** |    **0.05** | **3000.0000** | **1593.7500** | **593.7500** |   **16919 KB** |        **1.00** |
| 'Fluxo + ArrayPool'           | 10000  |  3,311.42 μs |   113.669 μs |  50.470 μs |  0.17 |    0.01 |  902.3438 |         - |        - | 5547.16 KB |        0.33 |
| 'Fluxo + MemoryPool'          | 10000  |  3,253.98 μs |   118.982 μs |  62.230 μs |  0.16 |    0.01 |  902.3438 |         - |        - | 5547.19 KB |        0.33 |
```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                     | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated  | Alloc Ratio |
|--------------------------- |---------:|---------:|--------:|------:|--------:|---------:|-------:|-----------:|------------:|
| 'Inicial (Split/string)'   | 354.3 μs |  8.47 μs | 4.43 μs |  1.00 |    0.02 | 194.3359 | 0.4883 | 1193.12 KB |        1.00 |
| ReadOnlySpan<char>         | 354.8 μs | 13.49 μs | 5.99 μs |  1.00 |    0.02 |  69.8242 |      - |  429.69 KB |        0.36 |
| 'ReadOnlySpan<byte> UTF-8' | 306.4 μs | 10.88 μs | 5.69 μs |  0.86 |    0.02 |  90.3320 |      - |  554.69 KB |        0.46 |

## Escrita NDJSON (flush por item × por lote)

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
13th Gen Intel Core i5-13420H 2.10GHz, 1 CPU, 12 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method       | ResultadosPorEscrita | Mean       | Error     | StdDev    | Gen0     | Allocated  |
|------------- |--------------------- |-----------:|----------:|----------:|---------:|-----------:|
| **GravarNdjson** | **1**                    | **111.865 ms** | **8.1508 ms** | **4.2630 ms** | **400.0000** | **2504.04 KB** |
| **GravarNdjson** | **100**                  |   **3.537 ms** | **0.2873 ms** | **0.1025 ms** |   **3.9063** |   **37.97 KB** |
