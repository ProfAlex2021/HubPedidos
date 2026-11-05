using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Application.Importacao;

/// <summary>Resultado de uma linha do lote, como aparece na resposta NDJSON.</summary>
public sealed record ResultadoDaLinhaDoLote(
    int Linha,
    string Status,
    string? PedidoId = null,
    string? Categoria = null,
    string? Erro = null,
    IReadOnlyList<ErroDeValidacao>? Erros = null);

/// <summary>Última linha da resposta: totais do lote.</summary>
public sealed record ResumoDoLote(string Tipo, int Linhas, int Aceitas, int ComErro, bool Interrompido, string? Motivo);

/// <summary>
/// Caso de uso da importação: liga o importador incremental, o caso de uso de
/// recepção do Roteiro 1 e o gravador NDJSON, sem reter o lote na memória.
/// </summary>
public sealed class ProcessadorDeLotes(ImportadorDeLotesPorFluxo importador, ServicoDeRecepcaoDePedidos servico)
{
    public async Task<ResumoDoLote> ProcessarAsync(Stream entrada, Stream saida, CancellationToken cancellationToken = default)
    {
        await using var gravador = new GravadorDeResultados(saida, importador.Opcoes.ResultadosPorEscrita);
        var linhas = 0;
        var aceitas = 0;

        async ValueTask EntregarAsync(LinhaImportada linha, CancellationToken token)
        {
            linhas++;
            var resultado = Converter(linha);
            if (resultado.Status == "aceito")
            {
                aceitas++;
            }

            await gravador.GravarAsync(resultado, token);
        }

        ResumoDoLote resumo;
        try
        {
            await importador.ImportarAsync(entrada, EntregarAsync, cancellationToken);
            resumo = new ResumoDoLote("resumo", linhas, aceitas, linhas - aceitas, Interrompido: false, Motivo: null);
        }
        catch (LimiteDeImportacaoExcedidoException excecao)
        {
            // A resposta já começou a ser enviada (200): a interrupção é informada na última linha.
            resumo = new ResumoDoLote("resumo", linhas, aceitas, linhas - aceitas, Interrompido: true, excecao.Message);
        }

        await gravador.GravarAsync(resumo, cancellationToken);
        await gravador.ConcluirAsync(cancellationToken);
        return resumo;
    }

    private ResultadoDaLinhaDoLote Converter(LinhaImportada linha)
    {
        if (!linha.Valida)
        {
            return new(linha.Numero, "erro-de-formato", Erro: linha.Erro);
        }

        return servico.Receber(linha.Pedido) switch
        {
            ResultadoDaRecepcao.Aceito aceito =>
                new(linha.Numero, "aceito", aceito.Pedido.Id, aceito.Pedido.Categoria),
            ResultadoDaRecepcao.DadosInvalidos invalidos =>
                new(linha.Numero, "invalido", Erros: invalidos.Erros),
            ResultadoDaRecepcao.Rejeitado rejeitado =>
                new(linha.Numero, "rejeitado", Erro: rejeitado.Motivo),
            _ => throw new InvalidOperationException("Resultado de recepção desconhecido."),
        };
    }
}
