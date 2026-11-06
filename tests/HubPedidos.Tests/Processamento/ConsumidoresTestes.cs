using System.Collections.Concurrent;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Infrastructure.Processamento;
using HubPedidos.Tests.Persistencia;
using HubPedidos.Tests.Processamento.Apoio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests.Processamento;

/// <summary>Consumidores, limite de concorrência, falhas, encerramento e duplicidade.</summary>
[TestClass]
public sealed class ConsumidoresTestes
{
    [TestMethod]
    public async Task Acessos_ao_banco_nunca_excedem_o_limite_definido()
    {
        using var limitador = new LimitadorDeAcessoAoBanco(2);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var falso = new ProcessamentoControlado((_, token) => limitador.ExecutarAsync(async t =>
        {
            await liberar.Task.WaitAsync(t); // segura a permissão até o teste observar o pico
            return ResultadoDoProcessamento.Processado;
        }, token));

        await using var ambiente = AmbienteDoWorker.ComFalso(new OpcoesDaFila { Consumidores = 6, Capacidade = 20 }, falso);
        await ambiente.Worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 12; i++)
        {
            Assert.AreEqual(ResultadoDaAdmissao.Aceito, ambiente.Fila.TentarEnfileirar(PedidoId.Novo()));
        }

        // Seis consumidores disputam, mas só duas operações entram na região do banco.
        await AmbienteDoWorker.EsperarAteAsync(() => falso.Chamadas == 6, "seis consumidores ativos");
        await AmbienteDoWorker.EsperarAteAsync(() => limitador.EmUso == 2, "duas permissões em uso");
        Assert.AreEqual(2, limitador.Pico);

