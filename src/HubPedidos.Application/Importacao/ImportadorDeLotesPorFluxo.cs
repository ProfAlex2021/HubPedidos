using System.Buffers;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Importação incremental: lê o stream em blocos para um buffer alugado, interpreta
/// cada linha completa direto dos bytes UTF-8 e entrega o resultado por callback
/// antes de ler a próxima linha.
/// </summary>
/// <remarks>
/// Contrato de memória:
/// - um único buffer de <see cref="OpcoesDeImportacao.TamanhoDoBuffer"/> bytes, alugado
///   de <see cref="ArrayPool{T}"/> ou de <see cref="MemoryPool{T}"/> (extensão), e devolvido
///   no <c>finally</c> mesmo em erro ou cancelamento;
/// - somente a fatia válida <c>[inicio..fim)</c> é lida: o conteúdo restante do array
///   alugado pode ser lixo de outro uso;
/// - nenhum <see cref="Span{T}"/> atravessa um <c>await</c>: o buffer fica em
///   <see cref="Memory{T}"/> durante a leitura assíncrona, e o span é criado só para a
///   interpretação síncrona de cada linha;
/// - o callback recebe um DTO com strings próprias: nada referencia o buffer depois
///   da devolução.
///
/// Backpressure: o callback é aguardado antes de interpretar a próxima linha; não
/// existe fila de resultados pendentes.
/// </remarks>
public sealed class ImportadorDeLotesPorFluxo
{
    private static ReadOnlySpan<byte> MarcaDeOrdemDeBytes => [0xEF, 0xBB, 0xBF];

    private readonly OpcoesDeImportacao _opcoes;
    private readonly ArrayPool<byte>? _arrayPool;
    private readonly MemoryPool<byte>? _memoryPool;

    /// <summary>Usa <see cref="ArrayPool{T}"/> (padrão: <see cref="ArrayPool{T}.Shared"/>).</summary>
    public ImportadorDeLotesPorFluxo(OpcoesDeImportacao? opcoes = null, ArrayPool<byte>? pool = null)
    {
        _opcoes = opcoes ?? OpcoesDeImportacao.Padrao;
        _opcoes.Validar();
        _arrayPool = pool ?? ArrayPool<byte>.Shared;
    }

    private ImportadorDeLotesPorFluxo(OpcoesDeImportacao opcoes, MemoryPool<byte> memoryPool)
    {
        _opcoes = opcoes;
        _opcoes.Validar();
        _memoryPool = memoryPool;
    }

    /// <summary>
    /// Extensão do Roteiro 2: a mesma importação com <see cref="MemoryPool{T}"/>. O pool
    /// entrega um <see cref="IMemoryOwner{T}"/>, tornando explícito quem é o dono do
    /// buffer e quando ele termina (Dispose).
    /// </summary>
    public static ImportadorDeLotesPorFluxo ComMemoryPool(MemoryPool<byte>? pool = null, OpcoesDeImportacao? opcoes = null) =>
        new(opcoes ?? OpcoesDeImportacao.Padrao, pool ?? MemoryPool<byte>.Shared);

    public OpcoesDeImportacao Opcoes => _opcoes;

    public string EstrategiaDeBuffer => _memoryPool is null ? "ArrayPool<byte>" : "MemoryPool<byte>";

