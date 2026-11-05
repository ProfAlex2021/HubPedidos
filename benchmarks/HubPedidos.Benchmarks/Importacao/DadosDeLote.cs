using System.Globalization;
using System.Text;

namespace HubPedidos.Benchmarks.Importacao;

/// <summary>Gera lotes determinísticos (sem aleatoriedade) para que as medições sejam reproduzíveis.</summary>
internal static class DadosDeLote
{
    public static string[] Linhas(int quantidade) =>
    [
        .. Enumerable.Range(1, quantidade).Select(i => string.Create(
            CultureInfo.InvariantCulture,
            $"cliente-{i % 500:D4};{Regioes[i % Regioes.Length]};{Prioridades[i % Prioridades.Length]};" +
            $"SKU-{i:D5}:{(i % 7) + 1}:{(i % 300) + 0.99m},SKU-B{i % 13}:1:12.50,ÇAFÉ-{i % 3}:2:3.40")),
    ];

    public static byte[] ConteudoUtf8(int quantidade) =>
        new UTF8Encoding(false).GetBytes(string.Join('\n', Linhas(quantidade)) + "\n");

    private static readonly string[] Regioes = ["Norte", "Nordeste", "CentroOeste", "Sudeste", "Sul"];
    private static readonly string[] Prioridades = ["Normal", "Normal", "Alta", "Urgente"];
}
