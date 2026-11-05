using System.Text;
using HubPedidos.Application.Importacao;
using HubPedidos.Tests.Importacao.Apoio;

namespace HubPedidos.Tests.Importacao;

[TestClass]
public sealed class ImportadoresDeLoteTestes
{
    private static readonly OpcoesDeImportacao OpcoesPequenas = new()
    {
        TamanhoMaximoDaLinha = 64,
        TamanhoDoBuffer = 128,
        TamanhoMaximoTotal = 4096,
    };

    [TestMethod]
    public async Task Baseline_e_fluxo_sao_equivalentes_com_terminadores_BOM_e_ultima_linha_sem_quebra()
    {
        const string conteudo =
            "﻿joão;Sul;Normal;CAFÉ-ç:2:3.40\r\n" +
            "c2;Sudeste;Urgente;A:1:1,B:2:2\n" +
            "\n" +
            "c3;Norte;Alta;A:1\n" +
            "c4;Nordeste;Normal;Z:5:9.99";

        var baseline = await ImportarComBaselineAsync(conteudo);
        foreach (var importador in ImportadoresIncrementais())
        {
            // Um byte por leitura: separadores e caracteres multibyte atravessam leituras.
            var incremental = await ImportarComFluxoAsync(importador, StreamObservavel.DeTexto(conteudo, maximoPorLeitura: 1));

            Assert.HasCount(baseline.Count, incremental, importador.EstrategiaDeBuffer);
            for (var i = 0; i < baseline.Count; i++)
            {
                ParsersDeLinhaTestes.AssertLinhasEquivalentes(baseline[i], incremental[i]);
            }
        }

        Assert.HasCount(5, baseline);
        Assert.AreEqual("joão", baseline[0].Pedido!.ClienteId);
        Assert.AreEqual(MensagensDeImportacao.LinhaVazia, baseline[2].Erro);
    }

    [TestMethod]
    public async Task Entrada_vazia_nao_produz_linhas()
    {
        Assert.IsEmpty(await ImportarComBaselineAsync(string.Empty));
        foreach (var importador in ImportadoresIncrementais())
        {
            Assert.IsEmpty(await ImportarComFluxoAsync(importador, StreamObservavel.DeTexto(string.Empty)));
        }
    }

    [TestMethod]
    public async Task Entrada_incompleta_gera_erro_somente_na_linha_truncada()
    {
        const string conteudo = "c1;Sul;Normal;A:1:1\nc2;Sul";

        foreach (var importador in ImportadoresIncrementais())
        {
            var linhas = await ImportarComFluxoAsync(importador, StreamObservavel.DeTexto(conteudo, maximoPorLeitura: 3));

            Assert.HasCount(2, linhas);
            Assert.IsTrue(linhas[0].Valida);
            Assert.AreEqual(MensagensDeImportacao.QuantidadeDeCampos, linhas[1].Erro);
        }
    }

    [TestMethod]
    public async Task Linha_acima_do_limite_vira_erro_e_a_importacao_continua()
    {
        var media = $"c;Sul;Normal;{new string('A', 80)}:1:1";   // > 64 bytes, < buffer de 128
        var enorme = $"c;Sul;Normal;{new string('B', 400)}:1:1"; // maior que o próprio buffer
        var conteudo = $"{media}\nc;Sul;Normal;OK:1:1\n{enorme}\nc;Sul;Normal;OK2:1:1\n";
        var esperado = MensagensDeImportacao.LinhaLonga(64);

        var baseline = await new ImportadorDeLotesInicial(OpcoesPequenas).ImportarAsync(StreamObservavel.DeTexto(conteudo));
        foreach (var importador in ImportadoresIncrementais(OpcoesPequenas))
        {
            var incremental = await ImportarComFluxoAsync(importador, StreamObservavel.DeTexto(conteudo, maximoPorLeitura: 17));

            CollectionAssert.AreEqual(
                new[] { esperado, null, esperado, null },
                incremental.Select(l => l.Erro).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, incremental.Select(l => l.Numero).ToArray());
        }

        CollectionAssert.AreEqual(new[] { esperado, null, esperado, null }, baseline.Select(l => l.Erro).ToArray());
    }

