using HubPedidos.Application.Notificacoes;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;

namespace HubPedidos.Api.TempoReal;

/// <summary>
/// Implementação do módulo de notificações na API (Roteiro 7), registrada no lugar do
/// NotificadorPorLog. Chamado DEPOIS da mudança persistida:
/// 1. invalida o output cache das leituras agregadas (tag "pedidos");
/// 2. publica o evento tipado só para o grupo do pedido.
/// Singleton: IHubContext e IOutputCacheStore são singletons e não guardam estado por chamada.
/// </summary>
public sealed partial class NotificadorDaApi(
    IHubContext<PedidosHub, IClienteDePedidos> hub,
    IOutputCacheStore cache,
    ILogger<NotificadorDaApi> logger) : INotificadorDePedidos
{
    public const string TagDoCache = "pedidos";

    public async ValueTask PublicarAsync(MudancaDeSituacao mudanca, CancellationToken cancellationToken)
    {
        await cache.EvictByTagAsync(TagDoCache, cancellationToken);

        var evento = new PedidoAtualizado(mudanca.PedidoId.ToString(), mudanca.Situacao.ToString(), mudanca.InstanteUtc.Ticks, mudanca.InstanteUtc);
        await hub.Clients.Group(PedidosHub.Grupo(mudanca.PedidoId)).PedidoAtualizado(evento);
        RegistrarPublicacao(logger, mudanca.PedidoId, mudanca.Situacao);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pedido {PedidoId} -> {Situacao}: evento publicado e cache invalidado.")]
    private static partial void RegistrarPublicacao(ILogger logger, Domain.Pedidos.PedidoId pedidoId, Domain.Pedidos.SituacaoPedido situacao);
}
