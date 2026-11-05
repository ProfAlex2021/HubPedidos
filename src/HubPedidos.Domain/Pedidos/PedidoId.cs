using System.Diagnostics.CodeAnalysis;

namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Identificador forte do pedido (value object).
/// </summary>
/// <remarks>
/// É um <c>readonly record struct</c>: a igualdade é por valor, a cópia é barata e
/// não existe identidade própria além do Guid. Um <see cref="Guid"/> "solto" poderia
/// ser confundido com o identificador de um cliente ou de um item; o tipo próprio
/// faz o compilador impedir essa troca.
///
/// Implementa <see cref="IParsable{TSelf}"/> (extensão do Roteiro 1): as Minimal APIs
/// usam o método estático <c>TryParse</c> para converter o segmento da rota, de modo
/// que um identificador inválido é rejeitado na fronteira HTTP sem código extra.
/// </remarks>
public readonly record struct PedidoId : IParsable<PedidoId>
{
    public PedidoId(Guid valor)
    {
        if (valor == Guid.Empty)
        {
            throw new ArgumentException("O identificador do pedido não pode ser vazio.", nameof(valor));
        }

        Valor = valor;
    }

    public Guid Valor { get; }

    /// <summary>
    /// Uma struct sempre pode ser criada com <c>default</c>, sem passar pelo construtor.
    /// Esta propriedade torna esse caso verificável em vez de escondê-lo.
    /// </summary>
    public bool EhValido => Valor != Guid.Empty;

    /// <summary>Gera um novo identificador. Guid v7 é ordenável pelo instante de criação.</summary>
    public static PedidoId Novo() => new(Guid.CreateVersion7());

    public override string ToString() => Valor.ToString("D");

    public static PedidoId Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var resultado)
            ? resultado
            : throw new FormatException($"'{s}' não é um identificador de pedido válido.");

    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out PedidoId resultado)
    {
        // Guid.Empty é sintaticamente válido, mas não identifica pedido algum.
        if (Guid.TryParse(s, out var guid) && guid != Guid.Empty)
        {
            resultado = new PedidoId(guid);
            return true;
        }

        resultado = default;
        return false;
    }

    /// <summary>Sobrecarga sem provedor de formato, também reconhecida pelas Minimal APIs.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out PedidoId resultado) =>
        TryParse(s, provider: null, out resultado);
}
