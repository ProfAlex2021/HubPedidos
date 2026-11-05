namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Item de um pedido (value object): SKU, quantidade e preço unitário definem
/// integralmente o valor, por isso dois itens com os mesmos componentes são iguais.
/// </summary>
/// <remarks>
/// O record usa um construtor convencional (e não posicional) para validar os
/// componentes antes da criação. As propriedades são somente leitura (sem <c>init</c>),
/// então uma expressão <c>with</c> não consegue contornar a validação.
/// </remarks>
public sealed record ItemPedido
{
    public const int TamanhoMaximoSku = 40;
    public const int QuantidadeMaxima = 1_000;
    public const decimal PrecoMaximo = 100_000m;

    public ItemPedido(string sku, int quantidade, decimal precoUnitario)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("O SKU deve ser informado.", nameof(sku));
        }

        var skuNormalizado = sku.Trim();
        if (skuNormalizado.Length > TamanhoMaximoSku)
        {
            throw new ArgumentException(
                $"O SKU deve ter no máximo {TamanhoMaximoSku} caracteres.", nameof(sku));
        }

        if (quantidade is < 1 or > QuantidadeMaxima)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidade), $"A quantidade deve estar entre 1 e {QuantidadeMaxima}.");
        }

        if (precoUnitario is <= 0m or > PrecoMaximo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precoUnitario), $"O preço unitário deve ser maior que zero e no máximo {PrecoMaximo}.");
        }

        if (decimal.Round(precoUnitario, 2) != precoUnitario)
        {
            throw new ArgumentException("O preço unitário deve ter no máximo duas casas decimais.", nameof(precoUnitario));
        }

        Sku = skuNormalizado;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }

    public string Sku { get; }

    public int Quantidade { get; }

    public decimal PrecoUnitario { get; }

    /// <summary>Calculado a partir dos componentes: não é estado independente que possa divergir.</summary>
    public decimal Subtotal => Quantidade * PrecoUnitario;
}
