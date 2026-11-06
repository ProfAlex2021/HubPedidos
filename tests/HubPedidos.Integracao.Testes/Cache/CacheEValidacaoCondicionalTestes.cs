using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HubPedidos.Integracao.Testes.Infraestrutura;

namespace HubPedidos.Integracao.Testes.Cache;

/// <summary>Roteiro 7: ETag, If-None-Match/304, If-Match/412 e output cache.</summary>
[TestClass]
public sealed class CacheEValidacaoCondicionalTestes
{
    [TestMethod]
    public async Task ETag_e_estavel_e_If_None_Match_valido_devolve_304_sem_corpo()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var ana = await fabrica.CriarClienteAutenticadoAsync("ana");
        var id = await fabrica.CriarPedidoAsync("cliente-001");

        var primeira = await ana.GetAsync($"/api/v2/pedidos/{id}");
        var segunda = await ana.GetAsync($"/api/v2/pedidos/{id}");
        var etag = primeira.Headers.ETag!;

        Assert.AreEqual(HttpStatusCode.OK, primeira.StatusCode);
        Assert.IsFalse(etag.IsWeak);
        StringAssert.StartsWith(etag.Tag, "\"sha256-");
        Assert.AreEqual(etag, segunda.Headers.ETag);           // estável: representação não mudou
        Assert.IsTrue(primeira.Headers.CacheControl!.Private);
        Assert.IsTrue(primeira.Headers.CacheControl.NoCache);

        foreach (var validador in new[] { etag, new EntityTagHeaderValue(etag.Tag, isWeak: true), EntityTagHeaderValue.Any })
        {
            using var condicional = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{id}");
            condicional.Headers.IfNoneMatch.Add(validador);
            var resposta = await ana.SendAsync(condicional);

            Assert.AreEqual(HttpStatusCode.NotModified, resposta.StatusCode, validador.ToString());
            Assert.IsEmpty(await resposta.Content.ReadAsByteArrayAsync());
            Assert.AreEqual(etag, resposta.Headers.ETag);       // metadados preservados no 304
            Assert.IsTrue(resposta.Headers.CacheControl!.Private);
        }
    }

    [TestMethod]
    public async Task ETag_muda_quando_a_representacao_muda_e_valor_obsoleto_recebe_200()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var ana = await fabrica.CriarClienteAutenticadoAsync("ana");
        var id = await fabrica.CriarPedidoAsync("cliente-001");
        var etagAntiga = (await ana.GetAsync($"/api/v2/pedidos/{id}")).Headers.ETag!;

        (await ana.PostAsync($"/api/pedidos/{id}/cancelamento", null)).EnsureSuccessStatusCode();

        using var obsoleta = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{id}");
        obsoleta.Headers.IfNoneMatch.Add(etagAntiga);
        var resposta = await ana.SendAsync(obsoleta);
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode);
        Assert.AreNotEqual(etagAntiga, resposta.Headers.ETag);
        Assert.AreEqual("cancelado", corpo.RootElement.GetProperty("situacao").GetString());

        using var invalida = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{id}");
        invalida.Headers.TryAddWithoutValidation("If-None-Match", "\"qualquer-coisa\"");
        Assert.AreEqual(HttpStatusCode.OK, (await ana.SendAsync(invalida)).StatusCode);
    }

    [TestMethod]
    public async Task Autorizacao_vem_antes_da_ETag_e_respostas_privadas_nao_sao_compartilhadas()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var ana = await fabrica.CriarClienteAutenticadoAsync("ana");
        var bruno = await fabrica.CriarClienteAutenticadoAsync("bruno");
        var id = await fabrica.CriarPedidoAsync("cliente-001");
        var etagDaAna = (await ana.GetAsync($"/api/v2/pedidos/{id}")).Headers.ETag!;

        // Bruno, mesmo com a ETag correta, não recebe 304 nem o corpo: 404.
        using var condicional = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{id}");
        condicional.Headers.IfNoneMatch.Add(etagDaAna);
        Assert.AreEqual(HttpStatusCode.NotFound, (await bruno.SendAsync(condicional)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await bruno.GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await fabrica.CriarCliente().GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await ana.GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
    }

    [TestMethod]
    public async Task If_Match_obsoleto_devolve_412_e_nao_altera_o_pedido()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var ana = await fabrica.CriarClienteAutenticadoAsync("ana");
        var id = await fabrica.CriarPedidoAsync("cliente-001");
        var etagAtual = (await ana.GetAsync($"/api/v2/pedidos/{id}")).Headers.ETag!;

        using var obsoleto = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/pedidos/{id}/cancelamento");
        obsoleto.Headers.IfMatch.Add(new EntityTagHeaderValue("\"sha256-versao-antiga\""));
        var recusado = await ana.SendAsync(obsoleto);

        Assert.AreEqual(HttpStatusCode.PreconditionFailed, recusado.StatusCode);
        Assert.AreEqual(etagAtual, recusado.Headers.ETag);
        Assert.AreEqual(etagAtual, (await ana.GetAsync($"/api/v2/pedidos/{id}")).Headers.ETag); // nada mudou

        using var atual = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/pedidos/{id}/cancelamento");
        atual.Headers.IfMatch.Add(etagAtual);
        Assert.AreEqual(HttpStatusCode.NoContent, (await ana.SendAsync(atual)).StatusCode);
    }

    [TestMethod]
    public async Task Output_cache_reutiliza_a_leitura_publica_varia_por_query_e_e_invalidado_por_tag()
    {
        await using var fabrica = new FabricaDaAplicacao();
        var anonimo = fabrica.CriarCliente();
        await fabrica.CriarPedidoAsync("cliente-001");

        var geracoes = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            geracoes.Add((await anonimo.GetAsync("/api/estatisticas/categorias?dias=30")).Headers.GetValues("X-Gerado-Em").Single());
        }

        var outraVariacao = (await anonimo.GetAsync("/api/estatisticas/categorias?dias=7")).Headers.GetValues("X-Gerado-Em").Single();

        Assert.HasCount(1, geracoes.Distinct().ToArray()); // 1 execução real + 19 acertos de cache
        Assert.AreNotEqual(geracoes[0], outraVariacao);    // "dias" faz parte da chave

        // Uma mudança de pedido invalida a tag: a próxima leitura é recalculada e inclui o novo pedido.
        await fabrica.CriarPedidoAsync("cliente-002");
        var depois = await anonimo.GetAsync("/api/estatisticas/categorias?dias=30");
        using var corpo = JsonDocument.Parse(await depois.Content.ReadAsStringAsync());

        Assert.AreNotEqual(geracoes[0], depois.Headers.GetValues("X-Gerado-Em").Single());
        Assert.AreEqual(2, corpo.RootElement.EnumerateArray().Sum(e => e.GetProperty("quantidade").GetInt32()));
    }

    [TestMethod]
    public void Output_cache_e_registrado_depois_de_autenticacao_e_autorizacao()
    {
        // Verificação estática da ordem do pipeline no composition root.
        var program = File.ReadAllText(Path.Combine(Repositorio.Raiz, "src", "HubPedidos.Api", "Program.cs"));
        var autenticacao = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var autorizacao = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        var cache = program.IndexOf("app.UseOutputCache();", StringComparison.Ordinal);

        Assert.IsTrue(autenticacao > 0 && autenticacao < autorizacao && autorizacao < cache, "Ordem esperada: UseAuthentication -> UseAuthorization -> UseOutputCache.");
    }
}
