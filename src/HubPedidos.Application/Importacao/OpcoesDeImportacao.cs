namespace HubPedidos.Application.Importacao;

/// <summary>Limites da importação de lotes. Todos os tamanhos são em bytes.</summary>
public sealed class OpcoesDeImportacao
{
    /// <summary>Uma linha (pedido) não pode passar disto; acima, vira erro só daquela linha.</summary>
    public int TamanhoMaximoDaLinha { get; init; } = 8 * 1024;

    /// <summary>O lote inteiro não pode passar disto; acima, a importação é interrompida.</summary>
    public long TamanhoMaximoTotal { get; init; } = 10 * 1024 * 1024;

    /// <summary>Tamanho do buffer alugado para leitura incremental.</summary>
    public int TamanhoDoBuffer { get; init; } = 16 * 1024;

    /// <summary>
    /// Limpa a região usada do buffer antes de devolvê-lo ao pool. Ativado por padrão
    /// porque as linhas contêm o identificador do cliente (dado pessoal): sem limpeza,
    /// outro componente que alugasse o mesmo array poderia ler esses bytes.
    /// O custo é proporcional à região usada (no máximo <see cref="TamanhoDoBuffer"/>) e
    /// acontece uma vez por importação, não por linha.
    /// </summary>
    public bool LimparAoDevolver { get; init; } = true;

    /// <summary>Quantidade de resultados acumulados antes de cada flush da resposta.</summary>
    public int ResultadosPorEscrita { get; init; } = 100;

    public static OpcoesDeImportacao Padrao { get; } = new();

    public void Validar()
    {
        if (TamanhoMaximoDaLinha < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(TamanhoMaximoDaLinha), "O limite de linha deve ser de pelo menos 16 bytes.");
        }

        // Uma linha completa (mais o terminador CRLF) precisa caber no buffer.
        if (TamanhoDoBuffer < TamanhoMaximoDaLinha + 2)
        {
            throw new ArgumentOutOfRangeException(nameof(TamanhoDoBuffer), "O buffer deve comportar ao menos uma linha de tamanho máximo.");
        }

        if (TamanhoMaximoTotal < TamanhoMaximoDaLinha)
        {
            throw new ArgumentOutOfRangeException(nameof(TamanhoMaximoTotal), "O limite total deve ser maior que o limite de linha.");
        }

        if (ResultadosPorEscrita < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ResultadosPorEscrita), "Deve haver ao menos um resultado por escrita.");
        }
    }
}

/// <summary>O lote ultrapassou <see cref="OpcoesDeImportacao.TamanhoMaximoTotal"/>.</summary>
public sealed class LimiteDeImportacaoExcedidoException(long limiteEmBytes)
    : IOException($"O lote excede o limite de {limiteEmBytes} bytes.")
{
    public long LimiteEmBytes { get; } = limiteEmBytes;
}
