using System.Collections.Concurrent;
using HubPedidos.Application.Notificacoes;

namespace HubPedidos.Tests.Aplicacao;

/// <summary>Notificador que só registra o que foi publicado.</summary>
internal sealed class NotificadorDeTeste : INotificadorDePedidos
{
    private readonly ConcurrentQueue<MudancaDeSituacao> _publicadas = new();

    public IReadOnlyList<MudancaDeSituacao> Publicadas => [.. _publicadas];

    public ValueTask PublicarAsync(MudancaDeSituacao mudanca, CancellationToken cancellationToken)
    {
        _publicadas.Enqueue(mudanca);
        return ValueTask.CompletedTask;
    }
}
