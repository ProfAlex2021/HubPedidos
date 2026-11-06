using System.Collections.Concurrent;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento.Fila;

/// <summary>Resultado do processamento de um item.</summary>
public enum ResultadoDoProcessamento
{
    Processado,

    /// <summary>Já tinha sido processado (ou cancelado): nada foi alterado (idempotência).</summary>
    IgnoradoPorIdempotencia,

    NaoEncontrado,
}

/// <summary>Fotografia das métricas para a API e para os experimentos.</summary>
public sealed record InstantaneoDasMetricas(
    int Profundidade,
    long Aceitos,
    long Rejeitados,
    long Descartados,
    long Processados,
    long Ignorados,
    long NaoEncontrados,
    long Falhas,
    int EmAndamento,
    int PicoDeAcessoAoBanco,
    double EsperaMediaNaFilaMs,
    double EsperaMaximaNaFilaMs,
    double DuracaoMediaMs);

/// <summary>
/// Métricas compartilhadas por produtores e consumidores. Cada operação é atômica:
/// contadores com <see cref="Interlocked"/> e estado por pedido em
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> usando apenas TryAdd/TryRemove
/// (nunca "verificar e depois inserir" em duas chamadas separadas).
/// </summary>
public sealed class MetricasDoProcessamento
{
    private readonly ConcurrentDictionary<PedidoId, DateTime> _emAndamento = new();
    private readonly ConcurrentDictionary<ResultadoDoProcessamento, long> _porResultado = new();
    private long _aceitos;
    private long _rejeitados;
    private long _descartados;
    private long _falhas;
    private long _esperaTotalTicks;
    private long _esperaMaximaTicks;
    private long _esperasRegistradas;
    private long _duracaoTotalTicks;
    private long _duracoesRegistradas;

    public void RegistrarAceito() => Interlocked.Increment(ref _aceitos);

    public void RegistrarRejeitado() => Interlocked.Increment(ref _rejeitados);

    public void RegistrarDescarte(PedidoId pedidoId) => Interlocked.Increment(ref _descartados);

    public void RegistrarFalha() => Interlocked.Increment(ref _falhas);

    /// <summary>
    /// Marca o pedido como "em processamento". TryAdd é atômico: se dois consumidores
    /// receberem o mesmo pedido ao mesmo tempo, só um consegue iniciar.
    /// </summary>
    public bool TentarIniciar(PedidoId pedidoId) => _emAndamento.TryAdd(pedidoId, DateTime.UtcNow);

    public void Finalizar(PedidoId pedidoId) => _emAndamento.TryRemove(pedidoId, out _);

    public void RegistrarResultado(ResultadoDoProcessamento resultado, TimeSpan duracao)
    {
        _porResultado.AddOrUpdate(resultado, 1, (_, atual) => atual + 1); // atômico por chave
        Interlocked.Add(ref _duracaoTotalTicks, duracao.Ticks);
        Interlocked.Increment(ref _duracoesRegistradas);
    }

    public void RegistrarEsperaNaFila(TimeSpan espera)
    {
        Interlocked.Add(ref _esperaTotalTicks, espera.Ticks);
        Interlocked.Increment(ref _esperasRegistradas);
        long anterior;
        do
        {
            anterior = Volatile.Read(ref _esperaMaximaTicks);
            if (espera.Ticks <= anterior)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref _esperaMaximaTicks, espera.Ticks, anterior) != anterior);
    }

    public long Contar(ResultadoDoProcessamento resultado) => _porResultado.GetValueOrDefault(resultado);

    public InstantaneoDasMetricas Instantaneo(int profundidade, int picoDeAcessoAoBanco)
    {
        var esperas = Interlocked.Read(ref _esperasRegistradas);
        var duracoes = Interlocked.Read(ref _duracoesRegistradas);
        return new InstantaneoDasMetricas(
            profundidade,
            Interlocked.Read(ref _aceitos),
            Interlocked.Read(ref _rejeitados),
            Interlocked.Read(ref _descartados),
            Contar(ResultadoDoProcessamento.Processado),
            Contar(ResultadoDoProcessamento.IgnoradoPorIdempotencia),
            Contar(ResultadoDoProcessamento.NaoEncontrado),
            Interlocked.Read(ref _falhas),
            _emAndamento.Count,
            picoDeAcessoAoBanco,
            esperas == 0 ? 0 : TimeSpan.FromTicks(Interlocked.Read(ref _esperaTotalTicks) / esperas).TotalMilliseconds,
            TimeSpan.FromTicks(Interlocked.Read(ref _esperaMaximaTicks)).TotalMilliseconds,
            duracoes == 0 ? 0 : TimeSpan.FromTicks(Interlocked.Read(ref _duracaoTotalTicks) / duracoes).TotalMilliseconds);
    }
}
