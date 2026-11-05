# Hub de Pedidos — Programação Aplicada em .NET

Projeto de referência da disciplina **Programação Aplicada em .NET** (UNIP, Análise e
Desenvolvimento de Sistemas). Uma API .NET 10 para processamento confiável de pedidos,
construída de forma cumulativa ao longo dos oito roteiros: cada roteiro corresponde a
um commit e a uma tag anotada `roteiro-NN`.

## Pré-requisitos

- Visual Studio 2026 (18.0 ou superior) com a carga **ASP.NET e desenvolvimento Web**,
  ou apenas o SDK .NET 10.0.401 (fixado em `global.json`).
- Git.

## Como executar

```bash
dotnet tool restore
dotnet build HubPedidos.slnx
dotnet test
```

Antes de executar a API pela primeira vez, grave a connection string do SQLite em
User Secrets (fora do repositório):

```bash
powershell -ExecutionPolicy Bypass -File scripts/configurar-desenvolvimento.ps1
```

```bash
dotnet run --project src/HubPedidos.Api --launch-profile http
```

A API escuta em `http://localhost:5080`. As requisições de exemplo estão em
`src/HubPedidos.Api/HubPedidos.Api.http`.

## Roteiros

| Roteiro | Título | Guia | Tag |
|---|---|---|---|
| 1 | Pattern matching, atributos, reflexão controlada, records e DTOs imutáveis | [roteiro-01](docs/roteiros/roteiro-01.md) | `roteiro-01` |
| 2 | Span<T>, Memory<T>, pooling e caminhos críticos limitados por E/S e medidos | [roteiro-02](docs/roteiros/roteiro-02.md) | `roteiro-02` |
| 3 | Consultas previsíveis, projeções, tracking, consultas compiladas e JSON | [roteiro-03](docs/roteiros/roteiro-03.md) | `roteiro-03` |

## Estrutura

```
HubPedidos.slnx
src/
  HubPedidos.Domain/        Entidades, value objects e regras (sem dependências)
  HubPedidos.Application/   DTOs, mapeadores, casos de uso e processadores
  HubPedidos.Infrastructure/ EF Core + SQLite (persistência e consultas)
  HubPedidos.Api/           Minimal API (composition root)
benchmarks/
  HubPedidos.Benchmarks/    BenchmarkDotNet (sempre em Release)
tests/
  HubPedidos.Tests/         Testes MSTest (Microsoft.Testing.Platform)
docs/roteiros/              Guia de cada roteiro: execução, decisões e evidências
```

## Segredos

Nenhuma senha, connection string ou chave é versionada. A connection string do SQLite
fica em User Secrets (`scripts/configurar-desenvolvimento.ps1`) ou na variável de
ambiente `ConnectionStrings__HubPedidos`. Quando um roteiro precisar
de configuração sensível, o guia correspondente explica como defini-la com
User Secrets ou variáveis de ambiente.