        liberar.SetResult();
        await AmbienteDoWorker.EsperarAteAsync(() => ambiente.Metricas.Contar(ResultadoDoProcessamento.Processado) == 12, "12 processados");
        Assert.AreEqual(2, limitador.Pico);
        Assert.AreEqual(2, limitador.Disponiveis); // nenhuma permissão perdida
        await ambiente.Worker.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Excecao_na_regiao_protegida_devolve_a_permissao()
    {
        using var limitador = new LimitadorDeAcessoAoBanco(1);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            limitador.ExecutarAsync<int>(_ => throw new InvalidOperationException("banco indisponível"), CancellationToken.None));
        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            limitador.ExecutarAsync(_ => Task.FromResult(1), cancelado.Token));

        Assert.AreEqual(1, limitador.Disponiveis);
        Assert.AreEqual(1, await limitador.ExecutarAsync(_ => Task.FromResult(1), CancellationToken.None));
    }

    [TestMethod]
    public async Task Falha_de_um_item_nao_encerra_os_consumidores()
    {
        var ids = Enumerable.Range(0, 10).Select(_ => PedidoId.Novo()).ToArray();
        var falso = new ProcessamentoControlado((id, _) => id == ids[3]
            ? throw new InvalidOperationException("estoque indisponível")
            : Task.FromResult(ResultadoDoProcessamento.Processado));

        await using var ambiente = AmbienteDoWorker.ComFalso(new OpcoesDaFila { Consumidores = 2 }, falso);
        await ambiente.Worker.StartAsync(CancellationToken.None);
        foreach (var id in ids)
        {
            ambiente.Fila.TentarEnfileirar(id);
        }

        await AmbienteDoWorker.EsperarAteAsync(() => falso.Chamadas == 10, "10 itens tentados");
        await ambiente.Worker.StopAsync(CancellationToken.None);

        var metricas = ambiente.Metricas.Instantaneo(ambiente.Fila.Profundidade, 0);
        Assert.AreEqual(9, metricas.Processados);
        Assert.AreEqual(1, metricas.Falhas);
        Assert.IsTrue(ambiente.Worker.Consumidores.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task Encerramento_impede_novos_itens_e_drena_os_aceitos()
    {
        var falso = new ProcessamentoControlado(async (_, token) =>
        {
            await Task.Yield();
            return ResultadoDoProcessamento.Processado;
        });
        await using var ambiente = AmbienteDoWorker.ComFalso(new OpcoesDaFila { Consumidores = 1, Capacidade = 10 }, falso);
        for (var i = 0; i < 5; i++)
        {
            ambiente.Fila.TentarEnfileirar(PedidoId.Novo());
        }

        await ambiente.Worker.StartAsync(CancellationToken.None);
        await ambiente.Worker.StopAsync(CancellationToken.None); // política: drenar

        Assert.AreEqual(ResultadoDaAdmissao.Encerrada, ambiente.Fila.TentarEnfileirar(PedidoId.Novo()));
        Assert.AreEqual(5, ambiente.Metricas.Contar(ResultadoDoProcessamento.Processado));
        Assert.AreEqual(0, ambiente.Fila.Profundidade);
    }

    [TestMethod]
    public async Task Encerramento_forcado_interrompe_e_deixa_pendentes_identificados()
    {
        var falso = new ProcessamentoControlado(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token); // trabalho que só termina se cancelado
            return ResultadoDoProcessamento.Processado;
        });
        await using var ambiente = AmbienteDoWorker.ComFalso(new OpcoesDaFila { Consumidores = 1, Capacidade = 10 }, falso);
        await ambiente.Worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 3; i++)
        {
            ambiente.Fila.TentarEnfileirar(PedidoId.Novo());
        }

        await AmbienteDoWorker.EsperarAteAsync(() => falso.Chamadas == 1, "primeiro item em andamento");
        using var prazo = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await ambiente.Worker.StopAsync(prazo.Token);

        Assert.IsTrue(ambiente.Worker.Consumidores.IsCompleted);
        Assert.AreEqual(0, ambiente.Metricas.Contar(ResultadoDoProcessamento.Processado));
        Assert.AreEqual(2, ambiente.Fila.Profundidade); // pendentes visíveis; no banco seguem "Recebido"
    }

    [TestMethod]
    public async Task Pedido_enfileirado_duas_vezes_nao_e_processado_em_paralelo()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processados = new ConcurrentBag<PedidoId>();
        var falso = new ProcessamentoControlado(async (id, token) =>
        {
            await liberar.Task.WaitAsync(token);
            processados.Add(id);
            return ResultadoDoProcessamento.Processado;
        });
        await using var ambiente = AmbienteDoWorker.ComFalso(new OpcoesDaFila { Consumidores = 2 }, falso);
        await ambiente.Worker.StartAsync(CancellationToken.None);
        var id = PedidoId.Novo();

        ambiente.Fila.TentarEnfileirar(id);
        ambiente.Fila.TentarEnfileirar(id);
        await AmbienteDoWorker.EsperarAteAsync(
            () => ambiente.Metricas.Contar(ResultadoDoProcessamento.IgnoradoPorIdempotencia) == 1, "duplicata ignorada");
        liberar.SetResult();
        await ambiente.Worker.StopAsync(CancellationToken.None);

        Assert.AreEqual(1, falso.Chamadas);
        CollectionAssert.AreEqual(new[] { id }, processados.ToArray());
    }

    [TestMethod]
    public async Task Idempotencia_persistida_impede_segundo_processamento_e_respeita_cancelamento()
    {
        using var banco = new BancoDeTeste();
        var aProcessar = await banco.InserirAsync("a", DateTime.UtcNow);
        var cancelado = await banco.InserirAsync("b", DateTime.UtcNow);
        await using (var contexto = banco.CriarContexto())
        {
            await new RepositorioDePedidos(contexto).CancelarAsync(cancelado.Id);
        }

        using var limitador = new LimitadorDeAcessoAoBanco(1);
        var opcoes = new OpcoesDaFila { DuracaoSimuladaEmMs = 0 };
        async Task<ResultadoDoProcessamento> ProcessarAsync(PedidoId id)
        {
            await using var contexto = banco.CriarContexto();
            return await new ProcessamentoDePedidoEfCore(contexto, limitador, opcoes, TimeProvider.System).ProcessarAsync(id, CancellationToken.None);
        }

        Assert.AreEqual(ResultadoDoProcessamento.Processado, await ProcessarAsync(aProcessar.Id));
        Assert.AreEqual(ResultadoDoProcessamento.IgnoradoPorIdempotencia, await ProcessarAsync(aProcessar.Id));
        Assert.AreEqual(ResultadoDoProcessamento.IgnoradoPorIdempotencia, await ProcessarAsync(cancelado.Id));
        Assert.AreEqual(ResultadoDoProcessamento.NaoEncontrado, await ProcessarAsync(PedidoId.Novo()));

        await using var leitura = banco.CriarContexto();
        var registro = await leitura.Pedidos.SingleAsync(p => p.Id == aProcessar.Id.Valor);
        Assert.AreEqual(SituacaoPedido.Processado, registro.Situacao);
        Assert.IsNotNull(registro.ProcessadoEmUtc);
        Assert.AreEqual(SituacaoPedido.Cancelado, (await leitura.Pedidos.SingleAsync(p => p.Id == cancelado.Id.Valor)).Situacao);
    }

    [TestMethod]
    public async Task Fluxo_completo_com_EF_processa_cada_pedido_uma_unica_vez()
    {
        using var banco = new BancoDeTeste();
        var ids = new List<PedidoId>();
        for (var i = 0; i < 20; i++)
        {
            ids.Add((await banco.InserirAsync($"c{i}", DateTime.UtcNow)).Id);
        }

        var limitador = new LimitadorDeAcessoAoBanco(1);
        var opcoes = new OpcoesDaFila { Consumidores = 4, DuracaoSimuladaEmMs = 1 };
        // Um contexto por escopo (por item). A conexão em memória é única, por isso o
        // limite de 1 acesso ao banco também evita uso concorrente da mesma conexão.
        await using var ambiente = new AmbienteDoWorker(opcoes, servicos => servicos
            .AddSingleton(limitador)
            .AddSingleton(opcoes)
            .AddSingleton(TimeProvider.System)
            .AddScoped<ContextoDePedidos>(_ => banco.CriarContexto())
            .AddScoped<IProcessamentoDePedido, ProcessamentoDePedidoEfCore>());
        await ambiente.Worker.StartAsync(CancellationToken.None);

        foreach (var id in ids.Concat(ids.Take(5))) // 5 reenvios do mesmo pedido
        {
            await ambiente.Fila.EnfileirarAsync(id, CancellationToken.None);
        }

        await ambiente.Worker.StopAsync(CancellationToken.None);

        var metricas = ambiente.Metricas.Instantaneo(0, limitador.Pico);
        Assert.AreEqual(20, metricas.Processados);
        Assert.AreEqual(5, metricas.Ignorados);
        Assert.AreEqual(0, metricas.Falhas);
        Assert.AreEqual(1, limitador.Pico);
        await using var leitura = banco.CriarContexto();
        Assert.AreEqual(20, await leitura.Pedidos.CountAsync(p => p.Situacao == SituacaoPedido.Processado));
        limitador.Dispose();
    }
}
