using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests.Dominio;

/// <summary>Extensão do Roteiro 1: value object com IParsable&lt;T&gt;.</summary>
[TestClass]
public sealed class PedidoIdTestes
{
    [TestMethod]
    public void TryParse_aceita_guid_valido_e_preserva_o_valor()
    {
        const string texto = "0199f3c2-7c4e-7a10-9d2b-3f1a2b3c4d5e";

        Assert.IsTrue(PedidoId.TryParse(texto, provider: null, out var id));
        Assert.AreEqual(Guid.Parse(texto), id.Valor);
        Assert.AreEqual(texto, id.ToString());
    }

    [TestMethod]
    [DataRow(null, DisplayName = "nulo")]
    [DataRow("", DisplayName = "vazio")]
    [DataRow("abc", DisplayName = "texto qualquer")]
    [DataRow("00000000-0000-0000-0000-000000000000", DisplayName = "Guid.Empty")]
    public void TryParse_rejeita_entrada_invalida(string? texto)
    {
        Assert.IsFalse(PedidoId.TryParse(texto, out var id));
        Assert.IsFalse(id.EhValido);
    }

    [TestMethod]
    public void Parse_lanca_FormatException_com_mensagem_legivel()
    {
        var excecao = Assert.ThrowsExactly<FormatException>(() => PedidoId.Parse("xyz", provider: null));

        StringAssert.Contains(excecao.Message, "'xyz'");
    }

    [TestMethod]
    public void Igualdade_e_por_valor()
    {
        var guid = Guid.CreateVersion7();

        Assert.AreEqual(new PedidoId(guid), new PedidoId(guid));
        Assert.AreNotEqual(PedidoId.Novo(), PedidoId.Novo());
    }

    [TestMethod]
    public void Construtor_rejeita_guid_vazio_e_default_e_detectavel()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PedidoId(Guid.Empty));
        Assert.IsFalse(default(PedidoId).EhValido);
    }
}
