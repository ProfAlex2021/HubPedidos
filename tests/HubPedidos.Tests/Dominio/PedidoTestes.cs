using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests.Dominio;

[TestClass]
public sealed class PedidoTestes
{
    [TestMethod]
    public void Pedido_calcula_totais_a_partir_dos_itens()
    {
        var pedido = CriarPedido([new ItemPedido("A", 2, 10.50m), new ItemPedido("B", 3, 1m)]);

        Assert.AreEqual(24m, pedido.ValorTotal);
        Assert.AreEqual(5, pedido.QuantidadeDeItens);
    }

    [TestMethod]
    public void Pedido_nao_compartilha_a_colecao_mutavel_recebida()
    {
        var itensDoChamador = new List<ItemPedido> { new("A", 1, 10m) };
        var pedido = CriarPedido(itensDoChamador);

        // Uma alteração posterior na lista do chamador não chega à entidade.
        itensDoChamador.Add(new ItemPedido("B", 1, 99m));
        itensDoChamador.Clear();

        Assert.HasCount(1, pedido.Itens);
        Assert.AreEqual(10m, pedido.ValorTotal);
    }

    [TestMethod]
    public void Colecao_exposta_nao_aceita_mutacao()
    {
        var pedido = CriarPedido([new ItemPedido("A", 1, 10m)]);
        var listaExposta = (IList<ItemPedido>)pedido.Itens;

        Assert.ThrowsExactly<NotSupportedException>(() => listaExposta.Add(new ItemPedido("B", 1, 1m)));
    }

    [TestMethod]
    public void Construtor_impede_identificador_cliente_e_lista_invalidos()
    {
        ItemPedido[] itens = [new("A", 1, 10m)];

        Assert.ThrowsExactly<ArgumentException>(() => new Pedido(default, "c", Regiao.Sul, Prioridade.Normal, itens));
        Assert.ThrowsExactly<ArgumentException>(() => new Pedido(PedidoId.Novo(), " ", Regiao.Sul, Prioridade.Normal, itens));
        Assert.ThrowsExactly<ArgumentException>(() => new Pedido(PedidoId.Novo(), "c", Regiao.Sul, Prioridade.Normal, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Pedido(PedidoId.Novo(), "c", (Regiao)9, Prioridade.Normal, itens));
    }

    [TestMethod]
    [DataRow("", 1, "1.00", DisplayName = "SKU vazio")]
    [DataRow("A", 0, "1.00", DisplayName = "quantidade zero")]
    [DataRow("A", 1001, "1.00", DisplayName = "quantidade acima do limite")]
    [DataRow("A", 1, "0", DisplayName = "preço zero")]
    [DataRow("A", 1, "100000.01", DisplayName = "preço acima do limite")]
    [DataRow("A", 1, "1.001", DisplayName = "preço com três casas")]
    public void Item_rejeita_valores_invalidos(string sku, int quantidade, string preco)
    {
        var precoDecimal = decimal.Parse(preco, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentException>(() => new ItemPedido(sku, quantidade, precoDecimal));
    }

    [TestMethod]
    public void Item_tem_igualdade_por_valor_e_identidade_distinta()
    {
        var primeiro = new ItemPedido("TECLADO", 2, 150m);
        var segundo = new ItemPedido(" TECLADO ", 2, 150m);

        Assert.AreEqual(primeiro, segundo);
        Assert.AreNotSame(primeiro, segundo);
        Assert.AreEqual(300m, primeiro.Subtotal);
    }

    private static Pedido CriarPedido(IEnumerable<ItemPedido> itens) =>
        new(PedidoId.Novo(), "cliente-1", Regiao.Sul, Prioridade.Normal, itens);
}
