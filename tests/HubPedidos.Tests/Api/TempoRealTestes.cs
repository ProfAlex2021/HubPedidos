using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace HubPedidos.Tests.Api;

/// <summary>Roteiro 7: hub, autorização no servidor, grupos, reconexão e revogação.</summary>
[TestClass]
public sealed class TempoRealTestes
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(10);

    public sealed record EventoRecebido(string PedidoId, string Situacao, long Sequencia, DateTime InstanteUtc);

    [TestMethod]
    public async Task Conexao_sem_autenticacao_e_recusada()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        await using var conexao = fabrica.CriarConexao(token: null);

        await Assert.ThrowsAsync<HttpRequestException>(() => conexao.StartAsync());
    }

    [TestMethod]
    public async Task Evento_chega_so_ao_cliente_autorizado_e_depois_da_mudanca_persistida()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var pedidoDaAna = await CacheEValidacaoCondicionalTestes.CriarPedidoAsync(fabrica, "cliente-001");
        var pedidoDoBruno = await CacheEValidacaoCondicionalTestes.CriarPedidoAsync(fabrica, "cliente-002");
        var (ana, eventosDaAna) = await ConectarAsync(fabrica, "ana");
        var (bruno, eventosDoBruno) = await ConectarAsync(fabrica, "bruno");

        await ana.InvokeAsync("AcompanharPedido", pedidoDaAna);
        await bruno.InvokeAsync("AcompanharPedido", pedidoDoBruno);
        // O grupo não é o mecanismo de segurança: o servidor recusa a entrada no grupo de outro cliente.
        var recusa = await Assert.ThrowsAsync<HubException>(() => bruno.InvokeAsync("AcompanharPedido", pedidoDaAna));
        StringAssert.Contains(recusa.Message, "sem permissão");

        await CancelarAsync(fabrica, pedidoDaAna);
        var evento = await eventosDaAna.ReadAsync().AsTask().WaitAsync(Limite);

        Assert.AreEqual(pedidoDaAna, evento.PedidoId);
        Assert.AreEqual("Cancelado", evento.Situacao);
        // O evento é publicado depois de persistir: a API já mostra o novo estado.
        StringAssert.Contains(await (await fabrica.CriarClienteAutenticadoAsync("ana")).GetStringAsync($"/api/v2/pedidos/{pedidoDaAna}"), "\"cancelado\"");

        // Bruno não recebeu o evento da Ana: o PRIMEIRO evento que chega a ele é o do próprio pedido.
        await CancelarAsync(fabrica, pedidoDoBruno);
        Assert.AreEqual(pedidoDoBruno, (await eventosDoBruno.ReadAsync().AsTask().WaitAsync(Limite)).PedidoId);
    }

    [TestMethod]
    public async Task Reconexao_exige_novo_ingresso_e_estado_perdido_e_recuperado_por_HTTP()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var pedido = await CacheEValidacaoCondicionalTestes.CriarPedidoAsync(fabrica, "cliente-001");
        var outro = await CacheEValidacaoCondicionalTestes.CriarPedidoAsync(fabrica, "cliente-001");
        var (ana, eventos) = await ConectarAsync(fabrica, "ana");
        await ana.InvokeAsync("AcompanharPedido", pedido);
        var etagAntes = (await (await fabrica.CriarClienteAutenticadoAsync("ana")).GetAsync($"/api/v2/pedidos/{pedido}")).Headers.ETag!;

        // Queda: a mudança acontece enquanto a Ana está desconectada (evento perdido).
        await ana.StopAsync();
        await CancelarAsync(fabrica, pedido);

        // Nova conexão (novo ConnectionId). Sem reingressar, nada chega:
        await ana.StartAsync();
        await ana.InvokeAsync("AcompanharPedido", outro); // controle: só este grupo
        await CancelarAsync(fabrica, outro);
        var primeiro = await eventos.ReadAsync().AsTask().WaitAsync(Limite);
        Assert.AreEqual(outro, primeiro.PedidoId); // o evento perdido do "pedido" não foi reenviado

        // Reconciliação: reingressa (servidor revalida) e consulta com a ETag antiga -> 200 com o estado novo.
        await ana.InvokeAsync("AcompanharPedido", pedido);
        var http = await fabrica.CriarClienteAutenticadoAsync("ana");
        using var condicional = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{pedido}");
        condicional.Headers.IfNoneMatch.Add(etagAntes);
        var reconciliado = await http.SendAsync(condicional);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, reconciliado.StatusCode);
        StringAssert.Contains(await reconciliado.Content.ReadAsStringAsync(), "\"cancelado\"");
    }

    [TestMethod]
    public async Task Revogacao_remove_associacoes_e_reconexao_revalida_a_autorizacao()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var pedido1 = await CacheEValidacaoCondicionalTestes.CriarPedidoAsync(fabrica, "cliente-001");
        var pedido2 = await CacheEValidacaoCondicionalTestes.CriarPedidoAsync(fabrica, "cliente-001");
        var (ana, eventos) = await ConectarAsync(fabrica, "ana");
        // Token emitido ANTES da revogação: continua válido criptograficamente, mas a autorização passa a negar.
        var anaComTokenAntigo = await fabrica.CriarClienteAutenticadoAsync("ana");
        var revogacoes = Channel.CreateUnbounded<string>();
        ana.On<string>("AcessoRevogado", motivo => revogacoes.Writer.TryWrite(motivo));
        await ana.InvokeAsync("AcompanharPedido", pedido1);
        await ana.InvokeAsync("AcompanharPedido", pedido2);

        var operador = await fabrica.CriarClienteAutenticadoAsync("operador");
        var removidas = await (await operador.PostAsync("/api/admin/usuarios/ana/revogacao", null)).Content.ReadAsStringAsync();

        Assert.AreEqual("2", removidas); // as duas associações foram desfeitas
        StringAssert.Contains(await revogacoes.Reader.ReadAsync().AsTask().WaitAsync(Limite), "revogado");

        // Depois da revogação: nova entrada no grupo é negada (reconexão revalida) e a API também nega.
        await ana.StopAsync();
        await ana.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("AcompanharPedido", pedido1));
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await anaComTokenAntigo.GetAsync($"/api/v2/pedidos/{pedido1}")).StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized,
            (await fabrica.CreateClient().PostAsJsonAsync("/api/autenticacao/token", new { usuario = "ana", senha = fabrica.Senhas["ana"] })).StatusCode);
        Assert.IsFalse(eventos.TryRead(out _)); // nenhum evento chegou depois da revogação
    }

    [TestMethod]
    public async Task Somente_operador_pode_revogar()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var bruno = await fabrica.CriarClienteAutenticadoAsync("bruno");

        Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, (await bruno.PostAsync("/api/admin/usuarios/ana/revogacao", null)).StatusCode);
    }

    private static async Task<(HubConnection Conexao, ChannelReader<EventoRecebido> Eventos)> ConectarAsync(FabricaComBancoTemporario fabrica, string usuario)
    {
        var conexao = fabrica.CriarConexao(await fabrica.ObterTokenAsync(usuario));
        var canal = Channel.CreateUnbounded<EventoRecebido>();
        conexao.On<EventoRecebido>("PedidoAtualizado", evento => canal.Writer.TryWrite(evento));
        await conexao.StartAsync();
        return (conexao, canal.Reader);
    }

    private static async Task CancelarAsync(FabricaComBancoTemporario fabrica, string pedido) =>
        (await (await fabrica.CriarClienteAutenticadoAsync("operador")).PostAsync($"/api/pedidos/{pedido}/cancelamento", null)).EnsureSuccessStatusCode();
}
