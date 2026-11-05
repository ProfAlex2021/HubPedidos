using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Escreve resultados em NDJSON (um objeto JSON por linha) agrupando a escrita:
/// os objetos são serializados em um buffer reutilizado e só vão para o stream,
/// com um flush, a cada <c>resultadosPorEscrita</c> itens.
/// </summary>
/// <remarks>
/// Flush por item antecipa a entrega, mas multiplica operações pequenas de E/S.
/// O lote preserva exatamente o mesmo conteúdo; muda só a frequência de escrita.
/// O destino pertence ao chamador e não é fechado aqui.
/// </remarks>
public sealed class GravadorDeResultados : IAsyncDisposable
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static ReadOnlySpan<byte> FimDeLinha => "\n"u8;

    private readonly Stream _destino;
    private readonly int _resultadosPorEscrita;
    private readonly ArrayBufferWriter<byte> _pendente = new(4 * 1024);
    private readonly Utf8JsonWriter _json;
    private int _quantidadePendente;
    private bool _descartado;

    public GravadorDeResultados(Stream destino, int resultadosPorEscrita)
    {
        ArgumentNullException.ThrowIfNull(destino);
        if (!destino.CanWrite)
        {
            throw new ArgumentException("O stream de destino precisa permitir escrita.", nameof(destino));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(resultadosPorEscrita, 1);
        _destino = destino;
        _resultadosPorEscrita = resultadosPorEscrita;
        _json = new Utf8JsonWriter(_pendente);
    }

    /// <summary>Quantas vezes os dados acumulados foram escritos e descarregados (flush).</summary>
    public int Descargas { get; private set; }

    public async ValueTask GravarAsync<T>(T resultado, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);

        JsonSerializer.Serialize(_json, resultado, OpcoesJson);
        _json.Flush();
        _pendente.Write(FimDeLinha);
        _json.Reset(); // permite o próximo objeto de nível superior no mesmo buffer
        _quantidadePendente++;

        if (_quantidadePendente >= _resultadosPorEscrita)
        {
            await DescarregarAsync(cancellationToken);
        }
    }

    /// <summary>Escreve o lote incompleto final.</summary>
    public async ValueTask ConcluirAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);
        if (_quantidadePendente > 0)
        {
            await DescarregarAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_descartado)
        {
            return;
        }

        // Garante que nada fique preso no buffer se o chamador esquecer ConcluirAsync.
        await ConcluirAsync(CancellationToken.None);
        _descartado = true;
        await _json.DisposeAsync();
    }

    private async ValueTask DescarregarAsync(CancellationToken cancellationToken)
    {
        await _destino.WriteAsync(_pendente.WrittenMemory, cancellationToken);
        await _destino.FlushAsync(cancellationToken);
        _pendente.ResetWrittenCount();
        _quantidadePendente = 0;
        Descargas++;
    }
}
