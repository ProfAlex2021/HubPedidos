using System.Text;
using System.Text.Json;
using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Importacao;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Processamento;
using HubPedidos.Tests.Importacao.Apoio;
using Microsoft.AspNetCore.Http;

namespace HubPedidos.Tests.Importacao;

[TestClass]
public sealed class ProcessadorDeLotesTestes
{
    private readonly ServicoDeRecepcaoDePedidos _servico = new(CatalogoDeProcessadores.DescobrirPadrao());

    [TestMethod]
    public async Task Gravador_por_item_e_por_lote_produz_o_mesmo_conteudo_com_menos_flushes()
    {
        var (porItem, flushesPorItem) = await GravarAsync(resultadosPorEscrita: 1);
        var (porLote, flushesPorLote) = await GravarAsync(resultadosPorEscrita: 4);

        Assert.AreEqual(porItem, porLote);
        Assert.AreEqual(10, flushesPorItem);
        Assert.AreEqual(3, flushesPorLote); // 4 + 4 + 2 (lote final incompleto)
    }

    [TestMethod]
    public async Task Dispose_descarrega_o_lote_incompleto()
    {
        var destino = new StreamObservavel();
        await using (var gravador = new GravadorDeResultados(destino, resultadosPorEscrita: 50))
        {
            await gravador.GravarAsync(new { linha = 1 });
            Assert.AreEqual(0, destino.Flushes);
        }

        Assert.AreEqual(1, destino.Flushes);
        Assert.AreEqual("{\"linha\":1}\n", destino.ObterTexto());
    }

    [TestMethod]
    public async Task Lote_gera_uma_linha_NDJSON_por_pedido_e_um_resumo()
    {
        const string lote =
            "c1;Sudeste;Urgente;A:1:10\n" +
            "c2;Sul;Normal;A:0:10\n" +
            "c3;Sul;Normal\n" +
            "c4;Norte;Normal;A:60:1\n";

        var saida = new StreamObservavel();
        var resumo = await new ProcessadorDeLotes(new ImportadorDeLotesPorFluxo(), _servico)
            .ProcessarAsync(StreamObservavel.DeTexto(lote, maximoPorLeitura: 7), saida);

        var linhas = saida.ObterTexto().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "aceito", "invalido", "erro-de-formato", "aceito", "resumo" },
            linhas.Select(l => l.TryGetProperty("status", out var s) ? s.GetString() : l.GetProperty("tipo").GetString()).ToArray());
        Assert.AreEqual("Expressa", linhas[0].GetProperty("categoria").GetString());
        Assert.AreEqual("itens[0].quantidade", linhas[1].GetProperty("erros")[0].GetProperty("campo").GetString());
        Assert.AreEqual("Volumosa", linhas[3].GetProperty("categoria").GetString());
        Assert.AreEqual(4, resumo.Linhas);
        Assert.AreEqual(2, resumo.Aceitas);
        Assert.IsFalse(resumo.Interrompido);
    }

    [TestMethod]
    public async Task Lote_acima_do_limite_termina_com_resumo_interrompido()
    {
        var opcoes = new OpcoesDeImportacao { TamanhoMaximoDaLinha = 64, TamanhoDoBuffer = 128, TamanhoMaximoTotal = 256 };
        var lote = string.Concat(Enumerable.Repeat("c;Sul;Normal;A:1:1\n", 50));

        var resumo = await new ProcessadorDeLotes(new ImportadorDeLotesPorFluxo(opcoes), _servico)
            .ProcessarAsync(StreamObservavel.DeTexto(lote), new StreamObservavel());

        Assert.IsTrue(resumo.Interrompido);
        StringAssert.Contains(resumo.Motivo!, "256 bytes");
        Assert.IsLessThan(50, resumo.Linhas);
    }

    [TestMethod]
    public async Task Endpoint_de_lote_le_o_corpo_como_stream_e_responde_NDJSON()
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("c1;Sul;Normal;A:1:10\nc2;Sul;Normal;B:2:5"));
        var corpoDaResposta = new MemoryStream();
        contexto.Response.Body = corpoDaResposta;

        await PedidosEndpoints.ImportarLoteAsync(
            contexto,
            new ProcessadorDeLotes(new ImportadorDeLotesPorFluxo(), _servico),
            CancellationToken.None);

        var texto = Encoding.UTF8.GetString(corpoDaResposta.ToArray());
        Assert.AreEqual(StatusCodes.Status200OK, contexto.Response.StatusCode);
        StringAssert.StartsWith(contexto.Response.ContentType, "application/x-ndjson");
        Assert.HasCount(3, texto.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        StringAssert.Contains(texto, "\"aceitas\":2");
    }

    private static async Task<(string Texto, int Flushes)> GravarAsync(int resultadosPorEscrita)
    {
        var destino = new StreamObservavel();
        var gravador = new GravadorDeResultados(destino, resultadosPorEscrita);
        for (var i = 1; i <= 10; i++)
        {
            await gravador.GravarAsync(new ResultadoDaLinhaDoLote(i, "aceito", $"id-{i}", "Padrao"));
        }

        await gravador.ConcluirAsync();
        await gravador.DisposeAsync();
        return (destino.ObterTexto(), destino.Flushes);
    }
}
