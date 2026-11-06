using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Benchmarks.Diagnostico;

/// <summary>
/// Roteiro 5, etapa 1.1: método crítico isolável da rota de criação. Pergunta: a
/// validação + mapeamento do DTO é um custo relevante dentro dos milissegundos de um
/// POST /api/pedidos? (Hipótese do roteiro: não; o custo dominante está no banco.)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8)]
public class MapeamentoDePedidoBenchmarks
{
    private static readonly PedidoId Id = PedidoId.Novo();
    private CriarPedidoRequest _pequeno = null!;
    private CriarPedidoRequest _grande = null!;
    private CriarPedidoRequest _invalido = null!;

    [GlobalSetup]
    public void Preparar()
    {
        _pequeno = new("cliente-001", "Sudeste", "Normal", [new("SKU-1", 2, 10.90m), new("CABO", 1, 5m)]);
        _grande = new("cliente-001", "Sudeste", "Normal",
            [.. Enumerable.Range(1, 50).Select(i => new ItemPedidoRequest($"SKU-{i}", 1 + i % 5, 10.5m + i))],
            new EnderecoRequest("Rua A", "Curitiba", "PR", "80000000"));
        _invalido = new("", "Marte", "5", [new("", 0, 10.999m)]);
    }

    [Benchmark(Baseline = true, Description = "DTO com 2 itens")]
    public Pedido? Pequeno() => MapeadorDePedido.ParaDominio(_pequeno, Id).Valor;

    [Benchmark(Description = "DTO com 50 itens + endereço")]
    public Pedido? Grande() => MapeadorDePedido.ParaDominio(_grande, Id).Valor;

    [Benchmark(Description = "DTO inválido (6 erros)")]
    public int Invalido() => MapeadorDePedido.ParaDominio(_invalido, Id).Erros.Count;
}
