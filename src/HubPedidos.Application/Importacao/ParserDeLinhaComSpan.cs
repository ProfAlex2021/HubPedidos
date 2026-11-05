using System.Globalization;
using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Versão com <see cref="ReadOnlySpan{T}"/> de <c>char</c>: os campos são recortes
/// (views) da linha original. Não há <c>Split</c>, nem arrays de campos, nem
/// substrings temporárias; os números são convertidos direto da região.
/// </summary>
/// <remarks>
/// Só é materializado como string o que precisa sobreviver ao método e compor o
/// <see cref="CriarPedidoRequest"/>: cliente, região, prioridade e SKUs. Nenhum span
/// escapa deste método síncrono.
/// </remarks>
public static class ParserDeLinhaComSpan
{
    private static ReadOnlySpan<char> Espacos => " \t";

    public static LinhaImportada Interpretar(ReadOnlySpan<char> linha, int numero)
    {
        if (!linha.IsEmpty && linha[^1] == '\r')
        {
            linha = linha[..^1];
        }

        if (linha.Trim(Espacos).IsEmpty)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.LinhaVazia);
        }

        // Split em Span<Range>: só índices, nenhuma string. Um destino com 5 posições
        // detecta tanto "campos de menos" quanto "campos demais".
        Span<Range> campos = stackalloc Range[5];
        if (linha.Split(campos, ';') != 4)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.QuantidadeDeCampos);
        }

        var itensTexto = linha[campos[3]].Trim(Espacos);
        if (itensTexto.IsEmpty)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.SemItens);
        }

        var itens = new List<ItemPedidoRequest>(itensTexto.Count(',') + 1);
        Span<Range> partes = stackalloc Range[4];
        var posicao = 0;
        foreach (var faixa in itensTexto.Split(','))
        {
            posicao++;
            var item = itensTexto[faixa];
            if (item.Split(partes, ':') != 3)
            {
                return LinhaImportada.Falha(numero, MensagensDeImportacao.ItemMalformado(posicao));
            }

            if (!int.TryParse(item[partes[1]].Trim(Espacos), FormatoNumerico.Inteiro, CultureInfo.InvariantCulture, out var quantidade))
            {
                return LinhaImportada.Falha(numero, MensagensDeImportacao.QuantidadeInvalida(posicao));
            }

            if (!decimal.TryParse(item[partes[2]].Trim(Espacos), FormatoNumerico.Decimal, CultureInfo.InvariantCulture, out var preco))
            {
                return LinhaImportada.Falha(numero, MensagensDeImportacao.PrecoInvalido(posicao));
            }

            itens.Add(new ItemPedidoRequest(item[partes[0]].Trim(Espacos).ToString(), quantidade, preco));
        }

        return LinhaImportada.Ok(
            numero,
            new CriarPedidoRequest(
                linha[campos[0]].Trim(Espacos).ToString(),
                NomesConhecidos.Obter(linha[campos[1]].Trim(Espacos)),
                NomesConhecidos.Obter(linha[campos[2]].Trim(Espacos)),
                itens));
    }
}

/// <summary>
/// Região e prioridade se repetem em todas as linhas. Quando o texto coincide
/// exatamente (ordinal) com um nome conhecido, reutiliza a string constante em vez
/// de alocar uma nova. Comparação ordinal: "sudeste" continua sendo uma string nova,
/// para que o resultado seja idêntico ao da linha de base.
/// </summary>
internal static class NomesConhecidos
{
    private static readonly string[] Nomes =
        ["Norte", "Nordeste", "CentroOeste", "Sudeste", "Sul", "Normal", "Alta", "Urgente"];

    public static string Obter(ReadOnlySpan<char> texto)
    {
        foreach (var nome in Nomes)
        {
            if (texto.SequenceEqual(nome))
            {
                return nome;
            }
        }

        return texto.ToString();
    }

    public static string Obter(ReadOnlySpan<byte> utf8, Func<ReadOnlySpan<byte>, string> decodificar)
    {
        // Os nomes conhecidos são ASCII; comparar byte a byte com o texto ASCII é exato.
        foreach (var nome in Nomes)
        {
            if (utf8.Length == nome.Length && System.Text.Ascii.Equals(utf8, nome))
            {
                return nome;
            }
        }

        return decodificar(utf8);
    }
}
