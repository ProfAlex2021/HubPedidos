using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Consultas;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Tests.Persistencia;

[TestClass]
public sealed class PersistenciaDePedidosTestes
{
    private static readonly DateTime Base = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Esquema_e_reproduzivel_pelas_migrations_e_corresponde_ao_modelo()
    {
        using var banco = new BancoDeTeste();
        using var contexto = banco.CriarContexto();

        // Todas as migrations do assembly foram aplicadas em ordem, e nenhuma ficou pendente.
        CollectionAssert.AreEqual(contexto.Database.GetMigrations().ToArray(), contexto.Database.GetAppliedMigrations().ToArray());
        Assert.IsEmpty(contexto.Database.GetPendingMigrations());
        StringAssert.EndsWith(contexto.Database.GetMigrations().First(), "_Inicial");
        // Se alguém mudar o modelo e esquecer a migration, este teste falha.
        Assert.IsFalse(contexto.Database.HasPendingModelChanges());
    }

    [TestMethod]
    public async Task Pedido_gravado_e_lido_preserva_centavos_endereco_itens_e_UTC()
    {
        using var banco = new BancoDeTeste();
        var pedido = await banco.InserirAsync("cliente-1", Base, precoUnitario: 19.99m, quantidade: 3,
            endereco: new EnderecoEntrega("Rua A, 10", "Campinas", "sp", "13000000"));

        await using var contexto = banco.CriarContexto();
        var detalhe = await new ConsultasDePedidos(contexto).ObterDetalheAsync(pedido.Id);

        Assert.IsNotNull(detalhe);
        Assert.AreEqual(pedido.ValorTotal, detalhe.ValorTotal);
        Assert.AreEqual(60.97m, detalhe.ValorTotal);
        CollectionAssert.AreEqual(new[] { "SKU-A", "SKU-B" }, detalhe.Itens.Select(i => i.Sku).ToArray());
        Assert.AreEqual(new EnderecoResponse("Rua A, 10", "Campinas", "SP", "13000000"), detalhe.Endereco);
        Assert.AreEqual(DateTimeKind.Utc, detalhe.CriadoEmUtc.Kind);
        Assert.AreEqual(Base, detalhe.CriadoEmUtc);
    }

    [TestMethod]
    public async Task Consulta_paginada_retorna_somente_colunas_e_linhas_necessarias()
    {
        using var banco = new BancoDeTeste();
        for (var i = 0; i < 45; i++)
        {
            // Cinco pedidos com a MESMA data: o desempate pelo número torna a ordem determinística.
            await banco.InserirAsync($"c{i}", i < 5 ? Base : Base.AddMinutes(i));
        }

        await using var contexto = banco.CriarContexto();
        var consultas = new ConsultasDePedidos(contexto);
        var filtro = new FiltroDePedidos();

        banco.Comandos.Limpar();
        var paginas = new List<PaginaDeResultados<PedidoResumo>>();
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            paginas.Add(await consultas.ListarResumosAsync(filtro, pagina, 20));
        }

        CollectionAssert.AreEqual(new[] { 20, 20, 5 }, paginas.Select(p => p.Itens.Count).ToArray());
        Assert.IsTrue(paginas.All(p => p.Total == 45 && p.TotalDePaginas == 3));
        var ids = paginas.SelectMany(p => p.Itens).Select(r => r.Id).ToArray();
        Assert.HasCount(45, ids.Distinct().ToArray()); // nenhuma linha repetida nem perdida entre páginas
        Assert.AreEqual(6, banco.Comandos.Quantidade); // COUNT + página, por página

