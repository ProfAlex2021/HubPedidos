using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>
/// Gera massa de dados determinística (semente fixa) para tornar paginação, filtros e
/// agregações observáveis. Os pedidos passam pelo domínio, pela classificação e pelo
/// catálogo reais, então a massa respeita as mesmas regras da API.
/// </summary>
public static class SemeadorDePedidos
{
    private static readonly string[] Cidades = ["São Paulo", "Campinas", "Curitiba", "Porto Alegre", "Recife", "Manaus", "Goiânia"];
    private static readonly string[] Ufs = ["SP", "SP", "PR", "RS", "PE", "AM", "GO"];

    public static async Task<int> SemearAsync(
        ContextoDePedidos contexto,
        CatalogoDeProcessadores catalogo,
        int quantidade,
        DateTime referenciaUtc,
        int semente = 42,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidade);
        var aleatorio = new Random(semente);
        var regioes = Enum.GetValues<Regiao>();
        var prioridades = Enum.GetValues<Prioridade>();

        // Inserção em lotes: um SaveChanges por bloco, com o rastreador limpo entre blocos
        // para que ele não cresça junto com a massa inteira.
        const int TamanhoDoBloco = 500;
        for (var inicio = 0; inicio < quantidade; inicio += TamanhoDoBloco)
        {
            var fim = Math.Min(quantidade, inicio + TamanhoDoBloco);
            for (var i = inicio; i < fim; i++)
            {
                var itens = Enumerable.Range(0, aleatorio.Next(1, 7))
                    .Select(n => new ItemPedido(
                        $"SKU-{aleatorio.Next(1, 300):D4}",
                        aleatorio.Next(1, i % 37 == 0 ? 30 : 6),
                        decimal.Round((decimal)aleatorio.NextDouble() * (i % 53 == 0 ? 6_000m : 400m) + 1m, 2)))
                    .ToArray();

                var cidade = aleatorio.Next(Cidades.Length);
                var endereco = i % 4 == 0
                    ? null
                    : new EnderecoEntrega($"Rua {aleatorio.Next(1, 999)}", Cidades[cidade], Ufs[cidade], $"{aleatorio.Next(10_000_000, 99_999_999)}");

                var pedido = new Pedido(
                    PedidoId.Novo(),
                    $"cliente-{aleatorio.Next(1, 200):D3}",
                    regioes[aleatorio.Next(regioes.Length)],
                    prioridades[aleatorio.Next(prioridades.Length)],
                    itens,
                    endereco,
                    referenciaUtc.AddMinutes(-aleatorio.Next(0, 90 * 24 * 60)));

                var classificacao = ClassificadorDePedidos.Classificar(pedido);
                catalogo.TentarObter(classificacao.Categoria, out var entrada);
                contexto.Pedidos.Add(MapeadorDePersistencia.ParaRegistro(pedido, classificacao, entrada.Instancia.Processar(pedido)));
            }

            await contexto.SaveChangesAsync(cancellationToken);
            contexto.ChangeTracker.Clear();
        }

        return await contexto.Pedidos.CountAsync(cancellationToken);
    }
}
