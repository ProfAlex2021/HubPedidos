# Roteiro 3 — Consultas previsíveis, projeções, tracking, consultas compiladas e JSON

**Objetivo:** persistir e consultar pedidos com EF Core, analisando SQL, roundtrips,
tracking e o impacto dos mapeamentos adotados.

## Como executar

```bash
# 1. Uma vez por máquina: grava a connection string em User Secrets (fora do Git)
powershell -ExecutionPolicy Bypass -File scripts/configurar-desenvolvimento.ps1

# 2. (Opcional) aplica as migrations e gera 5.000 pedidos determinísticos
dotnet run --project src/HubPedidos.Api -- --semear 5000 --somente-preparar-banco

# 3. API (em Development as migrations são aplicadas na inicialização)
dotnet run --project src/HubPedidos.Api --launch-profile http

# Testes (cada teste usa um banco SQLite em memória próprio)
dotnet test

# Benchmarks do roteiro (Release, fora do depurador)
dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*DetalheCompilado*" "*Paginacao*"
```

Migrations (ferramenta local `dotnet-ef` no manifesto `dotnet-tools.json`):

```bash
dotnet tool restore
dotnet ef migrations add <Nome> --project src/HubPedidos.Infrastructure --startup-project src/HubPedidos.Infrastructure --output-dir Persistencia/Migracoes
dotnet ef migrations script --project src/HubPedidos.Infrastructure --startup-project src/HubPedidos.Infrastructure
```

Sem a connection string a API não sobe, e a mensagem de erro indica o script. Em outro
ambiente, use a variável `ConnectionStrings__HubPedidos`.

## Arquivos produzidos

| Arquivo | Conteúdo |
|---|---|
| `src/HubPedidos.Infrastructure/` | Novo projeto: EF Core 10.0.13 + SQLite |
| `Persistencia/Modelos/PedidoRegistro.cs` | Modelos relacionais: pedido, item e endereço (tipo complexo) |
| `Persistencia/ContextoDePedidos.cs` | Mapeamento, índices, convenções; contexto alternativo com endereço em JSON |
| `Persistencia/Migracoes/*_Inicial.cs` | Migration revisada ([SQL gerado](resultados/roteiro-03-migracao-inicial.sql)) |
| `Persistencia/RepositorioDePedidos.cs` | Inclusão e cancelamento com tracking (e alternativa sem tracking) |
| `Persistencia/MapeadorDePersistencia.cs` | Domínio ↔ modelo relacional; centavos ↔ reais |
| `Persistencia/ContadorDeComandos.cs` | Interceptor que registra cada comando (associa SQL ao LINQ, conta roundtrips) |
| `Persistencia/SemeadorDePedidos.cs` | Massa de dados determinística |
| `Consultas/ConsultasDePedidos.cs` | Painel (projeção + paginação), cursor, detalhe normal e compilado, relatório |
| `Domain/Pedidos/EnderecoEntrega.cs` | Value object de endereço; `Pedido` ganhou `Situacao`, `CriadoEmUtc` e `Cancelar()` |
| `Api/Endpoints/PedidosEndpoints.cs` | `POST` persiste (201); `GET` painel, cursor, detalhe, relatório; `POST .../cancelamento` |
| `scripts/configurar-desenvolvimento.ps1` | Grava a connection string em User Secrets |
| `benchmarks/.../Consultas/ConsultasBenchmarks.cs` | Comum × compilada; deslocamento × cursor |
| `tests/.../Persistencia/` | 15 testes novos com banco efêmero |

## Decisões técnicas

1. **Modelo relacional separado do domínio.** A infraestrutura grava `PedidoRegistro` e
   `ItemPedidoRegistro`; o repositório converte de e para a entidade `Pedido`, como o livro
   faz na seção 8.1. O domínio continua sem referência ao EF Core (teste arquitetural do
   Roteiro 1), e decisões físicas (chave numérica, centavos, colunas) não vazam para ele.
2. **Chave e ordenação.** `Numero` (INTEGER, rowid do SQLite) é a chave primária usada
   nas junções e no desempate da ordenação. `Id` (Guid v7) é o identificador público, com
   índice único. Ordenar por `CriadoEmUtc DESC, Numero DESC` torna a paginação
   determinística mesmo com datas iguais (teste com 5 pedidos no mesmo instante).
3. **Tipos que o SQLite não tem.** O SQLite não tem `decimal` nem `DateTimeOffset`
   nativos; o EF não consegue ordenar, comparar nem somar esses tipos no banco. Dinheiro
   fica em **centavos (INTEGER)** e datas em **`DateTime` UTC** (TEXT ISO, que ordena
   corretamente), com um conversor que marca o valor lido como UTC. Enums são gravados
   como texto (legíveis e estáveis se a ordem do enum mudar).
