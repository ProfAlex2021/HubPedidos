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
dotnet build HubPedidos.slnx
dotnet test
dotnet run --project src/HubPedidos.Api --launch-profile http
```

A API escuta em `http://localhost:5080`. As requisições de exemplo estão em
`src/HubPedidos.Api/HubPedidos.Api.http`.

## Roteiros

| Roteiro | Título | Guia | Tag |
|---|---|---|---|
| 1 | Pattern matching, atributos, reflexão controlada, records e DTOs imutáveis | [roteiro-01](docs/roteiros/roteiro-01.md) | `roteiro-01` |

## Estrutura

```
HubPedidos.slnx
src/
  HubPedidos.Domain/        Entidades, value objects e regras (sem dependências)
  HubPedidos.Application/   DTOs, mapeadores, casos de uso e processadores
  HubPedidos.Api/           Minimal API (composition root)
tests/
  HubPedidos.Tests/         Testes MSTest (Microsoft.Testing.Platform)
docs/roteiros/              Guia de cada roteiro: execução, decisões e evidências
```

## Segredos

Nenhuma senha, connection string ou chave é versionada. Quando um roteiro precisar
de configuração sensível, o guia correspondente explica como defini-la com
User Secrets ou variáveis de ambiente.
