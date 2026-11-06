using BenchmarkDotNet.Running;
using HubPedidos.Benchmarks.Experimentos;

namespace HubPedidos.Benchmarks;

/// <summary>
/// Ponto de entrada. O BenchmarkSwitcher encontra todas as classes de benchmark do
/// assembly; use <c>--filter</c> para escolher quais executar.
/// <c>--experimento-fila</c> executa o experimento de backpressure do Roteiro 4.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("--experimento-fila"))
        {
            await ExperimentoDeBackpressure.ExecutarAsync();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
