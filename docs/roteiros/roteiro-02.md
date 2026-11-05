# Roteiro 2 — Span<T>, Memory<T>, pooling e caminhos críticos limitados por E/S e medidos

**Objetivo:** reduzir cópias e alocações no recebimento de lotes de pedidos, comprovando
ganhos e custos com benchmarks reproduzíveis.

## Como executar

```bash
dotnet test
dotnet run --project src/HubPedidos.Api --launch-profile http
curl -X POST http://localhost:5080/api/pedidos/lotes -H "Content-Type: text/plain; charset=utf-8" --data-binary @src/HubPedidos.Api/exemplos/lote-exemplo.txt
```

Benchmarks (sempre em **Release** e **fora do depurador**; no Visual Studio use
*Depurar > Iniciar sem Depuração* com a configuração Release):

```bash
dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*"
```

Para executar só uma classe: `--filter "*ParsingDeLinha*"`. Os relatórios ficam em
`BenchmarkDotNet.Artifacts/` (ignorado pelo Git); os resultados desta execução foram
copiados para [`resultados/roteiro-02-benchmarks.md`](resultados/roteiro-02-benchmarks.md).

## Formato do lote

Uma linha por pedido, em UTF-8, com campos separados por `;` e itens separados por `,`:

```
cliente;regiao;prioridade;sku:quantidade:preco,sku:quantidade:preco
cliente-001;Sudeste;Urgente;TECLADO:2:150.00,MOUSE:1:80.00
```

Espaços e tabulações nas bordas de cada parte são ignorados. O preço usa ponto decimal
(cultura invariável). A última linha pode não ter terminador; LF e CRLF são aceitos;
um BOM UTF-8 inicial é ignorado. O parser verifica só a **sintaxe**: o conteúdo vira um
`CriarPedidoRequest` e passa pelas mesmas validações do Roteiro 1 (`MapeadorDePedido`).

## Arquivos produzidos

| Arquivo | Conteúdo |
|---|---|
| `Application/Importacao/ParserDeLinhaInicial.cs` | Baseline com `Split`/`Trim`/`TryParse(string)` |
| `Application/Importacao/ParserDeLinhaComSpan.cs` | `ReadOnlySpan<char>`, `Split(Span<Range>)`, sem substrings temporárias |
| `Application/Importacao/ParserDeLinhaUtf8.cs` | Extensão: parsing direto de `ReadOnlySpan<byte>` UTF-8 |
| `Application/Importacao/ImportadorDeLotesInicial.cs` | Baseline: `ReadToEndAsync` + lista completa |
| `Application/Importacao/ImportadorDeLotesPorFluxo.cs` | Leitura incremental com `ArrayPool<byte>` ou `MemoryPool<byte>`, limites, cancelamento |
| `Application/Importacao/GravadorDeResultados.cs` | NDJSON com escrita/flush agrupados |
| `Application/Importacao/ProcessadorDeLotes.cs` | Liga importador, caso de uso do Roteiro 1 e gravador |
| `Application/Importacao/OpcoesDeImportacao.cs` | Limites de linha, total e buffer; política de limpeza |
| `Api/Endpoints/PedidosEndpoints.cs` | `POST /api/pedidos/lotes` (corpo lido como stream) |
| `Api/exemplos/lote-exemplo.txt` | Lote usado na requisição do `.http` |
| `benchmarks/HubPedidos.Benchmarks/` | Projeto BenchmarkDotNet separado (3 classes) |
| `tests/HubPedidos.Tests/Importacao/` | 35 testes novos + streams/pools observáveis |

## Decisões técnicas

1. **Onde estavam as cópias (etapa 2.1).** No baseline, cada linha gera o array de
   `Split(';')`, o array de `Split(',')`, um array de `Split(':')` por item e uma string
   para cada campo e para cada `Trim`. Só o cliente, a região, a prioridade e os SKUs
   precisam sobreviver ao método, pois compõem o DTO; o resto é temporário.
2. **Spans só no trecho síncrono (etapa 2.3).** Os parsers recebem `ReadOnlySpan` e
   devolvem um `LinhaImportada` com strings próprias; nenhum span é guardado em campo nem
   atravessa `await`. No importador, o buffer fica em `Memory<byte>` durante o
   `ReadAsync`, e o span é criado apenas na chamada síncrona que interpreta cada linha.
3. **Separação de linhas em bytes.** `\n`, `;`, `,` e `:` são ASCII e nunca aparecem
   dentro de uma sequência multibyte UTF-8, por isso é seguro procurá-los byte a byte.
   Um caractere como `ç` dividido entre duas leituras fica no buffer até a linha
   completar (teste com 1 byte por leitura).
4. **Escolha do pool (etapa 3.1).** O padrão é `ArrayPool<byte>.Shared`, porque
   `Stream.ReadAsync` aceita `Memory<byte>` criado de um array e o contrato
   alugar/devolver é simples. A extensão com `MemoryPool<byte>` entrega um
   `IMemoryOwner<byte>` e deixa explícito o dono do buffer (`Dispose`). Os dois modos
   compartilham o mesmo laço.
