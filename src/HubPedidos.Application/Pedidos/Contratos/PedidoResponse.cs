namespace HubPedidos.Application.Pedidos.Contratos;

/// <summary>
/// DTO de saída. Usa propriedades <c>init</c>: o mapeador monta a resposta com nomes
/// explícitos e, depois disso, nenhuma propriedade pode ser reatribuída.
/// </summary>
/// <remarks>
/// Os valores de domínio (enums, <c>PedidoId</c>) são convertidos para texto aqui,
/// para que nenhum tipo interno atravesse a fronteira HTTP.
/// </remarks>
public sealed record PedidoResponse
{
    public required string Id { get; init; }

    public required string ClienteId { get; init; }

    public required string Regiao { get; init; }

    public required string Prioridade { get; init; }

    public required string Categoria { get; init; }

    public required string MotivoDaCategoria { get; init; }

    public required string Processador { get; init; }

    public required int PrazoEstimadoEmDias { get; init; }

    public required decimal ValorTotal { get; init; }

    public required int QuantidadeDeItens { get; init; }

    /// <summary>Snapshot somente leitura criado pelo mapeador; nunca a coleção da entidade.</summary>
    public IReadOnlyList<ItemPedidoResponse> Itens { get; init; } = [];
}

/// <summary>Item na resposta, já com o subtotal calculado.</summary>
public sealed record ItemPedidoResponse(string Sku, int Quantidade, decimal PrecoUnitario, decimal Subtotal);
