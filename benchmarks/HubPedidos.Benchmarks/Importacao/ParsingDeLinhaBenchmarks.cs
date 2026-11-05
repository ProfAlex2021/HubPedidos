using System.Text;
using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Benchmarks.Importacao;

/// <summary>
/// Pergunta: interpretar uma linha com spans (char ou UTF-8) reduz alocação e tempo
/// em relação a Split/Substring? Mil linhas preparadas fora da região medida.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8)]
public class ParsingDeLinhaBenchmarks
{
    private string[] _linhas = null!;
    private byte[][] _linhasUtf8 = null!;

    [GlobalSetup]
    public void Preparar()
    {
        _linhas = DadosDeLote.Linhas(1_000);
        _linhasUtf8 = [.. _linhas.Select(Encoding.UTF8.GetBytes)];
    }

    [Benchmark(Baseline = true, Description = "Inicial (Split/string)")]
    public int Inicial()
    {
        var soma = 0;
        for (var i = 0; i < _linhas.Length; i++)
        {
            soma += ParserDeLinhaInicial.Interpretar(_linhas[i], i + 1).Pedido!.Itens!.Count;
        }

        return soma;
    }

    [Benchmark(Description = "ReadOnlySpan<char>")]
    public int ComSpan()
    {
        var soma = 0;
        for (var i = 0; i < _linhas.Length; i++)
        {
            soma += ParserDeLinhaComSpan.Interpretar(_linhas[i], i + 1).Pedido!.Itens!.Count;
        }

        return soma;
    }

    [Benchmark(Description = "ReadOnlySpan<byte> UTF-8")]
    public int Utf8()
    {
        var soma = 0;
        for (var i = 0; i < _linhasUtf8.Length; i++)
        {
            soma += ParserDeLinhaUtf8.Interpretar(_linhasUtf8[i], i + 1).Pedido!.Itens!.Count;
        }

        return soma;
    }
}
