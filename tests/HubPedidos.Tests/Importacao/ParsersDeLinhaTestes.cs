using System.Text;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Tests.Importacao;

/// <summary>As três implementações do parser precisam ser funcionalmente equivalentes.</summary>
[TestClass]
public sealed class ParsersDeLinhaTestes
{
    private static readonly Func<string, int, LinhaImportada>[] Parsers =
    [
        (linha, numero) => ParserDeLinhaInicial.Interpretar(linha, numero),
        (linha, numero) => ParserDeLinhaComSpan.Interpretar(linha, numero),
        (linha, numero) => ParserDeLinhaUtf8.Interpretar(Encoding.UTF8.GetBytes(linha), numero),
    ];

    [TestMethod]
    [DataRow("cliente-1;Sul;Normal;A:1:2.50", DisplayName = "um item")]
    [DataRow("  cliente-1 ;\tSudeste ; Urgente ;A:1:2,B:3:4.75 , C : 10 : 0.01  ", DisplayName = "espaços e tabulações nas bordas")]
    [DataRow("joão-ç;sudeste;normal;CAFÉ-ç:2:3.40\r", DisplayName = "UTF-8 multibyte, minúsculas e CR")]
    [DataRow("c;Marte;Talvez;A:-1:-2", DisplayName = "sintaxe válida com valores de negócio inválidos")]
    [DataRow(";;;A:1:1", DisplayName = "campos vazios (validados depois, no mapeador)")]
    public void Versoes_produzem_o_mesmo_DTO_para_linhas_validas(string linha)
    {
        var esperado = Parsers[0](linha, 7);

        Assert.IsTrue(esperado.Valida, esperado.Erro);
        foreach (var parser in Parsers[1..])
        {
            AssertLinhasEquivalentes(esperado, parser(linha, 7));
        }
    }

    [TestMethod]
    [DataRow("", MensagensDeImportacao.LinhaVazia)]
    [DataRow(" \t\r", MensagensDeImportacao.LinhaVazia)]
    [DataRow("c;Sul;Normal", MensagensDeImportacao.QuantidadeDeCampos)]
    [DataRow("c;Sul;Normal;A:1:1;extra", MensagensDeImportacao.QuantidadeDeCampos)]
    [DataRow("c;Sul;Normal;  ", MensagensDeImportacao.SemItens)]
    [DataRow("c;Sul;Normal;A:1", "O item 1 deve ter 3 partes separadas por ':': sku:quantidade:preco.")]
    [DataRow("c;Sul;Normal;A:1:1,B:1:1:1", "O item 2 deve ter 3 partes separadas por ':': sku:quantidade:preco.")]
    [DataRow("c;Sul;Normal;A:1:1,", "O item 2 deve ter 3 partes separadas por ':': sku:quantidade:preco.")]
    [DataRow("c;Sul;Normal;A:um:1", "O item 1 tem quantidade inválida (use um número inteiro).")]
    [DataRow("c;Sul;Normal;A:1.5:1", "O item 1 tem quantidade inválida (use um número inteiro).")]
    [DataRow("c;Sul;Normal;A:1:2,50", "O item 2 deve ter 3 partes separadas por ':': sku:quantidade:preco.")]
    [DataRow("c;Sul;Normal;A:1:1e3", "O item 1 tem preço inválido (use ponto como separador decimal).")]
    public void Versoes_produzem_a_mesma_mensagem_para_linhas_invalidas(string linha, string mensagem)
    {
        foreach (var parser in Parsers)
        {
            var resultado = parser(linha, 3);
            Assert.IsFalse(resultado.Valida);
            Assert.AreEqual(3, resultado.Numero);
            Assert.AreEqual(mensagem, resultado.Erro);
        }
    }

    [TestMethod]
    public void Parser_UTF8_rejeita_bytes_invalidos_somente_na_linha()
    {
        byte[] linha = [.. "c;Sul;Normal;A"u8, 0xC3, 0x28, .. ":1:1"u8];

        var resultado = ParserDeLinhaUtf8.Interpretar(linha, 1);

        Assert.AreEqual(MensagensDeImportacao.Utf8Invalido, resultado.Erro);
    }

    [TestMethod]
    public void Nomes_conhecidos_reutilizam_a_mesma_string()
    {
        var primeira = ParserDeLinhaComSpan.Interpretar("a;Sudeste;Urgente;A:1:1", 1);
        var segunda = ParserDeLinhaUtf8.Interpretar("b;Sudeste;Urgente;A:1:1"u8, 2);

        Assert.AreSame(primeira.Pedido!.Regiao, segunda.Pedido!.Regiao);
        Assert.AreSame(primeira.Pedido.Prioridade, segunda.Pedido.Prioridade);
    }

    internal static void AssertLinhasEquivalentes(LinhaImportada esperado, LinhaImportada atual)
    {
        Assert.AreEqual(esperado.Numero, atual.Numero);
        Assert.AreEqual(esperado.Erro, atual.Erro);
        Assert.AreEqual(esperado.Valida, atual.Valida);
        if (!esperado.Valida)
        {
            return;
        }

        // O record compara a lista por referência; aqui comparamos o conteúdo.
        Assert.AreEqual(esperado.Pedido!.ClienteId, atual.Pedido!.ClienteId);
        Assert.AreEqual(esperado.Pedido.Regiao, atual.Pedido.Regiao);
        Assert.AreEqual(esperado.Pedido.Prioridade, atual.Pedido.Prioridade);
        CollectionAssert.AreEqual(esperado.Pedido.Itens!.ToArray(), atual.Pedido.Itens!.ToArray());
    }
}
