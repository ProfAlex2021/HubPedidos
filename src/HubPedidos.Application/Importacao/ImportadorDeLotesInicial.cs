using System.Text;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Linha de base funcional: lê o lote inteiro para uma string, separa as linhas e
/// devolve a lista completa. Simples e correta para lotes pequenos; o primeiro
/// resultado só existe depois que todo o conteúdo foi lido e retido na memória.
/// </summary>
public sealed class ImportadorDeLotesInicial(OpcoesDeImportacao? opcoes = null)
{
    private static readonly UTF8Encoding Utf8Estrito = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly OpcoesDeImportacao _opcoes = opcoes ?? OpcoesDeImportacao.Padrao;

    public async Task<IReadOnlyList<LinhaImportada>> ImportarAsync(Stream origem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origem);

        // A origem pertence ao chamador: leaveOpen evita fechá-la.
        using var leitor = new StreamReader(origem, Utf8Estrito, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var texto = await leitor.ReadToEndAsync(cancellationToken);

        // O limite total só pode ser verificado DEPOIS de ler tudo: a proteção chega tarde.
        if (Utf8Estrito.GetByteCount(texto) > _opcoes.TamanhoMaximoTotal)
        {
            throw new LimiteDeImportacaoExcedidoException(_opcoes.TamanhoMaximoTotal);
        }

        if (texto.StartsWith('﻿'))
        {
            texto = texto[1..];
        }

        var linhas = texto.Split('\n');
        // Um terminador final não cria uma linha adicional.
        var quantidade = linhas.Length > 0 && linhas[^1].Length == 0 ? linhas.Length - 1 : linhas.Length;

        var resultados = new List<LinhaImportada>(quantidade);
        for (var i = 0; i < quantidade; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var linha = linhas[i];
            var numero = i + 1;
            var tamanho = Utf8Estrito.GetByteCount(linha.TrimEnd('\r'));
            resultados.Add(tamanho > _opcoes.TamanhoMaximoDaLinha
                ? LinhaImportada.Falha(numero, MensagensDeImportacao.LinhaLonga(_opcoes.TamanhoMaximoDaLinha))
                : ParserDeLinhaInicial.Interpretar(linha, numero));
        }

        return resultados;
    }
}
