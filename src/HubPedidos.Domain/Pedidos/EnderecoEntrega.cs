namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Endereço de entrega (value object). Cidade, UF e CEP definem o valor; o logradouro
/// é opcional neste cenário didático. Igualdade por valor (record).
/// </summary>
public sealed record EnderecoEntrega
{
    public const int TamanhoMaximoLogradouro = 120;
    public const int TamanhoMaximoCidade = 80;

    private static readonly HashSet<string> UfsValidas = new(StringComparer.Ordinal)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    };

    public EnderecoEntrega(string? logradouro, string cidade, string uf, string cep)
    {
        if (logradouro is not null && logradouro.Trim().Length > TamanhoMaximoLogradouro)
        {
            throw new ArgumentException($"O logradouro deve ter no máximo {TamanhoMaximoLogradouro} caracteres.", nameof(logradouro));
        }

        if (string.IsNullOrWhiteSpace(cidade) || cidade.Trim().Length > TamanhoMaximoCidade)
        {
            throw new ArgumentException($"A cidade deve ter entre 1 e {TamanhoMaximoCidade} caracteres.", nameof(cidade));
        }

        if (!UfEhValida(uf))
        {
            throw new ArgumentException("UF inválida.", nameof(uf));
        }

        if (!CepEhValido(cep))
        {
            throw new ArgumentException("O CEP deve ter 8 dígitos.", nameof(cep));
        }

        Logradouro = string.IsNullOrWhiteSpace(logradouro) ? null : logradouro.Trim();
        Cidade = cidade.Trim();
        Uf = uf.Trim().ToUpperInvariant();
        Cep = cep.Trim();
    }

    public string? Logradouro { get; }

    public string Cidade { get; }

    public string Uf { get; }

    public string Cep { get; }

    public static bool UfEhValida(string? uf) => uf is not null && UfsValidas.Contains(uf.Trim().ToUpperInvariant());

    public static bool CepEhValido(string? cep) => cep is not null && cep.Trim() is { Length: 8 } c && c.All(char.IsAsciiDigit);
}
