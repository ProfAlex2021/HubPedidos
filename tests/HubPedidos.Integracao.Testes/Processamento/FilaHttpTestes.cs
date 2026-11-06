using System.Net;
using System.Net.Http.Json;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Integracao.Testes.Infraestrutura;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HubPedidos.Integracao.Testes.Processamento;

/// <summary>
/// Produtor da fila pela API (Etapa 3, item 3): o consumidor real do worker continua
/// rodando, mas o processamento é o falso observável. Os testes esperam SINAIS com
/// limite de segurança, nunca um atraso fixo.
/// </summary>
[TestClass]
public sealed class FilaHttpTestes
{
    /// <summary>Limite de segurança: só é atingido se algo estiver errado.</summary>
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task Solicitacao_aceita_chega_ao_consumidor_e_o_resultado_aparece_nas_metricas()
    {
        var processamento = new ProcessamentoObservavel();
        await using var fabrica = CriarFabrica(processamento);
        var id = await fabrica.CriarPedidoAsync("cliente-001");
        var http = fabrica.CriarCliente();

        var resposta = await http.PostAsync($"/api/pedidos/{id}/processamento", null);
        var concluido = await processamento.Concluidos.ReadAsync().AsTask().WaitAsync(Limite);

        Assert.AreEqual(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.AreEqual($"/api/pedidos/{id}", resposta.Headers.Location?.OriginalString);
        Assert.AreEqual(PedidoId.Parse(id, null), concluido);

        // O worker registra a métrica logo depois de o processamento terminar; espera o
        // sinal de que ninguém está mais em andamento (estado observável, não um sleep).
        var metricas = await AguardarAsync(http, m => m.Processados == 1 && m.EmAndamento == 0);
        Assert.AreEqual(1, metricas.Aceitos);
        Assert.AreEqual(0, metricas.Rejeitados);
    }

    [TestMethod]
    public async Task Fila_cheia_devolve_429_com_Retry_After_e_nao_perde_os_itens_aceitos()
    {
        // Capacidade 1 e um consumidor, com a porta FECHADA: estado montado passo a passo.
        var processamento = new ProcessamentoObservavel(portaAberta: false);
        await using var fabrica = CriarFabrica(processamento, new Dictionary<string, string?>
        {
            ["Fila:Capacidade"] = "1",
            ["Fila:Consumidores"] = "1",
        });
        var ids = new[]
        {
            await fabrica.CriarPedidoAsync("cliente-001"),
            await fabrica.CriarPedidoAsync("cliente-001"),
            await fabrica.CriarPedidoAsync("cliente-001"),
        };
        var http = fabrica.CriarCliente();

        // 1º: aceito e retirado pelo consumidor (que fica preso na porta).
        Assert.AreEqual(HttpStatusCode.Accepted, (await http.PostAsync($"/api/pedidos/{ids[0]}/processamento", null)).StatusCode);
        await processamento.Iniciados.ReadAsync().AsTask().WaitAsync(Limite);
        // 2º: ocupa a única vaga da fila.
        Assert.AreEqual(HttpStatusCode.Accepted, (await http.PostAsync($"/api/pedidos/{ids[1]}/processamento", null)).StatusCode);
        // 3º: fila cheia -> 429 imediato, com Retry-After.
        var rejeitado = await http.PostAsync($"/api/pedidos/{ids[2]}/processamento", null);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejeitado.StatusCode, await rejeitado.Content.ReadAsStringAsync());
        Assert.AreEqual(TimeSpan.FromSeconds(1), rejeitado.Headers.RetryAfter?.Delta);

        processamento.Liberar();
        var concluidos = new[]
        {
            await processamento.Concluidos.ReadAsync().AsTask().WaitAsync(Limite),
            await processamento.Concluidos.ReadAsync().AsTask().WaitAsync(Limite),
        };
        CollectionAssert.AreEquivalent(ids[..2].Select(i => PedidoId.Parse(i, null)).ToArray(), concluidos);

        var metricas = await AguardarAsync(http, m => m.Processados == 2 && m.EmAndamento == 0);
        Assert.AreEqual(2, metricas.Aceitos);
        Assert.AreEqual(1, metricas.Rejeitados);
    }

    [TestMethod]
    public async Task Pedido_inexistente_nao_entra_na_fila()
    {
        var processamento = new ProcessamentoObservavel();
        await using var fabrica = CriarFabrica(processamento);
        var http = fabrica.CriarCliente();

        var resposta = await http.PostAsync($"/api/pedidos/{Guid.CreateVersion7()}/processamento", null);
        var metricas = await http.GetFromJsonAsync<InstantaneoDasMetricas>("/api/processamento/metricas");

        Assert.AreEqual(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.AreEqual(0, metricas!.Aceitos);
        Assert.IsFalse(processamento.Iniciados.TryRead(out _));
    }

    private static FabricaDaAplicacao CriarFabrica(ProcessamentoObservavel processamento, IReadOnlyDictionary<string, string?>? configuracao = null) =>
        new(servicos =>
            {
                servicos.RemoveAll<IProcessamentoDePedido>();
                servicos.AddSingleton<IProcessamentoDePedido>(processamento);
            },
            configuracao);

    /// <summary>Consulta as métricas até a condição valer (sondagem curta com limite, não um atraso fixo).</summary>
    private static async Task<InstantaneoDasMetricas> AguardarAsync(HttpClient http, Func<InstantaneoDasMetricas, bool> condicao)
    {
        var prazo = DateTime.UtcNow + Limite;
        while (true)
        {
            var metricas = (await http.GetFromJsonAsync<InstantaneoDasMetricas>("/api/processamento/metricas"))!;
            if (condicao(metricas))
            {
                return metricas;
            }

            if (DateTime.UtcNow > prazo)
            {
                Assert.Fail($"As métricas não chegaram ao estado esperado em {Limite.TotalSeconds} s. Última leitura: {metricas}");
            }

            await Task.Delay(10); // intervalo curto entre leituras; o teste termina assim que a condição vale
        }
    }
}
