using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Tests.Persistencia;

/// <summary>
/// Banco SQLite em memória isolado, criado e apagado pelo próprio teste: o banco
/// existe enquanto a conexão estiver aberta e desaparece no Dispose.
/// O esquema é criado pelas MIGRATIONS reais (não por EnsureCreated), então os
/// testes também validam a migration.
/// </summary>
internal sealed class BancoDeTeste : IDisposable
{
    private static readonly CatalogoDeProcessadores Catalogo = CatalogoDeProcessadores.DescobrirPadrao();
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");

    public BancoDeTeste()
    {
        _conexao.Open();
        using var contexto = CriarContexto();
        contexto.Database.Migrate();
        Comandos.Limpar();
    }

    /// <summary>Todos os comandos SQL enviados por contextos criados a partir deste banco.</summary>
    public ContadorDeComandos Comandos { get; } = new();

    public ContextoDePedidos CriarContexto() =>
        new(new DbContextOptionsBuilder<ContextoDePedidos>()
            .UseSqlite(_conexao)
            .AddInterceptors(Comandos)
            .Options);

    /// <summary>Grava um pedido passando por domínio, classificação e catálogo reais.</summary>
    public async Task<Pedido> InserirAsync(
        string cliente,
        DateTime criadoEmUtc,
        decimal precoUnitario = 10m,
        int quantidade = 1,
        EnderecoEntrega? endereco = null,
        Prioridade prioridade = Prioridade.Normal)
    {
        var pedido = new Pedido(
            PedidoId.Novo(), cliente, Regiao.Sul, prioridade,
            [new ItemPedido("SKU-A", quantidade, precoUnitario), new ItemPedido("SKU-B", 1, 1m)],
            endereco, criadoEmUtc);
        var classificacao = ClassificadorDePedidos.Classificar(pedido);
        Catalogo.TentarObter(classificacao.Categoria, out var entrada);

        await using var contexto = CriarContexto();
        await new RepositorioDePedidos(contexto).AdicionarAsync(pedido, classificacao, entrada.Instancia.Processar(pedido));
        return pedido;
    }

    public void Dispose() => _conexao.Dispose();
}

/// <summary>Banco efêmero para o contexto alternativo com endereço em JSON.</summary>
internal sealed class BancoJsonDeTeste : IDisposable
{
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");

    public BancoJsonDeTeste()
    {
        _conexao.Open();
        using var contexto = CriarContexto();
        // O modelo JSON é só para comparação e não tem migrations próprias.
        contexto.Database.EnsureCreated();
        Comandos.Limpar();
    }

    public ContadorDeComandos Comandos { get; } = new();

    public ContextoDePedidosComEnderecoJson CriarContexto() =>
        new(new DbContextOptionsBuilder<ContextoDePedidosComEnderecoJson>()
            .UseSqlite(_conexao)
            .AddInterceptors(Comandos)
            .Options);

    public void Dispose() => _conexao.Dispose();
}

/// <summary>
/// Extensão do Roteiro 3: falha quando um trecho envia mais comandos SQL do que o
/// orçamento permitido. Detecta N+1 sem depender de tempo de execução.
/// </summary>
internal static class OrcamentoDeComandos
{
    public static async Task<T> VerificarAsync<T>(ContadorDeComandos contador, int maximo, Func<Task<T>> acao)
    {
        contador.Limpar();
        var resultado = await acao();
        if (contador.Quantidade > maximo)
        {
            throw new OrcamentoDeComandosExcedidoException(
                $"Esperado no máximo {maximo} comando(s) SQL, mas {contador.Quantidade} foram enviados. " +
                "Provável N+1: carregue os dados relacionados na mesma consulta.");
        }

        return resultado;
    }
}

internal sealed class OrcamentoDeComandosExcedidoException(string mensagem) : Exception(mensagem);