5. **Devolução garantida (etapa 3.2).** O aluguel acontece antes do `try`, e a devolução
   (`Return` ou `Dispose`) fica no `finally`: roda em sucesso, exceção do consumidor,
   limite excedido e cancelamento. Os testes usam pools observáveis.
6. **Somente a fatia válida (etapa 3.3).** O array alugado pode ser maior que o pedido e
   conter lixo. O importador trabalha só com os primeiros `TamanhoDoBuffer` bytes e só
   lê `[inicio..fim)`. O teste usa um pool que devolve arrays cheios de `#` e `\n`: se o
   código lesse além da região válida, apareceriam linhas falsas.
7. **Limpeza (etapa 3.4).** As linhas contêm o identificador do cliente (dado pessoal).
   Por isso `LimparAoDevolver = true` zera **apenas a região usada** antes da devolução,
   uma vez por importação. O custo é proporcional ao buffer (16 KB), não ao lote.
8. **Fluxo assíncrono (etapa 4).** Leitura incremental com limite total
   (`TamanhoMaximoTotal`, verificado a cada bloco), limite por linha (linha longa vira
   erro só daquela linha), `CancellationToken` propagado à leitura, ao callback e à
   escrita, e callback aguardado antes da próxima linha (backpressure: nenhuma fila de
   resultados). A resposta NDJSON é escrita em lotes (`ResultadosPorEscrita = 100`):
   o mesmo conteúdo com 1% das operações de flush.
9. **Diferença de comportamento intencional.** Com UTF-8 inválido, o baseline falha o
   lote inteiro (o `StreamReader` estrito decodifica tudo de uma vez); a versão
   incremental marca só a linha afetada. A equivalência funcional vale para todas as
   entradas UTF-8 válidas, inclusive linhas com erro de formato.
10. **Erro depois do 200.** No endpoint, a resposta começa a ser enviada antes do fim do
    lote; um limite excedido no meio não pode mais virar 4xx. O resumo final informa
    `interrompido: true` e o motivo.

## Extensões ("Aprimoramentos")

- **`MemoryPool<T>` com propriedade explícita do buffer**
  (`ImportadorDeLotesPorFluxo.ComMemoryPool`), com teste de descarte em sucesso e falha.
- **Parsing UTF-8 direto × desserialização intermediária** (`ParserDeLinhaUtf8`
  comparado ao parser de `char` e ao baseline no benchmark `ParsingDeLinhaBenchmarks`).

## Testes obrigatórios do relatório

| Teste obrigatório (Relatório, Atividade 2) | Onde está |
|---|---|
| Equivalência funcional entre baseline e versão otimizada | `ParsersDeLinhaTestes.Versoes_produzem_o_mesmo_DTO...` e `..._a_mesma_mensagem...` (3 parsers); `ImportadoresDeLoteTestes.Baseline_e_fluxo_sao_equivalentes...` (baseline × ArrayPool × MemoryPool) |
| Entrada vazia, incompleta, acima do limite e com UTF-8 multibyte | `Entrada_vazia_nao_produz_linhas`, `Entrada_incompleta_gera_erro...`, `Linha_acima_do_limite...`, `Lote_acima_do_limite_total...`, `Baseline_e_fluxo...` (`joão`, `CAFÉ-ç`, 1 byte por leitura), `UTF8_invalido_afeta_somente_a_propria_linha...` |
| Buffer devolvido ao pool mesmo com exceção ou cancelamento | `Buffer_volta_ao_pool_em_sucesso_excecao_e_cancelamento`, `MemoryPool_descarta_o_proprietario...`, `Regiao_usada_e_limpa_antes_da_devolucao...` |
| Benchmark em Release, fora do depurador, com baseline explícita | Passo manual: comando acima; `[Benchmark(Baseline = true)]` nas 3 classes; resultados em `resultados/roteiro-02-benchmarks.md` |
| Fluxo grande sem carregar o corpo completo | `Fluxo_grande_e_processado_sem_carregar_o_corpo_inteiro` (100 mil linhas geradas sob demanda) |
| Escrita agrupada | `ProcessadorDeLotesTestes.Gravador_por_item_e_por_lote...`, `Dispose_descarrega_o_lote_incompleto` |

## Medições (BenchmarkDotNet 0.15.8)

Ambiente: Windows 11 10.0.26300, Intel Core i5-13420H (12 núcleos lógicos), SDK .NET
10.0.401, runtime 10.0.12, RyuJIT x64, Release, fora do depurador. Job: 1 lançamento,
3 aquecimentos, 8 iterações. Dados determinísticos gerados em `GlobalSetup` (fora da
região medida): linhas com 3 itens, inclusive caracteres multibyte (`ÇAFÉ`).
Relatórios completos: [`resultados/roteiro-02-benchmarks.md`](resultados/roteiro-02-benchmarks.md).

### Parsing de 1.000 linhas

| Versão | Média | Razão | Alocado | Razão alocada |
|---|---:|---:|---:|---:|
| Inicial (Split/string) — baseline | 357,5 µs | 1,00 | 1.193,12 KB | 1,00 |
| `ReadOnlySpan<char>` | 348,5 µs | 0,98 | 429,69 KB | 0,36 |
| `ReadOnlySpan<byte>` UTF-8 | 294,0 µs | 0,82 | 429,69 KB | 0,36 |

