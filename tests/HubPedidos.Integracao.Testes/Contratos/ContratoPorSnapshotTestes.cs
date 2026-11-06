using System.Net;
using System.Net.Http.Headers;
using HubPedidos.Integracao.Testes.Infraestrutura;

namespace HubPedidos.Integracao.Testes.Contratos;

/// <summary>
/// Extensão: o corpo inteiro das respostas v1 e v2 é comparado com um snapshot aprovado.
/// Complementa VersionamentoTestes (nomes e tipos dos campos): aqui qualquer mudança de
/// valor, formato ou campo novo também aparece, e precisa de aprovação explícita.
/// </summary>
[TestClass]
public sealed class ContratoPorSnapshotTestes
{
    private static readonly string[] CamposVariaveis = ["id", "criadoEmUtc", "criadoEm", "previsaoDeEntrega"];

    [TestMethod]
    public async Task Resposta_v1_corresponde_ao_snapshot_aprovado()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var id = await fabrica.CriarPedidoAsync("cliente-snapshot", precoUnitario: 12.50m, quantidade: 4);

        var resposta = await fabrica.CriarCliente().GetAsync($"/api/v1/pedidos/{id}");

        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode);
        Snapshot.Verificar(await resposta.Content.ReadAsStringAsync(), "pedido-v1", CamposVariaveis);
    }

    [TestMethod]
    public async Task Resposta_v2_e_304_correspondem_ao_contrato_aprovado()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var id = await fabrica.CriarPedidoAsync("cliente-001", precoUnitario: 12.50m, quantidade: 4);
        var http = await fabrica.CriarClienteAutenticadoAsync("ana");

        var resposta = await http.GetAsync($"/api/v2/pedidos/{id}");
        using var condicional = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{id}");
        condicional.Headers.IfNoneMatch.Add(resposta.Headers.ETag!);
        var naoModificado = await http.SendAsync(condicional);

        Snapshot.Verificar(await resposta.Content.ReadAsStringAsync(), "pedido-v2", CamposVariaveis);
        // A resposta condicional também faz parte do contrato protegido por integração.
        Assert.AreEqual(HttpStatusCode.NotModified, naoModificado.StatusCode);
        Assert.IsEmpty(await naoModificado.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(new CacheControlHeaderValue { Private = true, NoCache = true }, naoModificado.Headers.CacheControl);
    }
}
