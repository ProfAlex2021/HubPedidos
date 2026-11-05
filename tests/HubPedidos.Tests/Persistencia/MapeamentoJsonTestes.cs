using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Infrastructure.Persistencia.Modelos;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Tests.Persistencia;

/// <summary>
/// Etapa 4: o mesmo endereço mapeado em colunas (principal) e como JSON (alternativa).
/// Os testes de JSON só rodam no provedor e na versão em que o recurso foi verificado.
/// </summary>
[TestClass]
public sealed class MapeamentoJsonTestes
{
    private const string ProvedorVerificado = "Microsoft.EntityFrameworkCore.Sqlite";

    [TestMethod]
    public async Task Filtro_por_cidade_no_mapeamento_relacional_usa_a_coluna_dedicada()
    {
        using var banco = new BancoDeTeste();
        await banco.InserirAsync("a", DateTime.UtcNow, endereco: new("Rua X", "Curitiba", "PR", "80000000"));
        await banco.InserirAsync("b", DateTime.UtcNow, endereco: new("Rua Y", "Recife", "PE", "50000000"));
        await using var contexto = banco.CriarContexto();

        var consulta = contexto.Pedidos.Where(p => p.Endereco!.Cidade == "Curitiba").Select(p => p.ClienteId);

        StringAssert.Contains(consulta.ToQueryString(), "\"Entrega_Cidade\"");
        CollectionAssert.AreEqual(new[] { "a" }, await consulta.ToArrayAsync());
    }

    [TestMethod]
    public async Task Mapeamento_JSON_filtra_dentro_do_documento_e_substitui_o_documento_na_atualizacao()
    {
        using var banco = new BancoJsonDeTeste();
        await using var contexto = banco.CriarContexto();
        VerificarSuporteJson(contexto);

        contexto.Pedidos.AddRange(
            NovoPedido("a", new EnderecoRegistro { Logradouro = "Rua X", Cidade = "Curitiba", Uf = "PR", Cep = "80000000" }),
            NovoPedido("b", new EnderecoRegistro { Cidade = "Recife", Uf = "PE", Cep = "50000000" }),
            NovoPedido("c", null)); // complexo opcional: sem endereço
        await contexto.SaveChangesAsync();
        contexto.ChangeTracker.Clear();

        // Filtro: o provedor traduz o acesso ao membro do documento (operador ->> do SQLite).
        var consulta = contexto.Pedidos.Where(p => p.Endereco!.Cidade == "Curitiba").Select(p => p.ClienteId);
        var sqlDoFiltro = consulta.ToQueryString();
        StringAssert.Contains(sqlDoFiltro, "\"EnderecoJson\"");
        Assert.IsTrue(sqlDoFiltro.Contains("->>", StringComparison.Ordinal) || sqlDoFiltro.Contains("json_extract", StringComparison.Ordinal), sqlDoFiltro);
        CollectionAssert.AreEqual(new[] { "a" }, await consulta.ToArrayAsync());

        // Atualização rastreada de um único membro.
        var pedido = await contexto.Pedidos.SingleAsync(p => p.ClienteId == "b");
        pedido.Endereco!.Cidade = "Olinda";
        banco.Comandos.Limpar();
        await contexto.SaveChangesAsync();

        var update = banco.Comandos.Comandos.Single(c => c.Contains("UPDATE", StringComparison.Ordinal));
        StringAssert.Contains(update, "\"EnderecoJson\"");
        Assert.IsNull(await contexto.Pedidos.Where(p => p.ClienteId == "c").Select(p => p.Endereco).SingleAsync());

        contexto.ChangeTracker.Clear();
        Assert.AreEqual("Olinda", (await contexto.Pedidos.SingleAsync(p => p.ClienteId == "b")).Endereco!.Cidade);
    }

    private static void VerificarSuporteJson(DbContext contexto)
    {
        var versaoDoEf = typeof(DbContext).Assembly.GetName().Version!;
        if (contexto.Database.ProviderName != ProvedorVerificado || versaoDoEf.Major < 10)
        {
            Assert.Inconclusive($"Mapeamento JSON verificado apenas em {ProvedorVerificado} com EF Core 10 (atual: {contexto.Database.ProviderName} {versaoDoEf}).");
        }
    }

    private static PedidoRegistro NovoPedido(string cliente, EnderecoRegistro? endereco) => new()
    {
        Id = Guid.CreateVersion7(),
        ClienteId = cliente,
        Regiao = HubPedidos.Domain.Pedidos.Regiao.Sul,
        Prioridade = HubPedidos.Domain.Pedidos.Prioridade.Normal,
        Categoria = HubPedidos.Domain.Pedidos.CategoriaProcessamento.Padrao,
        MotivoDaCategoria = "m",
        Processador = "P v1",
        Situacao = HubPedidos.Domain.Pedidos.SituacaoPedido.Recebido,
        CriadoEmUtc = DateTime.UtcNow,
        ValorTotalCentavos = 100,
        Endereco = endereco,
        Itens = [new ItemPedidoRegistro { Posicao = 0, Sku = "A", Quantidade = 1, PrecoUnitarioCentavos = 100 }],
    };
}