**Ajuste guiado pela medição.** Na primeira execução, a versão UTF-8 alocava 554,69 KB,
mais que a de `char`. A diferença (~125 KB por 1.000 linhas) correspondia a dois objetos
por linha: passar o grupo de métodos `Utf8Estrito.GetString` como `Func` cria um delegate
novo a cada chamada. Com o delegate guardado em um campo estático, a alocação caiu para
429,69 KB. Hipótese confirmada pela segunda medição; as duas tabelas estão no arquivo de
resultados.

### Importação completa (MemoryStream, sem latência de E/S)

| Linhas | Versão | Média | Razão | Alocado | Gen1/Gen2 |
|---:|---|---:|---:|---:|---|
| 100 | Inicial (ReadToEnd + lista) | 45,21 µs | 1,00 | 172,45 KB | sim/não |
| 100 | Fluxo + ArrayPool | 32,57 µs | 0,72 | 55,76 KB | não/não |
| 100 | Fluxo + MemoryPool | 31,39 µs | 0,69 | 55,78 KB | não/não |
| 10.000 | Inicial (ReadToEnd + lista) | 20.073 µs | 1,00 | 16.919 KB | sim/sim |
| 10.000 | Fluxo + ArrayPool | 3.311 µs | 0,17 | 5.547 KB | não/não |
| 10.000 | Fluxo + MemoryPool | 3.254 µs | 0,16 | 5.547 KB | não/não |

Os contratos são diferentes: o baseline devolve a lista completa, enquanto o fluxo
entrega cada linha a um consumidor que só soma. Parte da diferença vem dessa retenção,
e é justamente ela que leva o baseline a coletas de geração 1 e 2 no lote grande.
A memória restante do fluxo (~0,55 KB por linha) são os DTOs e strings que o contrato
exige.

### Escrita NDJSON de 10.000 resultados em arquivo

| Resultados por flush | Média | Alocado |
|---:|---:|---:|
| 1 (flush por item) | 111,87 ms | 2.504,04 KB |
| 100 (lote) | 3,54 ms | 37,97 KB |

Mesmo conteúdo, ~32× mais rápido: cada flush é uma chamada de E/S ao sistema operacional.

### Tabela de decisão

| Otimização | Ganho medido | Custo de manutenção | Decisão |
|---|---|---|---|
| `ReadOnlySpan<char>` no parser | −64% de alocação; tempo igual (dentro do erro) | Baixo (`Split(Span<Range>)`, sem estado) | **Aceita**, mas como etapa intermediária |
| Parser UTF-8 direto (extensão) | −64% de alocação e −18% de tempo | Médio (separadores em bytes, decodificação estrita, BOM) | **Aceita**: é o parser usado pelo importador |
| Importação incremental + `ArrayPool` | −68% de memória e −83% de tempo no lote de 10 mil; sem Gen1/Gen2; primeiro resultado após 1 bloco | Médio (compactação do buffer, linha longa, devolução no `finally`) | **Aceita** |
| `MemoryPool` (extensão) | Igual ao `ArrayPool` (dentro do erro) | Baixo (só a forma de aluguel muda) | **Mantida como opção**; o padrão continua `ArrayPool` pela simplicidade |
| Flush em lote (100) | ~32× mais rápido, −98% de alocação | Baixo (contador + `ConcluirAsync`) | **Aceita**; a resposta chega em blocos de 100 linhas |
| Limpeza do buffer na devolução | Não medida isoladamente (≤16 KB por importação) | Baixo | **Aceita** por segurança (dado pessoal) |
| Cache de nomes conhecidos (região/prioridade) | Faz parte dos −64% | Baixo | **Aceita** |

Nenhuma otimização foi revertida. A versão baseline continua no código como referência
dos testes de equivalência e dos benchmarks.

## Evidências de execução manual

API executada com `dotnet run` (Debug):

- `POST /api/pedidos/lotes` com `exemplos/lote-exemplo.txt` → 7 linhas NDJSON: 5 `aceito`
  (Expressa, Padrão, Prioritária com `joão-ç`/`CAFÉ`, Volumosa e AltoValor), 1 `invalido`
  (quantidade 0), 1 `erro-de-formato` (3 campos), e o resumo `linhas 7, aceitas 5`.
- Lote de 200.000 linhas (9,67 MB) → `aceitas: 200000` em 1,14 s; 50.000 linhas em 0,30 s.
  O pico do working set do processo ficou em **74,4 MB**, incluindo o runtime e a API.
- Lote de 12,05 MB (acima do limite de 10 MB) → HTTP 200 com o resumo
  `interrompido: true` e `"O lote excede o limite de 10485760 bytes."` depois de 217.282 linhas.

## Versões utilizadas

| Item | Versão |
|---|---|
| SDK / runtime | .NET 10.0.401 / 10.0.12 |
| BenchmarkDotNet | 0.15.8 |
| MSTest | 4.5.1 |
| Testes | 112 aprovados (35 novos neste roteiro) |
