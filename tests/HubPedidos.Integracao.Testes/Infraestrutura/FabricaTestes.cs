using System.Net;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HubPedidos.Integracao.Testes.Infraestrutura;

/// <summary>
/// A própria fixture é testada: estado inicial conhecido, isolamento entre fábricas,
/// vida útil do banco em memória e proteção contra o banco de desenvolvimento.
/// </summary>
[TestClass]
public sealed class FabricaTestes
{
    [TestMethod]
    public async Task Cada_fabrica_comeca_com_esquema_das_migrations_e_sem_dados()
    {
        await using var fabrica = new FabricaDaAplicacao();
        _ = fabrica.CriarCliente(); // sobe o host

        var pendentes = await fabrica.NoBancoAsync(contexto => contexto.Database.GetPendingMigrationsAsync());
        var aplicadas = await fabrica.NoBancoAsync(contexto => contexto.Database.GetAppliedMigrationsAsync());

        Assert.IsEmpty(pendentes);
        Assert.IsNotEmpty(aplicadas);
        Assert.AreEqual(0, await fabrica.NoBancoAsync(contexto => contexto.Pedidos.CountAsync()));
        Assert.AreEqual("Testes", fabrica.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
    }

    [TestMethod]
    public async Task Fabricas_diferentes_nao_compartilham_dados()
    {
        await using var primeira = new FabricaDaAplicacao();
        await using var segunda = new FabricaDaAplicacao();

        var id = await primeira.CriarPedidoAsync("cliente-001");

        Assert.AreNotEqual(primeira.ConnectionString, segunda.ConnectionString);
        Assert.AreEqual(HttpStatusCode.OK, (await primeira.CriarCliente().GetAsync($"/api/pedidos/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await segunda.CriarCliente().GetAsync($"/api/pedidos/{id}")).StatusCode);
    }

    [TestMethod]
    public async Task Banco_vive_enquanto_a_conexao_guardia_existe_e_some_no_descarte()
    {
        string connectionString;
        await using (var fabrica = new FabricaDaAplicacao())
        {
            connectionString = fabrica.ConnectionString;
            var id = await fabrica.CriarPedidoAsync("cliente-001");

            // Requisições diferentes usam conexões diferentes; o dado continua lá
            // porque a conexão guardiã mantém o banco em memória vivo.
            Assert.AreEqual(HttpStatusCode.OK, (await fabrica.CriarCliente().GetAsync($"/api/pedidos/{id}")).StatusCode);
            Assert.AreEqual(1, ContarTabelas(connectionString, "Pedidos"));
        }

        // Depois do descarte, abrir o mesmo nome cria um banco NOVO e vazio.
        Assert.AreEqual(0, ContarTabelas(connectionString, "Pedidos"));
    }

    [TestMethod]
    [DataRow("Data Source=dados/hubpedidos.db")]
    [DataRow("Data Source=C:\\temp\\hubpedidos.db")]
    [DataRow("")]
    public void Banco_em_arquivo_e_recusado_pela_fabrica(string connectionString)
    {
        var excecao = Assert.Throws<InvalidOperationException>(() => FabricaDaAplicacao.GarantirBancoEfemero(connectionString));

        StringAssert.Contains(excecao.Message, "só usa SQLite em memória");
    }

    [TestMethod]
    public async Task Configuracao_externa_ou_substituicao_nao_desviam_para_um_banco_em_arquivo()
    {
        // Simula a máquina do desenvolvedor: uma connection string de arquivo vinda de fora
        // (como ConnectionStrings__HubPedidos) e um teste que tenta registrar outro DbContext.
        var arquivo = Path.Combine(Path.GetTempPath(), $"hubpedidos-nao-deve-existir-{Guid.NewGuid():N}.db");
        await using var fabrica = new FabricaDaAplicacao(
            substituicoes: servicos => servicos.AddDbContext<ContextoDePedidos>(opcoes => opcoes.UseSqlite($"Data Source={arquivo}")),
            configuracao: new Dictionary<string, string?> { ["ConnectionStrings:HubPedidos"] = $"Data Source={arquivo}" });

        await fabrica.CriarPedidoAsync("cliente-001");
        var usado = await fabrica.NoBancoAsync(contexto => Task.FromResult(contexto.Database.GetConnectionString()));

        Assert.AreEqual(fabrica.ConnectionString, usado);
        Assert.IsFalse(File.Exists(arquivo), "Nenhum arquivo de banco pode ser criado pelos testes.");
    }

    private static long ContarTabelas(string connectionString, string tabela)
    {
        using var conexao = new SqliteConnection(connectionString);
        conexao.Open();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $nome";
        comando.Parameters.AddWithValue("$nome", tabela);
        return (long)comando.ExecuteScalar()!;
    }
}
