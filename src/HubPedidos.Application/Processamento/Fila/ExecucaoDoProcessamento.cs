using HubPedidos.Application.Notificacoes;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento.Fila;

/// <summary>
/// Caso de uso de processamento de um item (scoped, um por item da fila): processa e,
/// se a situação mudou, notifica. O worker resolve só esta classe no escopo do item;
/// daqui para dentro tudo chega por construtor.
/// </summary>
public sealed class ExecucaoDoProcessamento(
    IProcessamentoDePedido processamento,
    INotificadorDePedidos notificador,
    TimeProvider relogio)
{
    public async Task<ResultadoDoProcessamento> ExecutarAsync(PedidoId pedidoId, CancellationToken cancellationToken)
    {
        var resultado = await processamento.ProcessarAsync(pedidoId, cancellationToken);
        if (resultado == ResultadoDoProcessamento.Processado)
        {
            // Publica depois da mudança persistida, nunca antes.
            await notificador.PublicarAsync(
                new MudancaDeSituacao(pedidoId, SituacaoPedido.Processado, relogio.GetUtcNow().UtcDateTime), cancellationToken);
        }

        return resultado;
    }
}
