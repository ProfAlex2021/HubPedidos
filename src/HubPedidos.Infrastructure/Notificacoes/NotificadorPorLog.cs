using HubPedidos.Application.Notificacoes;
using Microsoft.Extensions.Logging;

namespace HubPedidos.Infrastructure.Notificacoes;

/// <summary>
/// Implementação inicial do módulo de notificações: registra a mudança no log
/// estruturado. Singleton (sem estado). No Roteiro 7 a API registra uma implementação
/// com SignalR no lugar desta, sem mudar quem publica.
/// </summary>
public sealed class NotificadorPorLog(ILogger<NotificadorPorLog> logger) : INotificadorDePedidos
{
    public ValueTask PublicarAsync(MudancaDeSituacao mudanca, CancellationToken cancellationToken)
    {
        logger.LogInformation("Pedido {PedidoId} mudou para {Situacao} em {Instante:o}.", mudanca.PedidoId, mudanca.Situacao, mudanca.InstanteUtc);
        return ValueTask.CompletedTask;
    }
}
