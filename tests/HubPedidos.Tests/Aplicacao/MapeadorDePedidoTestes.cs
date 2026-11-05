using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests.Aplicacao;

[TestClass]
public sealed class MapeadorDePedidoTestes
{
    private static readonly PedidoId IdFixo = new(Guid.Parse("0199f3c2-7c4e-7a10-9d2b-3f1a2b3c4d5e"));

    [TestMethod]
    public void DTO_valido_gera_entidade_com_identidade_da_aplicacao()
    {
        var requisicao = new CriarPedidoRequest(
            "cliente-1",
            "sudeste",
            "URGENTE",
            [new ItemPedidoRequest("TECLADO", 2, 150m), new ItemPedidoRequest("MOUSE", 1, 80m)]);

        var resultado = MapeadorDePedido.ParaDominio(requisicao, IdFixo);

        Assert.IsTrue(resultado.Sucesso);
        Assert.IsEmpty(resultado.Erros);
        Assert.AreEqual(IdFixo, resultado.Valor.Id);
        Assert.AreEqual(Regiao.Sudeste, resultado.Valor.Regiao);
        Assert.AreEqual(Prioridade.Urgente, resultado.Valor.Prioridade);
        Assert.AreEqual(380m, resultado.Valor.ValorTotal);
    }

    [TestMethod]
    public void DTO_invalido_e_rejeitado_antes_de_criar_o_objeto_de_dominio()
    {
        // Cada um destes valores faria um construtor do domínio lançar exceção.
        // O mapeador devolve todos os erros sem lançar nada: a prova de que a
        // validação aconteceu antes de qualquer ItemPedido/Pedido ser construído.
        var requisicao = new CriarPedidoRequest(
            ClienteId: "",
            Regiao: "Marte",
            Prioridade: "5",
            Itens: [new ItemPedidoRequest("", 0, 10.999m)]);

        var resultado = MapeadorDePedido.ParaDominio(requisicao, IdFixo);

        Assert.IsFalse(resultado.Sucesso);
        Assert.IsNull(resultado.Valor);
        CollectionAssert.AreEquivalent(
            new[] { "clienteId", "regiao", "prioridade", "itens[0].sku", "itens[0].quantidade", "itens[0].precoUnitario" },
            resultado.Erros.Select(e => e.Campo).ToArray());
    }

    [TestMethod]
    [DataRow(null, DisplayName = "ausente")]
    [DataRow("Sul,Norte", DisplayName = "combinação de flags")]
    [DataRow("4", DisplayName = "valor numérico")]
    [DataRow("Atlantida", DisplayName = "nome inexistente")]
    public void Regiao_aceita_somente_nome_definido(string? regiao)
    {
        var requisicao = new CriarPedidoRequest("c", regiao, "Normal", [new ItemPedidoRequest("A", 1, 1m)]);

        var resultado = MapeadorDePedido.ParaDominio(requisicao, IdFixo);

        Assert.IsFalse(resultado.Sucesso);
        Assert.AreEqual("regiao", resultado.Erros.Single().Campo);
    }

    [TestMethod]
    public void Corpo_ausente_e_lista_vazia_sao_rejeitados()
    {
        Assert.AreEqual("$", MapeadorDePedido.ParaDominio(null, IdFixo).Erros.Single().Campo);
        Assert.AreEqual(
            "itens",
            MapeadorDePedido.ParaDominio(new CriarPedidoRequest("c", "Sul", "Normal", []), IdFixo).Erros.Single().Campo);
    }

    [TestMethod]
    public void Alterar_a_lista_da_requisicao_depois_do_mapeamento_nao_altera_a_entidade()
    {
        var itens = new List<ItemPedidoRequest> { new("A", 1, 10m) };
        var resultado = MapeadorDePedido.ParaDominio(new CriarPedidoRequest("c", "Sul", "Normal", itens), IdFixo);

        itens.Add(new ItemPedidoRequest("B", 5, 5m));

        Assert.HasCount(1, resultado.Valor!.Itens);
    }

    [TestMethod]
    public void Resposta_e_snapshot_independente_e_somente_leitura()
    {
        var pedido = MapeadorDePedido.ParaDominio(
            new CriarPedidoRequest("c", "Sul", "Normal", [new ItemPedidoRequest("A", 2, 10m)]), IdFixo).Valor!;

        var resposta = MapeadorDePedido.ParaResposta(
            pedido,
            ClassificadorDePedidos.Classificar(pedido),
            new ResultadoProcessamento("P", 1, 5, "obs"));

        Assert.AreEqual(IdFixo.ToString(), resposta.Id);
        Assert.AreEqual("Padrao", resposta.Categoria);
        Assert.AreEqual("P v1", resposta.Processador);
        Assert.AreNotSame<object>(pedido.Itens, resposta.Itens);
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<ItemPedidoResponse>)resposta.Itens).Add(new ItemPedidoResponse("X", 1, 1m, 1m)));
    }
}
