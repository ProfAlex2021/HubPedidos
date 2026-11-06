using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HubPedidos.Integracao.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Integracao.Testes.Pedidos;

/// <summary>
/// Contrato crítico: criar e consultar um pedido atravessando rota, binding, DI,
/// serviço de aplicação, repositório EF Core e SQLite (Etapa 3, item 1).
/// </summary>
[TestClass]
public sealed class CriacaoEConsultaTestes
{
    [TestMethod]
    public async Task Pedido_criado_por_HTTP_e_persistido_e_lido_de_volta_pela_mesma_rota()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var http = fabrica.CriarCliente();

        var criacao = await http.PostAsJsonAsync("/api/pedidos", new
        {
            clienteId = "cliente-integracao",
            regiao = "Sul",
            prioridade = "Normal",
            itens = new[]
            {
                new { sku = "CADERNO", quantidade = 3, precoUnitario = 12.50m },
                new { sku = "CANETA", quantidade = 2, precoUnitario = 2.25m },
            },
        });
        using var criado = JsonDocument.Parse(await criacao.Content.ReadAsStringAsync());
        var id = criado.RootElement.GetProperty("id").GetString()!;

        Assert.AreEqual(HttpStatusCode.Created, criacao.StatusCode, await criacao.Content.ReadAsStringAsync());
        Assert.AreEqual($"/api/pedidos/{id}", criacao.Headers.Location?.OriginalString);

        // A consulta usa o Location devolvido: o contrato é seguido como um cliente seguiria.
        var consulta = await http.GetAsync(criacao.Headers.Location);
        using var lido = JsonDocument.Parse(await consulta.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.OK, consulta.StatusCode);
        Assert.AreEqual("cliente-integracao", lido.RootElement.GetProperty("clienteId").GetString());
        Assert.AreEqual(42m, lido.RootElement.GetProperty("valorTotal").GetDecimal()); // 3 x 12,50 + 2 x 2,25
        Assert.AreEqual(2, lido.RootElement.GetProperty("itens").GetArrayLength());
        Assert.AreEqual("Recebido", lido.RootElement.GetProperty("situacao").GetString());

        // Inspeção do banco: exatamente um pedido e dois itens gravados (não foi só memória do endpoint).
        Assert.AreEqual(1, await fabrica.NoBancoAsync(contexto => contexto.Pedidos.CountAsync()));
        Assert.AreEqual(2, await fabrica.NoBancoAsync(contexto => contexto.Itens.CountAsync()));
    }

    [TestMethod]
    public async Task Pedido_invalido_e_recusado_com_problema_legivel_e_nada_e_gravado()
    {
        await using var fabrica = new FabricaDaAplicacao();

        var resposta = await fabrica.CriarCliente().PostAsJsonAsync("/api/pedidos", new
        {
            clienteId = "",
            regiao = "Sul",
            prioridade = "Normal",
            itens = Array.Empty<object>(),
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.AreEqual("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(0, await fabrica.NoBancoAsync(contexto => contexto.Pedidos.CountAsync()));
    }

    [TestMethod]
    public async Task Identificador_mal_formado_vira_400_e_inexistente_vira_404()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var http = fabrica.CriarCliente();

        Assert.AreEqual(HttpStatusCode.BadRequest, (await http.GetAsync("/api/pedidos/nao-e-um-guid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/pedidos/{Guid.CreateVersion7()}")).StatusCode);
    }
}
