using System.Buffers;
using System.Text;

namespace HubPedidos.Tests.Importacao.Apoio;

/// <summary>
/// Stream de memória que limita os bytes por leitura (simula rede/disco entregando
/// pouco de cada vez), registra quanto já foi lido e conta flushes.
/// </summary>
internal sealed class StreamObservavel(byte[]? conteudo = null, int maximoPorLeitura = int.MaxValue) : Stream
{
    private readonly MemoryStream _interno = conteudo is null ? new MemoryStream() : new MemoryStream(conteudo, writable: false);

    public long BytesLidos { get; private set; }

    public int Flushes { get; private set; }

    public bool FoiDescartado { get; private set; }

    public override bool CanRead => !FoiDescartado;

    public override bool CanSeek => false;

    public override bool CanWrite => !FoiDescartado && _interno.CanWrite;

    public override long Length => _interno.Length;

    public override long Position { get => _interno.Position; set => throw new NotSupportedException(); }

    public static StreamObservavel DeTexto(string texto, int maximoPorLeitura = int.MaxValue) =>
        new(new UTF8Encoding(false).GetBytes(texto), maximoPorLeitura);

    public string ObterTexto() => Encoding.UTF8.GetString(_interno.ToArray());

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var lidos = _interno.Read(buffer[..Math.Min(buffer.Length, maximoPorLeitura)]);
        BytesLidos += lidos;
        return lidos;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Write(byte[] buffer, int offset, int count) => _interno.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _interno.WriteAsync(buffer, cancellationToken);

    public override void Flush() => Flushes++;

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flushes++;
        return Task.CompletedTask;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        FoiDescartado = true;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Gera um lote grande sob demanda: o conteúdo inteiro nunca existe na memória,
/// nem no teste. Permite provar que o importador também não o materializa.
/// </summary>
internal sealed class StreamGeradorDeLote(int quantidadeDeLinhas) : Stream
{
    private byte[] _linhaAtual = [];
    private int _posicaoNaLinha;
    private int _proximaLinha = 1;

    public long BytesLidos { get; private set; }

    public int MaiorLeituraSolicitada { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => BytesLidos; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destino)
    {
        MaiorLeituraSolicitada = Math.Max(MaiorLeituraSolicitada, destino.Length);
        var escritos = 0;
        while (escritos < destino.Length)
        {
            if (_posicaoNaLinha == _linhaAtual.Length)
            {
                if (_proximaLinha > quantidadeDeLinhas)
                {
                    break;
                }

                _linhaAtual = Encoding.UTF8.GetBytes($"cliente-{_proximaLinha % 97};Sul;Normal;SKU-{_proximaLinha}:1:1.50\n");
                _posicaoNaLinha = 0;
                _proximaLinha++;
            }

            var quantidade = Math.Min(destino.Length - escritos, _linhaAtual.Length - _posicaoNaLinha);
            _linhaAtual.AsSpan(_posicaoNaLinha, quantidade).CopyTo(destino[escritos..]);
            _posicaoNaLinha += quantidade;
            escritos += quantidade;
        }

        BytesLidos += escritos;
        return escritos;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// ArrayPool que devolve arrays MAIORES que o pedido e preenchidos com lixo
/// (inclusive '\n'), e registra aluguel, devolução e o estado do array na devolução.
/// </summary>
internal sealed class ArrayPoolObservavel(int capacidadeExtra = 64) : ArrayPool<byte>
{
    public byte[]? Alugado { get; private set; }

    public byte[]? Devolvido { get; private set; }

    public int TamanhoSolicitado { get; private set; }

    public int Devolucoes { get; private set; }

    /// <summary>Cópia do conteúdo no momento da devolução (para verificar a limpeza).</summary>
    public byte[]? ConteudoNaDevolucao { get; private set; }

    public override byte[] Rent(int minimumLength)
    {
        TamanhoSolicitado = minimumLength;
        var array = new byte[minimumLength + capacidadeExtra];
        for (var i = 0; i < array.Length; i++)
        {
            array[i] = i % 10 == 0 ? (byte)'\n' : (byte)'#';
        }

        Alugado = array;
        return array;
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        Devolvido = array;
        Devolucoes++;
        ConteudoNaDevolucao = [.. array];
    }
}

/// <summary>MemoryPool que registra o descarte do proprietário do bloco.</summary>
internal sealed class MemoryPoolObservavel : MemoryPool<byte>
{
    public ProprietarioObservavel? Ultimo { get; private set; }

    public override int MaxBufferSize => int.MaxValue;

    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        Ultimo = new ProprietarioObservavel(new byte[Math.Max(minBufferSize, 1) + 32]);
        return Ultimo;
    }

    protected override void Dispose(bool disposing)
    {
    }

    internal sealed class ProprietarioObservavel(byte[] bloco) : IMemoryOwner<byte>
    {
        public bool Descartado { get; private set; }

        public Memory<byte> Memory => Descartado ? throw new ObjectDisposedException(nameof(ProprietarioObservavel)) : bloco;

        public void Dispose() => Descartado = true;
    }
}
