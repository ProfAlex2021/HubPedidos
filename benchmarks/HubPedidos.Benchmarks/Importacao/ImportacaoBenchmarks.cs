using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Benchmarks.Importacao;

/// <summary>
/// Pergunta: a importação incremental com buffer alugado reduz memória em lotes
/// pequenos e grandes? MemoryStream mede só o caminho local (sem latência de rede/disco).
/// </summary>
/// <remarks>
/// As estratégias têm contratos de retenção diferentes: a inicial devolve a lista
/// completa; as incrementais entregam cada linha a um consumidor que só soma.
/// Parte da diferença de alocação vem justamente dessa retenção.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8)]
public class ImportacaoBenchmarks
{
    private readonly ImportadorDeLotesInicial _inicial = new();
    private readonly ImportadorDeLotesPorFluxo _arrayPool = new();
    private readonly ImportadorDeLotesPorFluxo _memoryPool = ImportadorDeLotesPorFluxo.ComMemoryPool();
    private byte[] _conteudo = null!;

    [Params(100, 10_000)]
    public int Linhas { get; set; }

    [GlobalSetup]
    public void Preparar() => _conteudo = DadosDeLote.ConteudoUtf8(Linhas);

    [Benchmark(Baseline = true, Description = "Inicial (ReadToEnd + lista)")]
    public async Task<int> Inicial()
    {
        using var origem = new MemoryStream(_conteudo, writable: false);
        var linhas = await _inicial.ImportarAsync(origem);
        var itens = 0;
        foreach (var linha in linhas)
        {
            itens += linha.Pedido!.Itens!.Count;
        }

        return itens;
    }

    [Benchmark(Description = "Fluxo + ArrayPool")]
    public Task<int> FluxoArrayPool() => ImportarAsync(_arrayPool);

    [Benchmark(Description = "Fluxo + MemoryPool")]
    public Task<int> FluxoMemoryPool() => ImportarAsync(_memoryPool);

    private async Task<int> ImportarAsync(ImportadorDeLotesPorFluxo importador)
    {
        using var origem = new MemoryStream(_conteudo, writable: false);
        var acumulador = new Acumulador();
        await importador.ImportarAsync(origem, acumulador.Somar);
        return acumulador.Itens;
    }

    private sealed class Acumulador
    {
        public int Itens { get; private set; }

        public ValueTask Somar(LinhaImportada linha, CancellationToken token)
        {
            Itens += linha.Pedido!.Itens!.Count;
            return ValueTask.CompletedTask;
        }
    }
}
