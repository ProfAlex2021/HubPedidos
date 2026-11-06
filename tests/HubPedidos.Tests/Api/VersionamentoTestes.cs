using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HubPedidos.Tests.Api;

/// <summary>
/// Cliente que representa o front-end publicado ANTES da v2: tem a sua própria cópia
/// do contrato v1 e usa a rota sem versão, como sempre usou.
/// </summary>
internal sealed class ClienteFrontEndV1(HttpClient http)
{
    public async Task<string> CriarAsync(string cliente, decimal preco, int quantidade)
    {
        var resposta = await http.PostAsJsonAsync("/api/pedidos", new
        {
            clienteId = cliente,
            regiao = "Sul",
            prioridade = "Normal",
            itens = new[] { new { sku = "CADERNO", quantidade, precoUnitario = preco } },
        });
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PedidoV1>())!.Id;
    }

    public async Task<PedidoV1?> ObterAsync(string id)
    {
        var resposta = await http.GetAsync($"/api/pedidos/{id}");
        return resposta.StatusCode == HttpStatusCode.NotFound ? null : await resposta.Content.ReadFromJsonAsync<PedidoV1>();
    }

    public async Task CancelarAsync(string id) =>
        (await http.PostAsync($"/api/pedidos/{id}/cancelamento", null)).EnsureSuccessStatusCode();

    /// <summary>O que o front-end v1 conhece (e só isso).</summary>
    public sealed record PedidoV1(string Id, string ClienteId, string Situacao, decimal ValorTotal, int QuantidadeDeItens, List<ItemV1> Itens);

    public sealed record ItemV1(string Sku, int Quantidade, decimal PrecoUnitario, decimal Subtotal);
}

[TestClass]
public sealed class VersionamentoTestes
{
    private static readonly string[] PropriedadesDaV1 =
        ["id", "clienteId", "regiao", "prioridade", "categoria", "motivoDaCategoria", "processador", "prazoEstimadoEmDias",
         "valorTotal", "quantidadeDeItens", "itens", "situacao", "criadoEmUtc", "endereco"];

    private static readonly string[] PropriedadesDaV2 =
        ["id", "cliente", "situacao", "categoria", "valor", "entrega", "previsaoDeEntrega", "criadoEm", "itens"];

    [TestMethod]
    public async Task Cliente_v1_continua_funcionando_na_aplicacao_que_tambem_tem_v2()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var clienteV1 = new ClienteFrontEndV1(fabrica.CreateClient());

        var id = await clienteV1.CriarAsync("cliente-legado", preco: 12.50m, quantidade: 4);
        var lido = await clienteV1.ObterAsync(id);

        Assert.IsNotNull(lido);
        Assert.AreEqual("cliente-legado", lido.ClienteId);
        Assert.AreEqual(50m, lido.ValorTotal);              // mesmo significado: total em reais, número
        Assert.AreEqual(4, lido.QuantidadeDeItens);
        Assert.AreEqual("Recebido", lido.Situacao);         // mesma grafia de sempre
        Assert.AreEqual(50m, lido.Itens.Single().Subtotal);

