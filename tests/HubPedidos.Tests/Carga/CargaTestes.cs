using System.Net;
using HubPedidos.Carga;

namespace HubPedidos.Tests.Carga;

[TestClass]
public sealed class CalculadoraDePercentisTestes
{
    [TestMethod]
    public void Nearest_rank_sem_interpolacao_usa_a_posicao_teto()
    {
        // Mesma amostra do livro-texto: p50 = 5º valor; p95 e p99 = 10º valor.
        var resumo = CalculadoraDePercentis.Calcular([1, 2, 2, 3, 4, 5, 6, 7, 8, 100]);

        Assert.AreEqual(4, resumo.P50);
        Assert.AreEqual(100, resumo.P95);
        Assert.AreEqual(100, resumo.P99);
        Assert.AreEqual(13.8, resumo.Media, 1e-9);
        Assert.AreEqual(10, resumo.Quantidade);
    }

    [TestMethod]
    public void Com_cem_amostras_p99_e_a_99a_posicao_e_a_media_esconde_a_cauda()
    {
        var amostra = Enumerable.Repeat(10.0, 95).Concat([200, 300, 400, 500, 1000]).ToArray();

        var resumo = CalculadoraDePercentis.Calcular(amostra.Reverse());

        Assert.AreEqual(10, resumo.P50);
        Assert.AreEqual(10, resumo.P95);
        Assert.AreEqual(500, resumo.P99);
        Assert.IsLessThan(resumo.P99, resumo.Media); // a média (33,5) não mostra a cauda de 500 ms
    }

    [TestMethod]
    public void Amostra_vazia_ou_invalida_e_rejeitada()
    {
        Assert.Throws<ArgumentException>(() => CalculadoraDePercentis.Calcular([]));
        Assert.Throws<ArgumentException>(() => CalculadoraDePercentis.Calcular([1, double.NaN]));
        Assert.Throws<ArgumentException>(() => CalculadoraDePercentis.Calcular([-1]));
    }
}

[TestClass]
public sealed class GeradorDeCargaTestes
{
    [TestMethod]
    public async Task Aquecimento_fica_fora_e_erros_e_timeouts_sao_registrados()
    {
        // Servidor falso: toda criação falha (500) e uma em cada cinco consultas não responde.
        var manipulador = new ServidorFalso(async (requisicao, numero, token) =>
        {
            if (requisicao.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            if (numero % 5 == 0)
            {
                await Task.Delay(Timeout.Infinite, token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        using var cliente = new HttpClient(manipulador) { BaseAddress = new Uri("http://teste") };
        var opcoes = new OpcoesDaCarga(
            TaxaPorSegundo: 200,
            Aquecimento: TimeSpan.FromMilliseconds(250),
            Medicao: TimeSpan.FromMilliseconds(500),
            ProporcaoDeEscrita: 0.3,
            Timeout: TimeSpan.FromMilliseconds(100));

        var janela = await new GeradorDeCarga(cliente, opcoes).ExecutarAsync();
        var relatorio = RelatorioDeCarga.Consolidar("teste", janela);

        Assert.AreEqual(150, manipulador.Recebidas);       // 50 de aquecimento + 100 medidas
        Assert.HasCount(100, janela.Amostras);              // só a janela de medição é registrada
        var criacao = relatorio.Single(l => l.Rota == GeradorDeCarga.RotaDeCriacao);
        var consulta = relatorio.Single(l => l.Rota == GeradorDeCarga.RotaDeConsulta);
        Assert.AreEqual(30, criacao.Requisicoes);            // mistura determinística de 30% de escrita
        Assert.AreEqual(30, criacao.Erros);
        Assert.IsGreaterThan(0, consulta.Timeouts);
        Assert.AreEqual(consulta.Requisicoes, consulta.Sucessos + consulta.Timeouts);
        Assert.IsTrue(janela.Amostras.Where(a => a.Desfecho == Desfecho.Timeout).All(a => a.LatenciaMs >= 100));
        // Os timeouts entram nos percentis: o p99 da consulta reflete a espera de ~100 ms.
        Assert.IsGreaterThanOrEqualTo(100, consulta.Latencias.P99);
    }

    private sealed class ServidorFalso(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private int _recebidas;

        public int Recebidas => Volatile.Read(ref _recebidas);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, Interlocked.Increment(ref _recebidas), cancellationToken);
    }
}
