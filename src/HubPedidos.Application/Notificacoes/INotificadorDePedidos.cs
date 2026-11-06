using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Notificacoes;

/// <summary>
/// Evento mínimo de mudança de situação. Carrega só identificador, nova situação e
/// instante: nada de entidade, itens ou dados do cliente.
/// </summary>
public sealed record MudancaDeSituacao(PedidoId PedidoId, SituacaoPedido Situacao, DateTime InstanteUtc);

/// <summary>
/// Módulo de notificações (Roteiro 6). Os módulos de pedidos e de processamento
/// publicam aqui DEPOIS que a mudança foi persistida; quem entrega a mensagem (log
/// agora, SignalR no Roteiro 7) fica atrás desta interface.
/// </summary>
public interface INotificadorDePedidos
{
    ValueTask PublicarAsync(MudancaDeSituacao mudanca, CancellationToken cancellationToken);
}