    [TestMethod]
    public async Task Lote_acima_do_limite_total_e_interrompido_cedo_no_fluxo()
    {
        var conteudo = string.Concat(Enumerable.Repeat("c;Sul;Normal;A:1:1\n", 500)); // ~9.500 bytes > 4.096

        await Assert.ThrowsExactlyAsync<LimiteDeImportacaoExcedidoException>(
            () => new ImportadorDeLotesInicial(OpcoesPequenas).ImportarAsync(StreamObservavel.DeTexto(conteudo)));

        foreach (var importador in ImportadoresIncrementais(OpcoesPequenas))
        {
            var origem = StreamObservavel.DeTexto(conteudo);
            await Assert.ThrowsExactlyAsync<LimiteDeImportacaoExcedidoException>(
                () => importador.ImportarAsync(origem, (_, _) => ValueTask.CompletedTask));

            // O fluxo parou logo depois do limite: no máximo um buffer além dele foi lido.
            Assert.IsLessThanOrEqualTo(OpcoesPequenas.TamanhoMaximoTotal + OpcoesPequenas.TamanhoDoBuffer, origem.BytesLidos);
        }
    }

    [TestMethod]
    public async Task UTF8_invalido_afeta_somente_a_propria_linha_no_fluxo()
    {
        byte[] conteudo = [.. "c1;Sul;Normal;A:1:1\nc2;Sul;Normal;B"u8, 0xC3, 0x28, .. ":1:1\nc3;Sul;Normal;C:1:1"u8];

        var linhas = await ImportarComFluxoAsync(new ImportadorDeLotesPorFluxo(), new StreamObservavel(conteudo, maximoPorLeitura: 2));

        CollectionAssert.AreEqual(
            new[] { null, MensagensDeImportacao.Utf8Invalido, null },
            linhas.Select(l => l.Erro).ToArray());

        // A linha de base decodifica o lote inteiro de uma vez: um byte inválido derruba tudo.
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(
            () => new ImportadorDeLotesInicial().ImportarAsync(new StreamObservavel(conteudo)));
    }

    [TestMethod]
    public async Task Somente_a_fatia_valida_do_buffer_alugado_e_processada()
    {
        // O pool entrega arrays maiores que o pedido, cheios de '#' e '\n'. Se o importador
        // lesse além da região válida, surgiriam linhas falsas no resultado.
        var pool = new ArrayPoolObservavel(capacidadeExtra: 64);
        var importador = new ImportadorDeLotesPorFluxo(OpcoesPequenas, pool);

        var linhas = await ImportarComFluxoAsync(importador, StreamObservavel.DeTexto("c;Sul;Normal;A:1:1\nc;Sul;Normal;B:1:1", maximoPorLeitura: 5));

        Assert.HasCount(2, linhas);
        Assert.IsTrue(linhas.All(l => l.Valida));
        Assert.AreEqual(OpcoesPequenas.TamanhoDoBuffer, pool.TamanhoSolicitado);
    }

    [TestMethod]
    public async Task Buffer_volta_ao_pool_em_sucesso_excecao_e_cancelamento()
    {
        const string conteudo = "c;Sul;Normal;A:1:1\nc;Sul;Normal;B:1:1\nc;Sul;Normal;C:1:1\n";

        // Sucesso
        var poolSucesso = new ArrayPoolObservavel();
        await new ImportadorDeLotesPorFluxo(OpcoesPequenas, poolSucesso)
            .ImportarAsync(StreamObservavel.DeTexto(conteudo), (_, _) => ValueTask.CompletedTask);
        Assert.AreSame(poolSucesso.Alugado, poolSucesso.Devolvido);

        // Exceção dentro do callback (consumidor falhou)
        var poolFalha = new ArrayPoolObservavel();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new ImportadorDeLotesPorFluxo(OpcoesPequenas, poolFalha).ImportarAsync(
                StreamObservavel.DeTexto(conteudo),
                (_, _) => throw new InvalidOperationException("consumidor falhou")));
        Assert.AreSame(poolFalha.Alugado, poolFalha.Devolvido);
        Assert.AreEqual(1, poolFalha.Devolucoes);

        // Cancelamento no meio do fluxo
        var poolCancelado = new ArrayPoolObservavel();
        using var cancelamento = new CancellationTokenSource();
        var entregues = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ImportadorDeLotesPorFluxo(OpcoesPequenas, poolCancelado).ImportarAsync(
                StreamObservavel.DeTexto(conteudo, maximoPorLeitura: 4),
                (_, _) =>
                {
                    if (++entregues == 1)
                    {
                        cancelamento.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                cancelamento.Token));
        Assert.AreEqual(1, entregues);
        Assert.AreSame(poolCancelado.Alugado, poolCancelado.Devolvido);
    }

