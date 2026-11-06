namespace HubPedidos.Application.Processamento.Fila;

/// <summary>
/// Limita quantas operações entram ao mesmo tempo no banco, independentemente de
/// quantos consumidores existem. Só o trecho que usa o banco passa por aqui.
/// </summary>
/// <remarks>
/// O SQLite aceita um único escritor por vez: mais escritas simultâneas não aumentam a
/// vazão, só criam espera por bloqueio dentro do banco. Por isso o padrão é 1. Em um
/// SQL Server o limite seria escolhido em relação ao pool de conexões (padrão de 100
/// por connection string) e à carga das outras rotas que usam o mesmo pool.
/// </remarks>
public sealed class LimitadorDeAcessoAoBanco : IDisposable
{
    private readonly SemaphoreSlim _permissoes;
    private int _emUso;
    private int _pico;

    public LimitadorDeAcessoAoBanco(int limite)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        Limite = limite;
        _permissoes = new SemaphoreSlim(limite, limite);
    }

    public int Limite { get; }

    public int EmUso => Volatile.Read(ref _emUso);

    /// <summary>Maior concorrência observada desde a criação.</summary>
    public int Pico => Volatile.Read(ref _pico);

    /// <summary>Permissões livres agora (para provar que nenhuma ficou perdida).</summary>
    public int Disponiveis => _permissoes.CurrentCount;

    public async Task<T> ExecutarAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        // A espera fica FORA do try: se for cancelada, nenhuma permissão foi adquirida
        // e o finally não pode devolver o que não foi pego.
        await _permissoes.WaitAsync(cancellationToken);
        try
        {
            var atual = Interlocked.Increment(ref _emUso);
            AtualizarPico(atual);
            return await operacao(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _emUso);
            _permissoes.Release(); // falha ou cancelamento também devolvem a permissão
        }
    }

    public void Dispose() => _permissoes.Dispose();

    private void AtualizarPico(int atual)
    {
        // Laço de compare-and-swap: "ler, comparar e gravar" precisa ser atômico.
        int anterior;
        do
        {
            anterior = Volatile.Read(ref _pico);
            if (atual <= anterior)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _pico, atual, anterior) != anterior);
    }
}
