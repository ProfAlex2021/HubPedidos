using System.Globalization;
using System.Text;
using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Extensão do Roteiro 2: interpreta a linha direto dos bytes UTF-8 recebidos,
/// sem decodificar a linha inteira para <c>string</c>/<c>char</c> antes.
/// </summary>
/// <remarks>
/// Os separadores (<c>; , : \r \n</c>) são ASCII e nunca aparecem dentro de uma
/// sequência multibyte UTF-8, por isso é seguro procurá-los byte a byte. Os números
/// usam as sobrecargas UTF-8 de <c>int/decimal.TryParse</c> (.NET 8+). Somente os
/// textos que compõem o DTO são decodificados, com UTF-8 estrito.
/// </remarks>
public static class ParserDeLinhaUtf8
{
    private static readonly UTF8Encoding Utf8Estrito = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    // Delegate criado UMA vez. Passar o grupo de métodos "Utf8Estrito.GetString" a cada
    // chamada criava um Func novo por campo: o benchmark mostrou ~125 KB extras a cada
    // 1.000 linhas (ver docs/roteiros/roteiro-02.md, "Ajuste guiado pela medição").
    private static readonly Func<ReadOnlySpan<byte>, string> Decodificar = Utf8Estrito.GetString;

    private static ReadOnlySpan<byte> Espacos => " \t"u8;

    public static LinhaImportada Interpretar(ReadOnlySpan<byte> linha, int numero)
    {
        if (!linha.IsEmpty && linha[^1] == (byte)'\r')
        {
            linha = linha[..^1];
        }

        if (linha.Trim(Espacos).IsEmpty)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.LinhaVazia);
        }

        // Exatamente 3 separadores ';' => 4 campos.
        if (linha.Count((byte)';') != 3)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.QuantidadeDeCampos);
        }

        var resto = linha;
        var cliente = ProximoCampo(ref resto, (byte)';');
        var regiao = ProximoCampo(ref resto, (byte)';');
        var prioridade = ProximoCampo(ref resto, (byte)';');
        var itensTexto = resto.Trim(Espacos);
        if (itensTexto.IsEmpty)
        {
            return LinhaImportada.Falha(numero, MensagensDeImportacao.SemItens);
        }

        try
        {
            var itens = new List<ItemPedidoRequest>(itensTexto.Count((byte)',') + 1);
            var posicao = 0;
            var restoDosItens = itensTexto;
            bool continuar;
            do
            {
                posicao++;
                var separador = restoDosItens.IndexOf((byte)',');
                continuar = separador >= 0;
                var item = continuar ? restoDosItens[..separador] : restoDosItens;
                restoDosItens = continuar ? restoDosItens[(separador + 1)..] : default;

                if (item.Count((byte)':') != 2)
                {
                    return LinhaImportada.Falha(numero, MensagensDeImportacao.ItemMalformado(posicao));
                }

                var sku = ProximoCampo(ref item, (byte)':');
                var quantidadeTexto = ProximoCampo(ref item, (byte)':');
                if (!int.TryParse(quantidadeTexto, FormatoNumerico.Inteiro, CultureInfo.InvariantCulture, out var quantidade))
                {
                    return LinhaImportada.Falha(numero, MensagensDeImportacao.QuantidadeInvalida(posicao));
                }

                if (!decimal.TryParse(item.Trim(Espacos), FormatoNumerico.Decimal, CultureInfo.InvariantCulture, out var preco))
                {
                    return LinhaImportada.Falha(numero, MensagensDeImportacao.PrecoInvalido(posicao));
                }

                itens.Add(new ItemPedidoRequest(Utf8Estrito.GetString(sku), quantidade, preco));
            }
            while (continuar);

            return LinhaImportada.Ok(
                numero,
                new CriarPedidoRequest(
                    Utf8Estrito.GetString(cliente),
                    NomesConhecidos.Obter(regiao, Decodificar),
                    NomesConhecidos.Obter(prioridade, Decodificar),
                    itens));
        }
        catch (DecoderFallbackException)
        {
            // Bytes inválidos afetam somente esta linha; o restante do lote continua.
            return LinhaImportada.Falha(numero, MensagensDeImportacao.Utf8Invalido);
        }
    }

    /// <summary>Devolve o campo até o separador (sem espaços nas bordas) e avança o restante.</summary>
    private static ReadOnlySpan<byte> ProximoCampo(ref ReadOnlySpan<byte> resto, byte separador)
    {
        var indice = resto.IndexOf(separador);
        var campo = resto[..indice];
        resto = resto[(indice + 1)..];
        return campo.Trim(Espacos);
    }
}
