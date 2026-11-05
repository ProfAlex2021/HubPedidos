using System.Globalization;
using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Linha de base: interpreta a linha com APIs simples e corretas de string
/// (<c>Split</c>, <c>Trim</c>, <c>TryParse(string)</c>). Cada chamada cria arrays de
/// campos e várias strings intermediárias que só existem durante a análise.
/// </summary>
/// <remarks>
/// Formato: <c>cliente;regiao;prioridade;sku:quantidade:preco,sku:quantidade:preco</c>.
/// Espaços e tabulações nas bordas de cada parte são ignorados; números usam a
/// cultura invariável (ponto decimal). O parser verifica só a SINTAXE; as regras de
/// negócio (limites, região conhecida) continuam no <c>MapeadorDePedido</c>.
/// </remarks>
public static class ParserDeLinhaInicial
{
    internal static readonly char[] Espacos = [' ', '\t'];

    public static LinhaImportada Interpretar(string? linha, int numero)
    {
        var texto = (linha ?? string.Empty).TrimEnd('\r');
        if (texto.Trim(Espacos).Length == 0)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.LinhaVazia);
        }

        string[] campos = texto.Split(';');
        if (campos.Length != 4)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.QuantidadeDeCampos);
        }

        var itensTexto = campos[3].Trim(Espacos);
        if (itensTexto.Length == 0)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.SemItens);
        }

        string[] itensSeparados = itensTexto.Split(',');
        var itens = new List<ItemPedidoRequest>(itensSeparados.Length);
        for (var i = 0; i < itensSeparados.Length; i++)
        {
            string[] partes = itensSeparados[i].Split(':');
            if (partes.Length != 3)
            {
                return LinhaImportada.Falha(numero, MensagensDeImportacao.ItemMalformado(i + 1));
            }

            if (!int.TryParse(partes[1].Trim(Espacos), FormatoNumerico.Inteiro, CultureInfo.InvariantCulture, out var quantidade))
            {
                return LinhaImportada.Falha(numero, MensagensDeImportacao.QuantidadeInvalida(i + 1));
            }

            if (!decimal.TryParse(partes[2].Trim(Espacos), FormatoNumerico.Decimal, CultureInfo.InvariantCulture, out var preco))
            {
                return LinhaImportada.Falha(numero, MensagensDeImportacao.PrecoInvalido(i + 1));
            }

            itens.Add(new ItemPedidoRequest(partes[0].Trim(Espacos), quantidade, preco));
        }

        return LinhaImportada.Ok(
            numero,
            new CriarPedidoRequest(campos[0].Trim(Espacos), campos[1].Trim(Espacos), campos[2].Trim(Espacos), itens));
    }
}

/// <summary>Estilos numéricos comuns às três implementações.</summary>
internal static class FormatoNumerico
{
    public const NumberStyles Inteiro = NumberStyles.AllowLeadingSign;
    public const NumberStyles Decimal = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
}
