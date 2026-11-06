namespace HubPedidos.Carga;

/// <summary>Resumo de uma amostra de latências, em milissegundos.</summary>
public sealed record ResumoDeLatencias(int Quantidade, double Media, double P50, double P95, double P99, double Maximo);

/// <summary>
/// Percentis pela convenção nearest-rank, sem interpolação (a mesma do livro-texto):
/// ordena a amostra e escolhe a posição ⌈p × n⌉ (contada a partir de 1).
/// </summary>
/// <remarks>
/// Por isso o resultado é chamado de p50, e não de "mediana": em amostras de tamanho
/// par, a mediana convencional seria a média dos dois elementos centrais.
/// </remarks>
public static class CalculadoraDePercentis
{
    public static ResumoDeLatencias Calcular(IEnumerable<double> milissegundos)
    {
        ArgumentNullException.ThrowIfNull(milissegundos);
        double[] ordenadas = [.. milissegundos.Order()];
        if (ordenadas.Length == 0)
        {
            throw new ArgumentException("A amostra está vazia.", nameof(milissegundos));
        }

        if (ordenadas.Any(v => !double.IsFinite(v) || v < 0))
        {
            throw new ArgumentException("As latências devem ser finitas e não negativas.", nameof(milissegundos));
        }

        return new ResumoDeLatencias(
            ordenadas.Length,
            ordenadas.Average(),
            Obter(ordenadas, 0.50),
            Obter(ordenadas, 0.95),
            Obter(ordenadas, 0.99),
            ordenadas[^1]);
    }

    private static double Obter(double[] ordenadas, double percentil)
    {
        var posicaoBaseUm = (int)Math.Ceiling(percentil * ordenadas.Length);
        return ordenadas[Math.Max(1, posicaoBaseUm) - 1]; // índice do array começa em 0
    }
}