    [TestMethod]
    public async Task MemoryPool_descarta_o_proprietario_em_sucesso_e_falha()
    {
        var pool = new MemoryPoolObservavel();
        var importador = ImportadorDeLotesPorFluxo.ComMemoryPool(pool, OpcoesPequenas);

        await importador.ImportarAsync(StreamObservavel.DeTexto("c;Sul;Normal;A:1:1"), (_, _) => ValueTask.CompletedTask);
        Assert.IsTrue(pool.Ultimo!.Descartado);

        await Assert.ThrowsExactlyAsync<LimiteDeImportacaoExcedidoException>(() =>
            importador.ImportarAsync(StreamObservavel.DeTexto(new string('x', 5000)), (_, _) => ValueTask.CompletedTask));
        Assert.IsTrue(pool.Ultimo!.Descartado);
    }

    [TestMethod]
    public async Task Regiao_usada_e_limpa_antes_da_devolucao_quando_configurado()
    {
        const string conteudo = "cliente-sigiloso;Sul;Normal;A:1:1\n";
        var tamanhoUsado = Encoding.UTF8.GetByteCount(conteudo);

        var comLimpeza = new ArrayPoolObservavel();
        await new ImportadorDeLotesPorFluxo(OpcoesPequenas, comLimpeza)
            .ImportarAsync(StreamObservavel.DeTexto(conteudo), (_, _) => ValueTask.CompletedTask);

        var semLimpeza = new ArrayPoolObservavel();
        var opcoesSemLimpeza = new OpcoesDeImportacao
        {
            TamanhoMaximoDaLinha = 64,
            TamanhoDoBuffer = 128,
            TamanhoMaximoTotal = 4096,
            LimparAoDevolver = false,
        };
        await new ImportadorDeLotesPorFluxo(opcoesSemLimpeza, semLimpeza)
            .ImportarAsync(StreamObservavel.DeTexto(conteudo), (_, _) => ValueTask.CompletedTask);

        Assert.IsTrue(comLimpeza.ConteudoNaDevolucao!.AsSpan(0, tamanhoUsado).IndexOfAnyExcept((byte)0) < 0);
        StringAssert.Contains(Encoding.UTF8.GetString(semLimpeza.ConteudoNaDevolucao!, 0, tamanhoUsado), "cliente-sigiloso");
    }

    [TestMethod]
    public async Task Fluxo_grande_e_processado_sem_carregar_o_corpo_inteiro()
    {
        const int quantidade = 100_000; // ~4 MB gerados sob demanda
        var origem = new StreamGeradorDeLote(quantidade);
        var importador = new ImportadorDeLotesPorFluxo(); // buffer padrão de 16 KB
        long bytesNaPrimeiraEntrega = -1;
        long bytesNaMetade = -1;
        var validas = 0;

        var total = await importador.ImportarAsync(origem, (linha, _) =>
        {
            if (linha.Valida)
            {
                validas++;
            }

            if (linha.Numero == 1)
            {
                bytesNaPrimeiraEntrega = origem.BytesLidos;
            }
            else if (linha.Numero == quantidade / 2)
            {
                bytesNaMetade = origem.BytesLidos;
            }

            return ValueTask.CompletedTask; // o consumidor não retém nada
        });

        Assert.AreEqual(quantidade, total);
        Assert.AreEqual(quantidade, validas);
        // O primeiro resultado sai depois de ler apenas um bloco, e a leitura avança junto com o consumo.
        Assert.IsLessThanOrEqualTo(importador.Opcoes.TamanhoDoBuffer, bytesNaPrimeiraEntrega);
        Assert.IsLessThan(origem.BytesLidos, bytesNaMetade);
        // Nenhuma leitura pediu mais do que o tamanho do buffer.
        Assert.IsLessThanOrEqualTo(importador.Opcoes.TamanhoDoBuffer, origem.MaiorLeituraSolicitada);
    }

    private static IEnumerable<ImportadorDeLotesPorFluxo> ImportadoresIncrementais(OpcoesDeImportacao? opcoes = null)
    {
        yield return new ImportadorDeLotesPorFluxo(opcoes);
        yield return ImportadorDeLotesPorFluxo.ComMemoryPool(opcoes: opcoes);
    }

    private static async Task<IReadOnlyList<LinhaImportada>> ImportarComBaselineAsync(string conteudo) =>
        await new ImportadorDeLotesInicial().ImportarAsync(StreamObservavel.DeTexto(conteudo));

    private static async Task<List<LinhaImportada>> ImportarComFluxoAsync(ImportadorDeLotesPorFluxo importador, Stream origem)
    {
        var linhas = new List<LinhaImportada>();
        await importador.ImportarAsync(origem, (linha, _) =>
        {
            linhas.Add(linha);
            return ValueTask.CompletedTask;
        });
        return linhas;
    }
}
