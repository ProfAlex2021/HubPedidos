using System.Diagnostics.Metrics;

namespace HubPedidos.Application.Diagnostico;

/// <summary>
/// Métricas próprias do Hub (Roteiro 5), publicadas pelo <see cref="Meter"/> "HubPedidos".
/// Aparecem no dotnet-counters ao lado das métricas do runtime e do ASP.NET Core:
/// <c>dotnet-counters monitor -n HubPedidos.Api --counters System.Runtime,Microsoft.AspNetCore.Hosting,HubPedidos</c>
/// </summary>
/// <remarks>
/// Instrumentos estáticos: o custo de um <c>Add</c> sem ouvinte é praticamente zero, e
/// nenhum valor sensível (cliente, SKU) é usado como tag; só a categoria.
/// </remarks>
public static class TelemetriaDoHub
{
    public const string NomeDoMedidor = "HubPedidos";

    public static readonly Meter Medidor = new(NomeDoMedidor, "1.0");

    public static readonly Counter<long> PedidosRecebidos =
        Medidor.CreateCounter<long>("hubpedidos.pedidos.recebidos", "{pedido}", "Pedidos aceitos pela recepção");

    public static readonly Counter<long> AdmissoesRejeitadas =
        Medidor.CreateCounter<long>("hubpedidos.fila.rejeitados", "{pedido}", "Admissões rejeitadas por fila cheia");

    public static readonly Counter<long> PedidosProcessados =
        Medidor.CreateCounter<long>("hubpedidos.fila.processados", "{pedido}", "Pedidos processados pelo worker");

    /// <summary>Profundidade atual da fila, lida sob demanda pelo coletor.</summary>
    public static void PublicarProfundidadeDaFila(Func<int> profundidade) =>
        Medidor.CreateObservableGauge("hubpedidos.fila.profundidade", profundidade, "{item}", "Itens aguardando na fila");
}
