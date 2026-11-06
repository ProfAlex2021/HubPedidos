using System.Threading.Channels;
using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Integracao.Testes.Infraestrutura;

/// <summary>
/// Consumidor falso e observável da fila: substitui o processamento EF Core para que o
/// teste saiba QUANDO um item começou e terminou, sem esperar por tempo.
/// </summary>
/// <remarks>
/// O teste aguarda os canais <see cref="Iniciados"/> e <see cref="Concluidos"/> (sinais,
/// não atrasos). A <see cref="Liberar"/> controla a "porta": enquanto fechada, o item fica
/// em andamento, o que permite encher a fila de forma determinística.
/// </remarks>
public sealed class ProcessamentoObservavel : IProcessamentoDePedido
{
    private readonly Channel<PedidoId> _iniciados = Channel.CreateUnbounded<PedidoId>();
    private readonly Channel<PedidoId> _concluidos = Channel.CreateUnbounded<PedidoId>();
    private readonly TaskCompletionSource _porta;

    public ProcessamentoObservavel(bool portaAberta = true)
    {
        _porta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (portaAberta)
        {
            _porta.SetResult();
        }
    }

    public ChannelReader<PedidoId> Iniciados => _iniciados.Reader;

    public ChannelReader<PedidoId> Concluidos => _concluidos.Reader;

    public void Liberar() => _porta.TrySetResult();

    public async Task<ResultadoDoProcessamento> ProcessarAsync(PedidoId pedidoId, CancellationToken cancellationToken)
    {
        _iniciados.Writer.TryWrite(pedidoId);
        await _porta.Task.WaitAsync(cancellationToken);
        _concluidos.Writer.TryWrite(pedidoId);
        return ResultadoDoProcessamento.Processado;
    }
}