        await clienteV1.CancelarAsync(id);
        Assert.AreEqual("Cancelado", (await clienteV1.ObterAsync(id))!.Situacao);
        Assert.IsNull(await clienteV1.ObterAsync(Guid.CreateVersion7().ToString()));
    }

    [TestMethod]
    public async Task V1_tem_contrato_estavel_e_cabecalhos_de_ciclo_de_vida()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var http = fabrica.CreateClient();
        var id = await new ClienteFrontEndV1(http).CriarAsync("c", 10m, 1);

        var semVersao = await http.GetAsync($"/api/pedidos/{id}");
        var v1 = await http.GetAsync($"/api/v1/pedidos/{id}");
        using var json = JsonDocument.Parse(await v1.Content.ReadAsStringAsync());

        // Versão ausente segue a política: é a v1, com o mesmo corpo.
        Assert.AreEqual(await semVersao.Content.ReadAsStringAsync(), await v1.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(PropriedadesDaV1, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(JsonValueKind.Number, json.RootElement.GetProperty("valorTotal").ValueKind);
        Assert.AreEqual(JsonValueKind.String, json.RootElement.GetProperty("clienteId").ValueKind);

        Assert.AreEqual("@1793836800", v1.Headers.GetValues("Deprecation").Single()); // 2026-11-05T00:00:00Z
        Assert.AreEqual("Fri, 31 Dec 2027 23:59:59 GMT", v1.Headers.GetValues("Sunset").Single());
        StringAssert.Contains(v1.Headers.GetValues("Link").Single(), $"</api/v2/pedidos/{id}>; rel=\"successor-version\"");
        Assert.AreEqual("1, 2", v1.Headers.GetValues("api-supported-versions").Single());
    }

    [TestMethod]
    public async Task V2_tem_o_contrato_novo_e_a_mesma_semantica()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var http = fabrica.CreateClient();
        var clienteV1 = new ClienteFrontEndV1(http);
        var id = await clienteV1.CriarAsync("cliente-v2", 7.25m, 2);

        var v2 = await http.GetAsync($"/api/v2/pedidos/{id}");
        using var json = JsonDocument.Parse(await v2.Content.ReadAsStringAsync());
        var raiz = json.RootElement;

        Assert.AreEqual(HttpStatusCode.OK, v2.StatusCode);
        CollectionAssert.AreEquivalent(PropriedadesDaV2, raiz.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("cliente-v2", raiz.GetProperty("cliente").GetProperty("id").GetString());
        Assert.AreEqual((await clienteV1.ObterAsync(id))!.ValorTotal, raiz.GetProperty("valor").GetProperty("quantia").GetDecimal());
        Assert.AreEqual("BRL", raiz.GetProperty("valor").GetProperty("moeda").GetString());
        Assert.AreEqual("recebido", raiz.GetProperty("situacao").GetString());
        Assert.AreEqual(JsonValueKind.String, raiz.GetProperty("previsaoDeEntrega").ValueKind);
        Assert.IsFalse(v2.Headers.Contains("Deprecation"));

        await clienteV1.CancelarAsync(id);
        using var cancelado = JsonDocument.Parse(await http.GetStringAsync($"/api/v2/pedidos/{id}"));
        Assert.AreEqual(JsonValueKind.Null, cancelado.RootElement.GetProperty("previsaoDeEntrega").ValueKind); // sem data inventada
        Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/v2/pedidos/{Guid.CreateVersion7()}")).StatusCode);
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(0)]
    [DataRow(99)]
    public async Task Versao_nao_suportada_segue_politica_explicita(int versao)
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var http = fabrica.CreateClient();

        var resposta = await http.GetAsync($"/api/v{versao}/pedidos/{Guid.CreateVersion7()}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.AreEqual("1, 2", resposta.Headers.GetValues("api-supported-versions").Single());
        StringAssert.Contains(await resposta.Content.ReadAsStringAsync(), $"A versão {versao} da API não é suportada");
    }

    [TestMethod]
    public async Task OpenAPI_e_separado_por_versao()
    {
        await using var fabrica = new FabricaComBancoTemporario();
        var http = fabrica.CreateClient();

        var v1 = await http.GetStringAsync("/openapi/v1.json");
        var v2 = await http.GetStringAsync("/openapi/v2.json");

        StringAssert.Contains(v1, "/api/v1/pedidos/{id}");
        StringAssert.Contains(v1, "/api/pedidos");
        Assert.DoesNotContain("/api/v2/pedidos", v1);
        StringAssert.Contains(v2, "/api/v2/pedidos/{id}");
        Assert.DoesNotContain("/api/v1/pedidos", v2);
        Assert.DoesNotContain("/api/v{versao}", v1 + v2); // a rota de recusa não é documentada como operação
    }
}
