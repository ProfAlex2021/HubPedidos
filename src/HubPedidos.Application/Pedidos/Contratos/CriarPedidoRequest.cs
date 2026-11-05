namespace HubPedidos.Application.Pedidos.Contratos;

/// <summary>
/// DTO de entrada: declara somente o que o consumidor pode informar.
/// Identificador, categoria e totais ficam fora porque são decididos pela aplicação.
/// </summary>
/// <remarks>
/// Os campos são anuláveis e a região/prioridade chegam como texto de propósito:
/// assim um JSON incompleto ou com valor desconhecido vira uma lista de erros de
/// validação legíveis no mapeador, em vez de uma exceção de desserialização.
/// O endereço (Roteiro 3) é opcional: acrescentar um campo opcional no fim preserva
/// os consumidores anteriores.
/// </remarks>
public sealed record CriarPedidoRequest(
    string? ClienteId,
    string? Regiao,
    string? Prioridade,
    IReadOnlyList<ItemPedidoRequest>? Itens,
    EnderecoRequest? Endereco = null);

/// <summary>Item informado pelo consumidor.</summary>
public sealed record ItemPedidoRequest(string? Sku, int Quantidade, decimal PrecoUnitario);

/// <summary>Endereço de entrega informado pelo consumidor (opcional).</summary>
public sealed record EnderecoRequest(string? Logradouro, string? Cidade, string? Uf, string? Cep);
