# ADR 0003 — Versionamento da consulta de pedido

- **Data:** 2026-11-06 (Roteiro 6)
- **Situação:** aceita

## Contexto

A consulta `GET /api/pedidos/{id}` é usada pelo **front-end do painel** (versões já
instaladas nos navegadores dos atendentes) e por **integrações de lojistas** (que leem
`valorTotal` como número). A nova tela de acompanhamento precisa de um formato diferente:
cliente como objeto, dinheiro com moeda e uma previsão de entrega.

## Decisão

- Versão no **segmento da URL** (`/api/v1/...`, `/api/v2/...`).
- **Versão ausente = v1** (padrão): `/api/pedidos/{id}` continua respondendo exatamente
  como antes.
- **Versão não suportada** → `400` com ProblemDetails e `api-supported-versions: 1, 2`.
- v1 **inalterada**; v2 com mudanças incompatíveis deliberadas (matriz no guia do
  Roteiro 6), produzida por um adaptador sobre o mesmo serviço e a mesma consulta.
- v1 depreciada: `Deprecation: @1793836800` (2026-11-05), `Sunset: Fri, 31 Dec 2027
  23:59:59 GMT` (data demonstrativa) e `Link: </api/v2/pedidos/{id}>; rel="successor-version"`.
- Documentos OpenAPI separados: `/openapi/v1.json` e `/openapi/v2.json`.
- Sem pacote de versionamento (Asp.Versioning): rotas explícitas e uma classe de política
  bastam para duas versões de um endpoint. O pacote passaria a valer a pena com muitas
  versões, negociação por cabeçalho ou relatório automático de versões.

## Alternativas descartadas

- **Cabeçalho** (`api-version: 2`): invisível em logs e no navegador, e o cache precisaria
  variar por cabeçalho.
- **Query string** (`?versao=2`): fácil de esquecer e mistura a versão com filtros.
- **Alterar a v1 "só um pouco"**: quebraria os lojistas que leem `valorTotal` como número.

## Critérios para retirar a v1

1. O contador `hubpedidos.api.consulta_pedido.chamadas{versao=1}` (dotnet-counters ou
   OpenTelemetry) fica abaixo de 1% das consultas por 30 dias seguidos.
2. Os lojistas identificados confirmaram a migração.
3. A data de `Sunset` foi comunicada com pelo menos 6 meses de antecedência.
4. Depois da data, a v1 responde `410 Gone` por mais 30 dias, com link para a v2, antes
   de a rota ser removida.
