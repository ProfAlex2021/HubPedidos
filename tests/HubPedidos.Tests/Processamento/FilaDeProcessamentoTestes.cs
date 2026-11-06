using System.Threading.Channels;
using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Tests.Persistencia;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Tests.Processamento;

/// <summary>Política de fila cheia: espera, descarte e rejeição explícita.</summary>
[TestClass]
public sealed class FilaDeProcessamentoTestes
{
    [TestMethod]
    public async Task No_modo_Wait_o_produtor_aguarda_e_TryWrite_rejeita()
    {
        var metricas = new MetricasDoProcessamento();
        var fila = new FilaDeProcessamento(new OpcoesDaFila { Capacidade = 2 }, metricas);

        Assert.AreEqual(ResultadoDaAdmissao.Aceito, fila.TentarEnfileirar(PedidoId.Novo()));
        Assert.AreEqual(ResultadoDaAdmissao.Aceito, fila.TentarEnfileirar(PedidoId.Novo()));
        Assert.AreEqual(ResultadoDaAdmissao.Rejeitado, fila.TentarEnfileirar(PedidoId.Novo()));

        // O produtor interno espera por espaço em vez de descartar.
        var produtorQueEspera = fila.EnfileirarAsync(PedidoId.Novo(), CancellationToken.None).AsTask();
        Assert.IsFalse(produtorQueEspera.IsCompleted);
        Assert.AreEqual(2, fila.Profundidade);

        Assert.IsTrue(fila.Leitor.TryRead(out _)); // um consumidor libera espaço
        await produtorQueEspera.WaitAsync(TimeSpan.FromSeconds(5));

        var instantaneo = metricas.Instantaneo(fila.Profundidade, 0);
        Assert.AreEqual(3, instantaneo.Aceitos);
        Assert.AreEqual(1, instantaneo.Rejeitados);
        Assert.AreEqual(0, instantaneo.Descartados);
    }

    [TestMethod]
    [DataRow(BoundedChannelFullMode.DropOldest, new[] { 2, 3 }, DisplayName = "DropOldest descarta o mais antigo")]
    [DataRow(BoundedChannelFullMode.DropNewest, new[] { 1, 3 }, DisplayName = "DropNewest descarta o mais novo da fila")]
    [DataRow(BoundedChannelFullMode.DropWrite, new[] { 1, 2 }, DisplayName = "DropWrite descarta o item sendo escrito")]
    public void Modos_de_descarte_seguem_a_politica_e_contam_descartes(BoundedChannelFullMode modo, int[] restantes)
    {
        var metricas = new MetricasDoProcessamento();
        var fila = new FilaDeProcessamento(new OpcoesDaFila { Capacidade = 2, ModoQuandoCheia = modo }, metricas);
        var ids = new[] { PedidoId.Novo(), PedidoId.Novo(), PedidoId.Novo() };

        foreach (var id in ids)
        {
            // Nos modos de descarte TryWrite sempre "aceita": a perda acontece pela política.
            Assert.AreEqual(ResultadoDaAdmissao.Aceito, fila.TentarEnfileirar(id));
        }

        var lidos = new List<PedidoId>();
        while (fila.Leitor.TryRead(out var job))
        {
            lidos.Add(job.PedidoId);
        }

        CollectionAssert.AreEqual(restantes.Select(n => ids[n - 1]).ToArray(), lidos.ToArray());
        var instantaneo = metricas.Instantaneo(0, 0);
        Assert.AreEqual(1, instantaneo.Descartados);
        Assert.AreEqual(0, instantaneo.Rejeitados);
    }

    [TestMethod]
    public async Task Cancelamento_do_produtor_nao_admite_o_item()
    {
        var metricas = new MetricasDoProcessamento();
        var fila = new FilaDeProcessamento(new OpcoesDaFila { Capacidade = 1 }, metricas);
        fila.TentarEnfileirar(PedidoId.Novo());
        using var cancelamento = new CancellationTokenSource();

        var espera = fila.EnfileirarAsync(PedidoId.Novo(), cancelamento.Token).AsTask();
        await cancelamento.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => espera);
        Assert.AreEqual(1, metricas.Instantaneo(fila.Profundidade, 0).Aceitos);
        Assert.AreEqual(1, fila.Profundidade);
    }

    [TestMethod]
    public async Task Endpoint_produtor_responde_202_429_com_Retry_After_404_e_503()
    {
        using var banco = new BancoDeTeste();
        var pedido = await banco.InserirAsync("c", DateTime.UtcNow);
        await using var contexto = banco.CriarContexto();
        var fila = new FilaDeProcessamento(new OpcoesDaFila { Capacidade = 1 }, new MetricasDoProcessamento());

        async Task<(IResult Resultado, HttpResponse Resposta)> SolicitarAsync(PedidoId id)
        {
            var http = new DefaultHttpContext();
            var resultado = await ProcessamentoEndpoints.SolicitarProcessamentoAsync(id, fila, contexto, http.Response, CancellationToken.None);
            return (resultado.Result, http.Response);
        }

        var aceito = await SolicitarAsync(pedido.Id);
        var cheio = await SolicitarAsync(pedido.Id);
        var inexistente = await SolicitarAsync(PedidoId.Novo());
        fila.Concluir();
        var encerrando = await SolicitarAsync(pedido.Id);

        Assert.IsInstanceOfType<Accepted>(aceito.Resultado);
        var rejeicao = Assert.IsInstanceOfType<ProblemHttpResult>(cheio.Resultado);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, rejeicao.StatusCode);
        Assert.AreEqual("1", cheio.Resposta.Headers.RetryAfter.ToString());
        Assert.IsInstanceOfType<NotFound>(inexistente.Resultado);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, Assert.IsInstanceOfType<ProblemHttpResult>(encerrando.Resultado).StatusCode);
    }

    [TestMethod]
    public void Opcoes_invalidas_sao_rejeitadas()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcoesDaFila { Capacidade = 0 }.Validar());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcoesDaFila { Consumidores = 0 }.Validar());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcoesDaFila { LimiteDeAcessoAoBanco = 0 }.Validar());
    }
}