4. **Revisão da migration (etapa 1.2).** Conferi os nomes (`Pedidos`, `ItensPedido`,
   `Entrega_*`), as chaves (`Numero` autoincremento, FK com cascade), a nulabilidade (as
   colunas do endereço são anuláveis porque o tipo complexo é opcional; as demais são
   `NOT NULL`) e os índices, escolhidos a partir das consultas reais: `(CriadoEmUtc, Numero)`
   para ordenação e cursor, `ClienteId`, `Categoria` e `Id` (único). O teste
   `Esquema_e_reproduzivel...` falha se o modelo mudar sem uma nova migration
   (`HasPendingModelChanges`).
5. **Logging sem dados sensíveis (etapa 1.3).** Em Development, a categoria
   `Microsoft.EntityFrameworkCore.Database.Command` registra o SQL; como
   `EnableSensitiveDataLogging` não é usado, os parâmetros aparecem como `'?'`. O SQL
   capturado de cada consulta está em [`resultados/roteiro-03-sql.md`](resultados/roteiro-03-sql.md).
6. **Painel (etapa 2).** O `PedidoResumo` é **projetado no banco**: o SQL lê só as 8
   colunas usadas, a soma das quantidades vira uma subconsulta `COALESCE(SUM(...))` e a
   página é `LIMIT/OFFSET` parametrizado. Uma projeção para DTO não materializa entidades,
   então não há o que rastrear; `AsNoTracking` só é usado onde entidades são lidas (relatório
   com `Include`). O total vem de um `COUNT(*)` com o mesmo filtro (2 comandos por página).
7. **Tracking (etapa 2.3).** O cancelamento lê o pedido **com tracking**, pois o mesmo
   contexto vai alterá-lo e salvá-lo. O domínio decide se a transição é válida, e o EF gera
   um `UPDATE` só da coluna `Situacao`. A alternativa sem tracking (`ExecuteUpdateAsync`)
   está documentada e testada: um único comando, sem materializar nada, mas com a regra de
   negócio duplicada na cláusula `WHERE`. Por isso não é a usada pela API.
8. **Carregamento relacionado (etapa 2.4).** No relatório paginado de pedidos e itens:
   projeção = 1 comando só com as colunas necessárias; `Include` + `AsNoTracking` =
   1 comando com todas as colunas; projeção + `AsSplitQuery` = 2 comandos, sem repetir o
   cabeçalho a cada item. Nenhuma estratégia faz N+1 (teste com orçamento de comandos). A
   projeção é o padrão.
9. **Consulta compilada (etapa 3).** Escolhi o detalhe por `Id`: consulta estável, muito
   repetida e com um parâmetro escalar. A medição (tabela abaixo) mostrou **0,72× o tempo
   e 0,81× a alocação**, com desvio pequeno. Adotei a versão compilada na rota
   `GET /api/pedidos/{id}`. A versão comum continua no código, e um teste garante que as
   duas devolvem o mesmo resultado. Restrições conhecidas: um único modelo por delegate;
   a consulta não pode mudar de forma em tempo de execução; o delegate compilado não
   recebe `CancellationToken` aqui. Em um banco remoto o ganho seria uma fração menor do
   total, porque a rede domina.
10. **Parâmetros (relatório).** Todos os valores do usuário entram como variáveis locais
    capturadas, que o EF transforma em `@parâmetros`. Um teste compara o SQL de duas
    consultas com valores diferentes (inclusive uma tentativa de injeção) e verifica que o
    texto é idêntico.

### Relacional × JSON (etapa 4)

O EF Core 10 permite mapear tipos complexos para JSON, e a documentação indica suporte no
provedor SQLite. Verifiquei isso no próprio provedor (EF Core 10.0.13 + SQLite), com o
contexto alternativo `ContextoDePedidosComEnderecoJson`:

| Aspecto | Endereço em colunas (adotado) | Endereço em JSON (alternativa) |
|---|---|---|
| Esquema | 4 colunas `Entrega_*` anuláveis | 1 coluna `EnderecoJson` (TEXT) |
| Filtro por cidade | `WHERE "Entrega_Cidade" = @cidade` (indexável normalmente) | `WHERE "EnderecoJson" ->> 'Cidade' = @cidade` (exigiria índice de expressão) |
| Atualização de um membro | `UPDATE` só da coluna alterada | `UPDATE` reescreve o documento inteiro (sem `json_set` parcial) |
| Tracking | Tipo complexo rastreado como parte do pedido | Idem; a mudança de um membro marca o documento todo |
| Portabilidade | SQL padrão em qualquer provedor | Depende dos operadores JSON de cada banco |
| Evolução do esquema | Nova coluna = nova migration | Membro novo não exige migration, mas documentos antigos ficam sem ele |

**Decisão:** colunas. A cidade e a UF são filtradas no painel, a estrutura é pequena e
estável, e a atualização parcial é mais barata. JSON valeria para dados opcionais lidos e
gravados como um bloco (como as "informações complementares" do livro).

## Medições (BenchmarkDotNet 0.15.8, SQLite em memória, 5.000 pedidos)

