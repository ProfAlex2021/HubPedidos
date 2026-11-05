using BenchmarkDotNet.Running;

namespace HubPedidos.Benchmarks;

/// <summary>
/// Ponto de entrada. O BenchmarkSwitcher encontra todas as classes de benchmark do
/// assembly; use <c>--filter</c> para escolher quais executar.
/// </summary>
public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
