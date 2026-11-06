namespace HubPedidos.Domain.Pedidos;

/// <summary>Região de entrega usada na classificação logística.</summary>
public enum Regiao
{
    Norte = 1,
    Nordeste = 2,
    CentroOeste = 3,
    Sudeste = 4,
    Sul = 5,
}

/// <summary>Prioridade informada pelo cliente.</summary>
public enum Prioridade
{
    Normal = 1,
    Alta = 2,
    Urgente = 3,
}

/// <summary>Ciclo de vida do pedido.</summary>
public enum SituacaoPedido
{
    Recebido = 1,
    Cancelado = 2,

    /// <summary>Roteiro 4: processado pelo worker a partir da fila.</summary>
    Processado = 3,
}

/// <summary>
/// Categoria de processamento produzida pela classificação.
/// <see cref="Invalida"/> existe para que dados inválidos tenham um resultado
/// explícito, em vez de cair silenciosamente em uma categoria "padrão".
/// </summary>
public enum CategoriaProcessamento
{
    Invalida = 0,
    Padrao = 1,
    Prioritaria = 2,
    Expressa = 3,
    Volumosa = 4,
    AltoValor = 5,
}
