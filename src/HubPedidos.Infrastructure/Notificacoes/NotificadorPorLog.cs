using HubPedidos.Application.Notificacoes;
using Microsoft.Extensions.Logging;

namespace HubPedidos.Infrastructure.Notificacoes;

/// <summary>
/// Implementação inicial do módulo de notificações: registra a mudança no log
/// estruturado. Singleton (sem estado). No Roteiro 7 a API registra uma implementação
/// com SignalR no lugar desta, sem mudar quem publica.
/// </summary>
public sealed partial class NotificadorPorLog(ILogger<NotificadorPorLog> logger) : INotificadorDePedidos
{
    public ValueTask PublicarAsync(MudancaDeSituacao mudanca, CancellationToken cancellationToken)
    {
        RegistrarMudanca(logger, mudanca.PedidoId, mudanca.Situacao, mudanca.InstanteUtc);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pedido {PedidoId} mudou para {Situacao} em {Instante:o}.")]
    private static partial void RegistrarMudanca(ILogger logger, Domain.Pedidos.PedidoId pedidoId, Domain.Pedidos.SituacaoPedido situacao, DateTime instante);
}
