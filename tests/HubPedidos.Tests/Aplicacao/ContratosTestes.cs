using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Tests.Aplicacao;

/// <summary>Igualdade e cópia de records (DTOs imutáveis).</summary>
[TestClass]
public sealed class ContratosTestes
{
    [TestMethod]
    public void Record_copiado_com_with_nao_altera_o_original()
    {
        var original = CriarResposta();

        var copia = original with { Categoria = "Expressa", PrazoEstimadoEmDias = 1 };

        Assert.AreNotSame(original, copia);
        Assert.AreEqual("Padrao", original.Categoria);
        Assert.AreEqual(5, original.PrazoEstimadoEmDias);
        Assert.AreEqual("Expressa", copia.Categoria);
        // with é uma cópia superficial: a coleção não substituída é a mesma referência.
        Assert.AreSame(original.Itens, copia.Itens);
    }

    [TestMethod]
    public void Records_com_mesmos_componentes_sao_iguais_por_valor()
    {
        var primeiro = new ItemPedidoRequest("TECLADO", 2, 150m);
        var segundo = new ItemPedidoRequest("TECLADO", 2, 150m);

        Assert.AreEqual(primeiro, segundo);
        Assert.AreNotSame(primeiro, segundo);
        Assert.AreNotEqual(primeiro, primeiro with { Quantidade = 3 });
    }

    [TestMethod]
    public void Igualdade_de_record_nao_e_profunda_para_colecoes()
    {
        // Duas respostas com itens equivalentes, mas em arrays diferentes, não são iguais:
        // o record compara a referência da coleção, não seu conteúdo.
        var primeira = CriarResposta();
        var segunda = primeira with { Itens = [.. primeira.Itens] };

        Assert.IsTrue(primeira.Itens.SequenceEqual(segunda.Itens));
        Assert.AreNotEqual(primeira, segunda);
    }

    private static PedidoResponse CriarResposta() => new()
    {
        Id = "0199f3c2-7c4e-7a10-9d2b-3f1a2b3c4d5e",
        ClienteId = "c",
        Regiao = "Sul",
        Prioridade = "Normal",
        Categoria = "Padrao",
        MotivoDaCategoria = "m",
        Processador = "ProcessadorPadrao v1",
        PrazoEstimadoEmDias = 5,
        ValorTotal = 20m,
        QuantidadeDeItens = 2,
        Itens = Array.AsReadOnly([new ItemPedidoResponse("A", 2, 10m, 20m)]),
    };
}
