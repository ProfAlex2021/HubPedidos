using System.Text.Json.Serialization;
using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Api.Contratos.V2;

// Contrato público da v2 da consulta de pedido. Os nomes JSON são declarados
// explicitamente: a partir da publicação, eles são compromissos com os clientes e não
// podem mudar por uma simples renomeação de propriedade em C#.
//
// Mudanças INCOMPATÍVEIS em relação à v1 (deliberadas e documentadas):
// - "clienteId" (texto) virou "cliente": { "id" }       -> nome e forma mudaram
// - "valorTotal" (número) virou "valor": { "quantia", "moeda" } -> forma mudou
// - "prioridade"/"regiao" passam a ficar em "entrega": { "regiao", "prioridade", "endereco" }
// - "situacao" passa a ser texto em minúsculas ("recebido", "processado"...)
// - "motivoDaCategoria" e "prazoEstimadoEmDias" saem; entra "previsaoDeEntrega" (data ou null)
// - preços dos itens também usam o objeto de dinheiro

public sealed record PedidoResponseV2(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("cliente")] ClienteV2 Cliente,
    [property: JsonPropertyName("situacao")] string Situacao,
    [property: JsonPropertyName("categoria")] string Categoria,
    [property: JsonPropertyName("valor")] DinheiroV2 Valor,
    [property: JsonPropertyName("entrega")] EntregaV2 Entrega,
    [property: JsonPropertyName("previsaoDeEntrega")] DateOnly? PrevisaoDeEntrega,
    [property: JsonPropertyName("criadoEm")] DateTime CriadoEm,
    [property: JsonPropertyName("itens")] IReadOnlyList<ItemV2> Itens);

public sealed record ClienteV2([property: JsonPropertyName("id")] string Id);

public sealed record DinheiroV2(
    [property: JsonPropertyName("quantia")] decimal Quantia,
    [property: JsonPropertyName("moeda")] string Moeda);

public sealed record EntregaV2(
    [property: JsonPropertyName("regiao")] string Regiao,
    [property: JsonPropertyName("prioridade")] string Prioridade,
    [property: JsonPropertyName("endereco")] EnderecoResponse? Endereco);

public sealed record ItemV2(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("quantidade")] int Quantidade,
    [property: JsonPropertyName("precoUnitario")] DinheiroV2 PrecoUnitario,
    [property: JsonPropertyName("subtotal")] DinheiroV2 Subtotal);

/// <summary>
/// Adaptador anticorrupção (extensão do Roteiro 6): traduz a representação interna,
/// que é a mesma da v1, para o contrato v2. Domínio, serviços e consultas são
/// compartilhados; as diferenças ficam concentradas aqui, na fronteira.
/// </summary>
public static class AdaptadorDePedidoV2
{
    public const string MoedaPadrao = "BRL";

    public static PedidoResponseV2 Converter(PedidoResponse pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return new PedidoResponseV2(
            pedido.Id,
            new ClienteV2(pedido.ClienteId),
            pedido.Situacao.ToLowerInvariant(),
            pedido.Categoria,
            Dinheiro(pedido.ValorTotal),
            new EntregaV2(pedido.Regiao, pedido.Prioridade, pedido.Endereco),
            CalcularPrevisao(pedido),
            pedido.CriadoEmUtc,
            [.. pedido.Itens.Select(i => new ItemV2(i.Sku, i.Quantidade, Dinheiro(i.PrecoUnitario), Dinheiro(i.Subtotal)))]);
    }

    /// <summary>
    /// Previsão só existe enquanto o pedido está em andamento; para cancelados não há
    /// data legítima, e o contrato devolve null em vez de inventar uma.
    /// </summary>
    private static DateOnly? CalcularPrevisao(PedidoResponse pedido) =>
        pedido.Situacao == "Cancelado"
            ? null
            : DateOnly.FromDateTime(pedido.CriadoEmUtc.AddDays(pedido.PrazoEstimadoEmDias));

    private static DinheiroV2 Dinheiro(decimal quantia) => new(quantia, MoedaPadrao);
}