        // O SQL limita linhas (LIMIT/OFFSET parametrizados) e não lê colunas que a tela não usa.
        var sql = consultas.ConsultaDeResumos(filtro).Skip(20).Take(20).ToQueryString();
        StringAssert.Contains(sql, "LIMIT");
        StringAssert.Contains(sql, "OFFSET");
        foreach (var colunaDesnecessaria in new[] { "Entrega_", "Processador", "MotivoDaCategoria", "Prioridade", "\"Sku\"" })
        {
            Assert.DoesNotContain(colunaDesnecessaria, sql);
        }
    }

    [TestMethod]
    public void Parametros_do_usuario_nao_alteram_a_forma_do_comando()
    {
        using var banco = new BancoDeTeste();
        using var contexto = banco.CriarContexto();
        var consultas = new ConsultasDePedidos(contexto);

        string SqlSemValores(decimal minimo, string cliente) => string.Join('\n',
            consultas.ConsultaDeResumos(new FiltroDePedidos(ValorMinimo: minimo, ClienteId: cliente, Categoria: "Padrao"))
                .ToQueryString().Split('\n').Where(l => !l.StartsWith(".param", StringComparison.Ordinal)));

        var primeiro = SqlSemValores(10m, "cliente-1");
        var segundo = SqlSemValores(999_999.99m, "x' OR '1'='1");

        Assert.AreEqual(primeiro, segundo); // mesma forma: só os valores dos parâmetros mudam
        StringAssert.Contains(primeiro, "@");
        Assert.DoesNotContain("99999999", primeiro);
        Assert.DoesNotContain("OR '1'='1", primeiro);
    }

    [TestMethod]
    public async Task Atualizacao_usa_consulta_com_tracking_e_altera_somente_a_situacao()
    {
        using var banco = new BancoDeTeste();
        var pedido = await banco.InserirAsync("c", Base);
        await using var contexto = banco.CriarContexto();
        var repositorio = new RepositorioDePedidos(contexto);

        banco.Comandos.Limpar();
        var resultado = await repositorio.CancelarAsync(pedido.Id);

        Assert.AreEqual(ResultadoDoCancelamento.Cancelado, resultado);
        // A entidade foi carregada COM tracking pelo mesmo contexto que salvou a mudança.
        Assert.IsTrue(contexto.ChangeTracker.Entries().Any());
        var update = banco.Comandos.Comandos.Single(c => c.StartsWith("UPDATE", StringComparison.Ordinal));
        StringAssert.Contains(update, "\"Situacao\"");
        Assert.DoesNotContain("ClienteId", update);
        Assert.AreEqual(ResultadoDoCancelamento.SituacaoNaoPermite, await repositorio.CancelarAsync(pedido.Id));
        Assert.AreEqual(ResultadoDoCancelamento.NaoEncontrado, await repositorio.CancelarAsync(PedidoId.Novo()));
    }

    [TestMethod]
    public async Task Alternativa_sem_tracking_atualiza_com_um_comando_explicito()
    {
        using var banco = new BancoDeTeste();
        var pedido = await banco.InserirAsync("c", Base);
        await using var contexto = banco.CriarContexto();
        var repositorio = new RepositorioDePedidos(contexto);

        banco.Comandos.Limpar();
        var resultado = await repositorio.CancelarSemTrackingAsync(pedido.Id);

        Assert.AreEqual(ResultadoDoCancelamento.Cancelado, resultado);
        Assert.IsFalse(contexto.ChangeTracker.Entries().Any()); // nada foi materializado nem rastreado
        Assert.AreEqual(1, banco.Comandos.Quantidade);
        StringAssert.StartsWith(banco.Comandos.Comandos[0], "UPDATE");
        Assert.AreEqual("Cancelado", (await new ConsultasDePedidos(contexto).ObterDetalheAsync(pedido.Id))!.Situacao);
        Assert.AreEqual(ResultadoDoCancelamento.SituacaoNaoPermite, await repositorio.CancelarSemTrackingAsync(pedido.Id));
    }

    [TestMethod]
    [DataRow(EstrategiaDeCarregamento.Projecao, 1)]
    [DataRow(EstrategiaDeCarregamento.Include, 1)]
    [DataRow(EstrategiaDeCarregamento.Dividida, 2)]
    public async Task Relatorio_de_pedidos_e_itens_nao_tem_N_mais_1(EstrategiaDeCarregamento estrategia, int comandosEsperados)
    {
        using var banco = new BancoDeTeste();
        for (var i = 0; i < 30; i++)
        {
            await banco.InserirAsync($"c{i}", Base.AddMinutes(i));
        }

        await using var contexto = banco.CriarContexto();
        var relatorio = await OrcamentoDeComandos.VerificarAsync(banco.Comandos, maximo: 2,
            () => new ConsultasDePedidos(contexto).ListarRelatorioAsync(1, 20, estrategia));

        Assert.HasCount(20, relatorio);
        Assert.IsTrue(relatorio.All(p => p.Itens.Count == 2));
        Assert.AreEqual(comandosEsperados, banco.Comandos.Quantidade);
    }

    [TestMethod]
    public async Task Orcamento_de_comandos_detecta_um_N_mais_1()
    {
        using var banco = new BancoDeTeste();
        for (var i = 0; i < 10; i++)
        {
            await banco.InserirAsync($"c{i}", Base.AddMinutes(i));
        }

        await using var contexto = banco.CriarContexto();
        var consultas = new ConsultasDePedidos(contexto);

        // Implementação ingênua: lista os pedidos e busca o detalhe de cada um (1 + N comandos).
        var excecao = await Assert.ThrowsExactlyAsync<OrcamentoDeComandosExcedidoException>(() =>
            OrcamentoDeComandos.VerificarAsync(banco.Comandos, maximo: 2, async () =>
            {
                var pagina = await consultas.ListarResumosAsync(new FiltroDePedidos(), 1, 10);
                var detalhes = new List<PedidoResponse>();
                foreach (var resumo in pagina.Itens)
                {
                    detalhes.Add((await consultas.ObterDetalheAsync(PedidoId.Parse(resumo.Id, null)))!);
                }

                return detalhes;
            }));

        StringAssert.Contains(excecao.Message, "12 foram enviados");
    }

    [TestMethod]
    public async Task Consulta_compilada_produz_o_mesmo_resultado_que_a_comum()
    {
        using var banco = new BancoDeTeste();
        var pedido = await banco.InserirAsync("c", Base, endereco: new EnderecoEntrega(null, "Recife", "PE", "50000000"));
        await using var contexto = banco.CriarContexto();
        var consultas = new ConsultasDePedidos(contexto);

        var comum = await consultas.ObterDetalheAsync(pedido.Id);
        var compilada = await consultas.ObterDetalheCompiladoAsync(pedido.Id);

        Assert.AreEqual(comum!.Id, compilada!.Id);
        Assert.AreEqual(comum.ValorTotal, compilada.ValorTotal);
        Assert.AreEqual(comum.Endereco, compilada.Endereco);
        CollectionAssert.AreEqual(comum.Itens.ToArray(), compilada.Itens.ToArray());
        Assert.IsNull(await consultas.ObterDetalheCompiladoAsync(PedidoId.Novo()));
    }

    [TestMethod]
    public async Task Paginacao_por_cursor_percorre_a_mesma_sequencia_que_o_deslocamento()
    {
        using var banco = new BancoDeTeste();
        for (var i = 0; i < 25; i++)
        {
            await banco.InserirAsync($"c{i}", i < 6 ? Base : Base.AddMinutes(i)); // empates incluídos
        }

        await using var contexto = banco.CriarContexto();
        var consultas = new ConsultasDePedidos(contexto);

        var porDeslocamento = (await consultas.ListarResumosAsync(new FiltroDePedidos(), 1, 25)).Itens.Select(r => r.Id).ToArray();
        var porCursor = new List<string>();
        string? cursor = null;
        do
        {
            var pagina = await consultas.ListarResumosPorCursorAsync(new FiltroDePedidos(), cursor, 7);
            porCursor.AddRange(pagina.Itens.Select(r => r.Id));
            cursor = pagina.ProximoCursor;
        }
        while (cursor is not null);

        CollectionAssert.AreEqual(porDeslocamento, porCursor);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => consultas.ListarResumosPorCursorAsync(new FiltroDePedidos(), "lixo!", 7));
    }

    [TestMethod]
    public async Task Filtros_por_periodo_categoria_e_valor_sao_aplicados_no_banco()
    {
        using var banco = new BancoDeTeste();
        await banco.InserirAsync("a", Base, precoUnitario: 50m);
        await banco.InserirAsync("b", Base.AddDays(2), precoUnitario: 50m);
        await banco.InserirAsync("c", Base.AddDays(2), precoUnitario: 6_000m, quantidade: 2); // AltoValor
        await using var contexto = banco.CriarContexto();
        var consultas = new ConsultasDePedidos(contexto);

        var recentes = await consultas.ListarResumosAsync(new FiltroDePedidos(CriadoDesdeUtc: Base.AddDays(1)), 1, 10);
        var altoValor = await consultas.ListarResumosAsync(new FiltroDePedidos(Categoria: "altovalor"), 1, 10);
        var acimaDe100 = await consultas.ListarResumosAsync(new FiltroDePedidos(ValorMinimo: 100m), 1, 10);

        Assert.AreEqual(2, recentes.Total);
        Assert.AreEqual("c", altoValor.Itens.Single().ClienteId);
        Assert.AreEqual("c", acimaDe100.Itens.Single().ClienteId);
        Assert.AreEqual(3, acimaDe100.Itens.Single().QuantidadeDeItens); // soma calculada no banco
    }
}