| Consulta | Média | Razão | Alocado |
|---|---:|---:|---:|
| Detalhe — comum (baseline) | 123,76 µs ± 2,16 | 1,00 | 55,53 KB |
| Detalhe — `EF.CompileAsyncQuery` | 88,83 µs ± 1,64 | 0,72 | 44,94 KB |
| Página 41 de 100 por deslocamento (+ COUNT) | 444,7 µs | 1,00 | 140,26 KB |
| Mesma página por cursor (extensão) | 399,2 µs | 0,90 | 140,97 KB |

Com 5.000 linhas em memória, o cursor ganhou ~10% na página profunda. A diferença cresce
com o volume (o deslocamento percorre e descarta as linhas anteriores), mas o cursor não
permite saltar para uma página arbitrária nem informa o total. O painel mantém o
deslocamento, e o cursor fica disponível em `/api/pedidos/cursor` para rolagem contínua.
Relatórios completos: [`resultados/roteiro-03-benchmarks.md`](resultados/roteiro-03-benchmarks.md).

## Extensões ("Aprimoramentos")

- **Paginação por deslocamento × por cursor** (`ListarResumosPorCursorAsync`, rota
  `/api/pedidos/cursor`), com teste de equivalência de sequência e benchmark.
- **Teste que falha ao detectar comandos SQL em excesso** (`OrcamentoDeComandos`): o
  teste `Orcamento_de_comandos_detecta_um_N_mais_1` mostra que uma implementação ingênua
  (1 + 10 consultas) é barrada.

## Testes obrigatórios do relatório

| Teste obrigatório (Relatório, Atividade 3) | Onde está |
|---|---|
| Consulta paginada retorna somente colunas e linhas necessárias | `PersistenciaDePedidosTestes.Consulta_paginada_retorna_somente_colunas_e_linhas_necessarias` (páginas 20/20/5, SQL com LIMIT/OFFSET e sem colunas não exibidas) |
| Atualização pelo mesmo DbContext usa tracking; outra estratégia exige anexação explícita testada | `Atualizacao_usa_consulta_com_tracking_e_altera_somente_a_situacao` e `Alternativa_sem_tracking_atualiza_com_um_comando_explicito` |
| Nenhuma consulta N+1 no relatório de pedidos e itens | `Relatorio_de_pedidos_e_itens_nao_tem_N_mais_1` (3 estratégias, 1 ou 2 comandos) e `Orcamento_de_comandos_detecta_um_N_mais_1` |
| Parâmetros do usuário não alteram a forma da expressão | `Parametros_do_usuario_nao_alteram_a_forma_do_comando` |
| Mapeamento complexo/JSON testado apenas no provedor/versão registrados | `MapeamentoJsonTestes` (`Assert.Inconclusive` fora de EF Core 10 + SQLite); filtro relacional × JSON e atualização |
| Comparação normal × compilada | `Consulta_compilada_produz_o_mesmo_resultado_que_a_comum` + benchmark `DetalheCompiladoBenchmarks` |

## Evidências de execução manual

Com 5.000 pedidos gerados pelo semeador, na API em execução:

- `GET /api/pedidos?pagina=2&tamanho=3&categoria=Padrao&valorMinimo=100` → 3 itens,
  `total: 1598`, `totalDePaginas: 533`, ordenados por data decrescente.
- `POST /api/pedidos` com endereço → `201 Created` e `Location: /api/pedidos/{id}`;
  `GET` do `Location` → detalhe com itens e endereço.
- `POST /api/pedidos/{id}/cancelamento` → `204`; repetido → `409`.
- `GET /api/pedidos/cursor?tamanho=2` → `proximoCursor`, usado em `?apos=` para a página seguinte.
- `GET /api/relatorios/pedidos?estrategia=Dividida` → pedidos com itens.
- Log de SQL: `[Parameters=[@minimoEmCentavos='?' (DbType = Int64), @cliente='?' ...]]`, sem valores.
- **Defeito encontrado e corrigido na execução:** `GET /api/pedidos/nao-e-guid` devolvia
  500. Em Development, as Minimal APIs lançam `BadHttpRequestException` (400) quando o
  binding falha, e o `UseExceptionHandler` convertia a exceção em 500. Agora o
  `StatusCodeSelector` preserva o 400. (A cobertura automatizada via HTTP entra com o
  `WebApplicationFactory` no Roteiro 8.)

## Versões utilizadas

| Item | Versão |
|---|---|
| EF Core / provedor | Microsoft.EntityFrameworkCore.Sqlite 10.0.13 (ferramenta `dotnet-ef` 10.0.13) |
| SQLite | Versão embutida em Microsoft.Data.Sqlite (SQLitePCLRaw), via EF Core 10.0.13 |
| SDK / runtime | .NET 10.0.401 / 10.0.12 |
| Testes | 127 aprovados (15 novos neste roteiro) |
