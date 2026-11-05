using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Consultas;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Benchmarks.Consultas;

/// <summary>
/// Roteiro 3, etapa 3: consulta comum × compilada para o detalhe de pedido (consulta
/// estável, repetida, com parâmetro escalar). SQLite em memória com 5.000 pedidos.
/// </summary>
/// <remarks>
/// Banco local em memória: quase não há custo de rede nem de disco, então a diferença
/// observada é o overhead do cliente (preparação da consulta + materialização). Em um
/// banco remoto, esse overhead seria uma fração menor do total.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8)]
public class DetalheCompiladoBenchmarks
{
    private BancoDeMedicao _banco = null!;
    private PedidoId[] _ids = null!;
    private int _proximo;

    [GlobalSetup]
    public void Preparar()
    {
        _banco = BancoDeMedicao.Criar(5_000);
        _ids = _banco.ObterIds(64);
    }

    [GlobalCleanup]
    public void Limpar() => _banco.Dispose();

    [Benchmark(Baseline = true, Description = "Detalhe: consulta comum")]
    public async Task<decimal> Comum()
    {
        await using var contexto = _banco.CriarContexto();
        return (await new ConsultasDePedidos(contexto).ObterDetalheAsync(ProximoId()))!.ValorTotal;
    }

    [Benchmark(Description = "Detalhe: EF.CompileAsyncQuery")]
    public async Task<decimal> Compilada()
    {
        await using var contexto = _banco.CriarContexto();
        return (await new ConsultasDePedidos(contexto).ObterDetalheCompiladoAsync(ProximoId()))!.ValorTotal;
    }

    private PedidoId ProximoId() => _ids[_proximo++ % _ids.Length];
}

/// <summary>
/// Extensão: paginação por deslocamento × por cursor em uma página "profunda"
/// (posição 4.000 de 5.000). O deslocamento obriga o banco a percorrer e descartar
/// as linhas anteriores; o cursor parte direto do índice.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8)]
public class PaginacaoBenchmarks
{
    private BancoDeMedicao _banco = null!;
    private string _cursorDaPosicao4000 = null!;

    [GlobalSetup]
    public void Preparar()
    {
        _banco = BancoDeMedicao.Criar(5_000);
        using var contexto = _banco.CriarContexto();
        var consultas = new ConsultasDePedidos(contexto);
        string? cursor = null;
        for (var i = 0; i < 40; i++) // 40 páginas de 100 = 4.000 linhas
        {
            cursor = consultas.ListarResumosPorCursorAsync(new FiltroDePedidos(), cursor, 100).GetAwaiter().GetResult().ProximoCursor;
        }

        _cursorDaPosicao4000 = cursor!;
    }

    [GlobalCleanup]
    public void Limpar() => _banco.Dispose();

    [Benchmark(Baseline = true, Description = "Página 41 por deslocamento (com COUNT)")]
    public async Task<int> Deslocamento()
    {
        await using var contexto = _banco.CriarContexto();
        return (await new ConsultasDePedidos(contexto).ListarResumosAsync(new FiltroDePedidos(), 41, 100)).Itens.Count;
    }

    [Benchmark(Description = "Mesma página por cursor")]
    public async Task<int> Cursor()
    {
        await using var contexto = _banco.CriarContexto();
        return (await new ConsultasDePedidos(contexto).ListarResumosPorCursorAsync(new FiltroDePedidos(), _cursorDaPosicao4000, 100)).Itens.Count;
    }
}

/// <summary>SQLite em memória com massa determinística, mantido aberto durante a medição.</summary>
internal sealed class BancoDeMedicao : IDisposable
{
    private readonly SqliteConnection _conexao;
    private readonly DbContextOptions<ContextoDePedidos> _opcoes;

    private BancoDeMedicao()
    {
        _conexao = new SqliteConnection("Data Source=:memory:");
        _conexao.Open();
        _opcoes = new DbContextOptionsBuilder<ContextoDePedidos>().UseSqlite(_conexao).Options;
    }

    public static BancoDeMedicao Criar(int pedidos)
    {
        var banco = new BancoDeMedicao();
        using var contexto = banco.CriarContexto();
        contexto.Database.Migrate();
        SemeadorDePedidos.SemearAsync(contexto, CatalogoDeProcessadores.DescobrirPadrao(), pedidos,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).GetAwaiter().GetResult();
        return banco;
    }

    public ContextoDePedidos CriarContexto() => new(_opcoes);

    public PedidoId[] ObterIds(int quantidade)
    {
        using var contexto = CriarContexto();
        return [.. contexto.Pedidos.OrderBy(p => p.Numero).Select(p => p.Id).Take(quantidade).AsEnumerable().Select(id => new PedidoId(id))];
    }

    public void Dispose() => _conexao.Dispose();
}
