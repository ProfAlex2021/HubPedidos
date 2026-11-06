using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace HubPedidos.Tests.Processamento.Apoio;

/// <summary>Processamento falso cujo comportamento cada teste define.</summary>
internal sealed class ProcessamentoControlado(Func<PedidoId, CancellationToken, Task<ResultadoDoProcessamento>> comportamento)
    : IProcessamentoDePedido
{
    private int _chamadas;

    public int Chamadas => Volatile.Read(ref _chamadas);

    public Task<ResultadoDoProcessamento> ProcessarAsync(PedidoId pedidoId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _chamadas);
        return comportamento(pedidoId, cancellationToken);
    }
}

/// <summary>Monta fila, métricas, worker e contêiner para os testes.</summary>
internal sealed class AmbienteDoWorker : IAsyncDisposable
{
    private readonly ServiceProvider _provedor;

    public AmbienteDoWorker(OpcoesDaFila opcoes, Action<IServiceCollection> registrarProcessamento)
    {
        Metricas = new MetricasDoProcessamento();
        Fila = new FilaDeProcessamento(opcoes, Metricas);
        var servicos = new ServiceCollection();
        registrarProcessamento(servicos);
        // Roteiro 6: o worker resolve ExecucaoDoProcessamento (processa e notifica).
        Notificador = new Aplicacao.NotificadorDeTeste();
        servicos.AddSingleton<HubPedidos.Application.Notificacoes.INotificadorDePedidos>(Notificador);
        servicos.TryAddSingleton(TimeProvider.System);
        servicos.AddScoped<ExecucaoDoProcessamento>();
        _provedor = servicos.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Worker = new ProcessadorDePedidosWorker(
            Fila, Metricas, _provedor.GetRequiredService<IServiceScopeFactory>(), NullLogger<ProcessadorDePedidosWorker>.Instance);
    }

    public static AmbienteDoWorker ComFalso(OpcoesDaFila opcoes, ProcessamentoControlado falso) =>
        new(opcoes, servicos => servicos.AddSingleton<IProcessamentoDePedido>(falso));

    public MetricasDoProcessamento Metricas { get; }

    internal Aplicacao.NotificadorDeTeste Notificador { get; }

    public FilaDeProcessamento Fila { get; }

    public ProcessadorDePedidosWorker Worker { get; }

    public async ValueTask DisposeAsync()
    {
        Worker.Dispose();
        await _provedor.DisposeAsync();
    }

    /// <summary>Espera uma condição sem atraso fixo; falha com mensagem útil se não ocorrer em 5 s.</summary>
    public static async Task EsperarAteAsync(Func<bool> condicao, string descricao)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicao())
        {
            if (DateTime.UtcNow > limite)
            {
                Assert.Fail($"A condição não ocorreu em 5 s: {descricao}");
            }

            await Task.Delay(5);
        }
    }
}
