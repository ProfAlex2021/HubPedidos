# Roteiro 3 — SQL gerado pelo EF Core 10.0.13 (SQLite)

Capturado com `ToQueryString()` e com o interceptor `ContadorDeComandos`, em banco vazio.
Em banco vazio a consulta dividida (AsSplitQuery) envia só o primeiro comando; com dados, envia 2 (ver teste `Relatorio_de_pedidos_e_itens_nao_tem_N_mais_1`).

```sql
### Painel (projeção + filtro + paginação)
.param set @categoria 'Padrao'
.param set @minimoEmCentavos 10000
.param set @p 20

SELECT "p"."Numero", "p"."Id", "p"."ClienteId", "p"."CriadoEmUtc", "p"."Categoria", "p"."Situacao", (
    SELECT COALESCE(SUM("i"."Quantidade"), 0)
    FROM "ItensPedido" AS "i"
    WHERE "p"."Numero" = "i"."PedidoNumero"), "p"."ValorTotalCentavos"
FROM "Pedidos" AS "p"
WHERE "p"."Categoria" = @categoria AND "p"."ValorTotalCentavos" >= @minimoEmCentavos
ORDER BY "p"."CriadoEmUtc" DESC, "p"."Numero" DESC
LIMIT @p OFFSET @p

### Relatório - Projecao: 1 comando(s)
SELECT "p0"."Id", "p0"."ClienteId", "p0"."Regiao", "p0"."Prioridade", "p0"."Categoria", "p0"."MotivoDaCategoria", "p0"."Processador", "p0"."PrazoEstimadoEmDias", "p0"."Situacao", "p0"."CriadoEmUtc", "p0"."ValorTotalCentavos", "p0"."Numero", "p0"."Entrega_Cep", "p0"."Entrega_Cidade", "p0"."Entrega_Logradouro", "p0"."Entrega_Uf", "i"."Sku", "i"."Quantidade", "i"."PrecoUnitarioCentavos", "i"."Id"
FROM (
    SELECT "p"."Id", "p"."ClienteId", "p"."Regiao", "p"."Prioridade", "p"."Categoria", "p"."MotivoDaCategoria", "p"."Processador", "p"."PrazoEstimadoEmDias", "p"."Situacao", "p"."CriadoEmUtc", "p"."ValorTotalCentavos", "p"."Numero", "p"."Entrega_Cep", "p"."Entrega_Cidade", "p"."Entrega_Logradouro", "p"."Entrega_Uf"
    FROM "Pedidos" AS "p"
    ORDER BY "p"."CriadoEmUtc" DESC, "p"."Numero" DESC
    LIMIT @p1 OFFSET @p
) AS "p0"
LEFT JOIN "ItensPedido" AS "i" ON "p0"."Numero" = "i"."PedidoNumero"
ORDER BY "p0"."CriadoEmUtc" DESC, "p0"."Numero" DESC, "i"."Posicao"

### Relatório - Include: 1 comando(s)
SELECT "p0"."Numero", "p0"."Categoria", "p0"."ClienteId", "p0"."CriadoEmUtc", "p0"."Id", "p0"."MotivoDaCategoria", "p0"."PrazoEstimadoEmDias", "p0"."Prioridade", "p0"."Processador", "p0"."Regiao", "p0"."Situacao", "p0"."ValorTotalCentavos", "p0"."Entrega_Cep", "p0"."Entrega_Cidade", "p0"."Entrega_Logradouro", "p0"."Entrega_Uf", "i"."Id", "i"."PedidoNumero", "i"."Posicao", "i"."PrecoUnitarioCentavos", "i"."Quantidade", "i"."Sku"
FROM (
    SELECT "p"."Numero", "p"."Categoria", "p"."ClienteId", "p"."CriadoEmUtc", "p"."Id", "p"."MotivoDaCategoria", "p"."PrazoEstimadoEmDias", "p"."Prioridade", "p"."Processador", "p"."Regiao", "p"."Situacao", "p"."ValorTotalCentavos", "p"."Entrega_Cep", "p"."Entrega_Cidade", "p"."Entrega_Logradouro", "p"."Entrega_Uf"
    FROM "Pedidos" AS "p"
    ORDER BY "p"."CriadoEmUtc" DESC, "p"."Numero" DESC
    LIMIT @p1 OFFSET @p
) AS "p0"
LEFT JOIN "ItensPedido" AS "i" ON "p0"."Numero" = "i"."PedidoNumero"
ORDER BY "p0"."CriadoEmUtc" DESC, "p0"."Numero" DESC

### Relatório - Dividida: 1 comando(s)
SELECT "p"."Id", "p"."ClienteId", "p"."Regiao", "p"."Prioridade", "p"."Categoria", "p"."MotivoDaCategoria", "p"."Processador", "p"."PrazoEstimadoEmDias", "p"."Situacao", "p"."CriadoEmUtc", "p"."ValorTotalCentavos", "p"."Numero", "p"."Entrega_Cep", "p"."Entrega_Cidade", "p"."Entrega_Logradouro", "p"."Entrega_Uf"
FROM "Pedidos" AS "p"
ORDER BY "p"."CriadoEmUtc" DESC, "p"."Numero" DESC
LIMIT @p1 OFFSET @p

### Detalhe
SELECT "p0"."Id", "p0"."ClienteId", "p0"."Regiao", "p0"."Prioridade", "p0"."Categoria", "p0"."MotivoDaCategoria", "p0"."Processador", "p0"."PrazoEstimadoEmDias", "p0"."Situacao", "p0"."CriadoEmUtc", "p0"."ValorTotalCentavos", "p0"."Numero", "p0"."Entrega_Cep", "p0"."Entrega_Cidade", "p0"."Entrega_Logradouro", "p0"."Entrega_Uf", "i"."Sku", "i"."Quantidade", "i"."PrecoUnitarioCentavos", "i"."Id"
FROM (
    SELECT "p"."Id", "p"."ClienteId", "p"."Regiao", "p"."Prioridade", "p"."Categoria", "p"."MotivoDaCategoria", "p"."Processador", "p"."PrazoEstimadoEmDias", "p"."Situacao", "p"."CriadoEmUtc", "p"."ValorTotalCentavos", "p"."Numero", "p"."Entrega_Cep", "p"."Entrega_Cidade", "p"."Entrega_Logradouro", "p"."Entrega_Uf"
    FROM "Pedidos" AS "p"
    WHERE "p"."Id" = @id_Valor
    LIMIT 1
) AS "p0"
LEFT JOIN "ItensPedido" AS "i" ON "p0"."Numero" = "i"."PedidoNumero"
ORDER BY "p0"."Numero", "i"."Posicao"

### JSON - filtro por cidade
SELECT "p"."ClienteId"
FROM "Pedidos" AS "p"
WHERE "p"."EnderecoJson" ->> 'Cidade' = 'Curitiba'
```