    /// <returns>Quantidade de linhas entregues ao callback.</returns>
    public async Task<int> ImportarAsync(
        Stream origem,
        Func<LinhaImportada, CancellationToken, ValueTask> entregarAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(entregarAsync);
        if (!origem.CanRead)
        {
            throw new ArgumentException("O stream de origem precisa permitir leitura.", nameof(origem));
        }

        var tamanho = _opcoes.TamanhoDoBuffer;

        // Aluguel conforme a estratégia. O array/bloco pode ser MAIOR que o pedido;
        // trabalhamos somente com os primeiros "tamanho" bytes.
        byte[]? arrayAlugado = null;
        IMemoryOwner<byte>? proprietario = null;
        Memory<byte> buffer;
        if (_memoryPool is not null)
        {
            proprietario = _memoryPool.Rent(tamanho);
            buffer = proprietario.Memory[..tamanho];
        }
        else
        {
            arrayAlugado = _arrayPool!.Rent(tamanho);
            buffer = arrayAlugado.AsMemory(0, tamanho);
        }

        var regiaoUsada = 0;
        try
        {
            var inicio = 0;      // primeiro byte ainda não consumido
            var fim = 0;         // fim dos bytes válidos no buffer
            long totalLido = 0;
            var numero = 0;
            var entregues = 0;
            var descartandoLinhaLonga = false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (inicio > 0)
                {
                    // Compacta: a linha incompleta vai para o início, liberando espaço no fim.
                    buffer[inicio..fim].CopyTo(buffer);
                    fim -= inicio;
                    inicio = 0;
                }

                if (fim == tamanho)
                {
                    // Buffer cheio sem '\n': a linha é maior que o buffer e, portanto, maior que o limite.
                    // Reporta uma vez e descarta bytes até o próximo terminador.
                    if (!descartandoLinhaLonga)
                    {
                        numero++;
                        await entregarAsync(LinhaImportada.Falha(numero, MensagensDeImportacao.LinhaLonga(_opcoes.TamanhoMaximoDaLinha)), cancellationToken);
                        entregues++;
                        descartandoLinhaLonga = true;
                    }

                    fim = 0;
                }

                var lidos = await origem.ReadAsync(buffer[fim..], cancellationToken);
                if (lidos == 0)
                {
                    break; // somente 0 indica fim; uma leitura parcial positiva não encerra o stream.
                }

                totalLido += lidos;
                if (totalLido > _opcoes.TamanhoMaximoTotal)
                {
                    // Interrompe assim que o limite é ultrapassado: no máximo um bloco além dele foi lido.
                    throw new LimiteDeImportacaoExcedidoException(_opcoes.TamanhoMaximoTotal);
                }

                fim += lidos;
                regiaoUsada = Math.Max(regiaoUsada, fim);

                // Entrega todas as linhas completas já disponíveis no buffer.
                while (ProcurarTerminador(buffer.Span, inicio, fim) is var comprimento and >= 0)
                {
                    var inicioDaLinha = inicio;
                    inicio += comprimento + 1;

                    if (descartandoLinhaLonga)
                    {
                        descartandoLinhaLonga = false; // o resto da linha longa terminou aqui
                        continue;
                    }

                    numero++;
                    // O span existe só durante a chamada síncrona; o resultado já tem strings próprias.
                    var resultado = Interpretar(buffer.Span.Slice(inicioDaLinha, comprimento), numero);
                    await entregarAsync(resultado, cancellationToken);
                    entregues++;
                }
            }

            // A última linha pode não ter terminador.
            if (fim > inicio && !descartandoLinhaLonga)
            {
                numero++;
                await entregarAsync(Interpretar(buffer.Span[inicio..fim], numero), cancellationToken);
                entregues++;
            }

            return entregues;
        }
        finally
        {
            // Executa em sucesso, exceção e cancelamento.
            if (_opcoes.LimparAoDevolver)
            {
                buffer.Span[..regiaoUsada].Clear(); // limpa só a região realmente usada
            }

            if (arrayAlugado is not null)
            {
                _arrayPool!.Return(arrayAlugado);
            }

            proprietario?.Dispose();
        }
    }

    private static int ProcurarTerminador(ReadOnlySpan<byte> buffer, int inicio, int fim) =>
        buffer[inicio..fim].IndexOf((byte)'\n');

    private LinhaImportada Interpretar(ReadOnlySpan<byte> linha, int numero)
    {
        if (numero == 1 && linha.StartsWith(MarcaDeOrdemDeBytes))
        {
            linha = linha[MarcaDeOrdemDeBytes.Length..];
        }

        var semCr = !linha.IsEmpty && linha[^1] == (byte)'\r' ? linha[..^1] : linha;
        return semCr.Length > _opcoes.TamanhoMaximoDaLinha
            ? LinhaImportada.Falha(numero, MensagensDeImportacao.LinhaLonga(_opcoes.TamanhoMaximoDaLinha))
            : ParserDeLinhaUtf8.Interpretar(linha, numero);
    }
}
