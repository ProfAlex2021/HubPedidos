using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Benchmarks.Importacao;

/// <summary>
/// Pergunta: agrupar a escrita (flush a cada N resultados) reduz o custo em relação
/// ao flush por item? Usa um arquivo temporário real para que cada flush seja uma
/// chamada de E/S ao sistema operacional, e não uma operação vazia em memória.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8)]
public class EscritaDeResultadosBenchmarks
{
    private const int Quantidade = 10_000;
    private string _arquivo = null!;
    private ResultadoDaLinhaDoLote[] _resultados = null!;

    [Params(1, 100)]
    public int ResultadosPorEscrita { get; set; }

    [GlobalSetup]
    public void Preparar()
    {
        _arquivo = Path.Combine(Path.GetTempPath(), $"hubpedidos-bench-{Guid.NewGuid():N}.ndjson");
        _resultados = [.. Enumerable.Range(1, Quantidade).Select(i => new ResultadoDaLinhaDoLote(i, "aceito", $"id-{i:D6}", "Padrao"))];
    }

    [GlobalCleanup]
    public void Limpar() => File.Delete(_arquivo);

    [Benchmark]
    public async Task<int> GravarNdjson()
    {
        await using var destino = new FileStream(_arquivo, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1, useAsync: true);
        await using var gravador = new GravadorDeResultados(destino, ResultadosPorEscrita);
        foreach (var resultado in _resultados)
        {
            await gravador.GravarAsync(resultado);
        }

        await gravador.ConcluirAsync();
        return gravador.Descargas;
    }
}
