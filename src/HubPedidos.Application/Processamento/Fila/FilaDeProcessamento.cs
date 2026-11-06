using System.Threading.Channels;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento.Fila;

/// <summary>
/// Unidade de trabalho da fila: somente o identificador e o instante de admissão.
/// Nada de DbContext, entidade ou DTO grande dentro do item: o consumidor busca o
/// que precisa no próprio escopo, e a fila retém poucos bytes por item.
/// </summary>
public sealed record ProcessarPedidoJob(PedidoId PedidoId, DateTime EnfileiradoEmUtc);

/// <summary>Resultado de uma tentativa imediata de admissão.</summary>
public enum ResultadoDaAdmissao
{
    Aceito,

    /// <summary>Fila cheia no modo Wait: rejeição explícita (vira 429 na API).</summary>
    Rejeitado,

    /// <summary>A aplicação está encerrando e não aceita novos itens (vira 503).</summary>
    Encerrada,
}

/// <summary>Configuração da fila, dos consumidores e do acesso ao banco.</summary>
public sealed class OpcoesDaFila
{
    /// <summary>Itens aguardando leitura. Limita a memória retida pela fila.</summary>
    public int Capacidade { get; init; } = 100;

    /// <summary>
    /// Política quando a fila está cheia. Padrão: <see cref="BoundedChannelFullMode.Wait"/>,
    /// porque pedidos não podem ser descartados silenciosamente; a fronteira HTTP
    /// rejeita com <c>TryWrite</c> e o produtor interno aguarda com <c>WriteAsync</c>.
    /// </summary>
    public BoundedChannelFullMode ModoQuandoCheia { get; init; } = BoundedChannelFullMode.Wait;

    /// <summary>Tarefas consumidoras permanentes.</summary>
    public int Consumidores { get; init; } = 4;

    /// <summary>Operações simultâneas permitidas no banco (SemaphoreSlim).</summary>
    public int LimiteDeAcessoAoBanco { get; init; } = 1;

    /// <summary>Trabalho externo simulado por pedido (separação/estoque), fora do banco.</summary>
    public int DuracaoSimuladaEmMs { get; init; } = 20;

    public void Validar()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacidade, 1, nameof(Capacidade));
        ArgumentOutOfRangeException.ThrowIfLessThan(Consumidores, 1, nameof(Consumidores));
        ArgumentOutOfRangeException.ThrowIfLessThan(LimiteDeAcessoAoBanco, 1, nameof(LimiteDeAcessoAoBanco));
        ArgumentOutOfRangeException.ThrowIfNegative(DuracaoSimuladaEmMs, nameof(DuracaoSimuladaEmMs));
    }
}

/// <summary>
/// Fila limitada (<see cref="Channel{T}"/> bounded), registrada como serviço único.
/// </summary>
/// <remarks>
/// - Capacidade e modo de "cheia" são observáveis (<see cref="Profundidade"/>, métricas).
/// - Nos modos de descarte, o callback <c>itemDropped</c> registra cada item descartado,
///   para que descarte (decisão da política) e rejeição (TryWrite falhou) apareçam
///   separados nas métricas.
/// - <see cref="Concluir"/> impede novas admissões e permite que os consumidores
///   esvaziem o que já foi aceito.
/// </remarks>
public sealed class FilaDeProcessamento
{
    private readonly Channel<ProcessarPedidoJob> _canal;
    private readonly MetricasDoProcessamento _metricas;

    public FilaDeProcessamento(OpcoesDaFila opcoes, MetricasDoProcessamento metricas)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        opcoes.Validar();
        Opcoes = opcoes;
        _metricas = metricas;
        _canal = Channel.CreateBounded<ProcessarPedidoJob>(
            new BoundedChannelOptions(opcoes.Capacidade)
            {
                FullMode = opcoes.ModoQuandoCheia,
                SingleReader = false, // vários consumidores
                SingleWriter = false, // várias requisições produzem ao mesmo tempo
            },
            itemDropped: descartado => _metricas.RegistrarDescarte(descartado.PedidoId));
    }

    public OpcoesDaFila Opcoes { get; }

    public ChannelReader<ProcessarPedidoJob> Leitor => _canal.Reader;

    /// <summary>Itens aguardando leitura agora.</summary>
    public int Profundidade => _canal.Reader.Count;

    public bool Encerrada { get; private set; }

    /// <summary>
    /// Admissão imediata para a fronteira interativa: nunca espera. No modo Wait,
    /// fila cheia = <see cref="ResultadoDaAdmissao.Rejeitado"/>.
    /// </summary>
    public ResultadoDaAdmissao TentarEnfileirar(PedidoId pedidoId)
    {
        if (Encerrada)
        {
            return ResultadoDaAdmissao.Encerrada;
        }

        if (_canal.Writer.TryWrite(new ProcessarPedidoJob(pedidoId, DateTime.UtcNow)))
        {
            _metricas.RegistrarAceito();
            return ResultadoDaAdmissao.Aceito;
        }

        // TryWrite também falha depois de Concluir(); distingue os dois casos.
        if (Encerrada)
        {
            return ResultadoDaAdmissao.Encerrada;
        }

        _metricas.RegistrarRejeitado();
        return ResultadoDaAdmissao.Rejeitado;
    }

    /// <summary>
    /// Admissão para produtores internos que podem esperar (backpressure): no modo
    /// Wait, aguarda espaço. Respeita cancelamento; um item cancelado não conta como aceito.
    /// </summary>
    public async ValueTask EnfileirarAsync(PedidoId pedidoId, CancellationToken cancellationToken)
    {
        await _canal.Writer.WriteAsync(new ProcessarPedidoJob(pedidoId, DateTime.UtcNow), cancellationToken);
        _metricas.RegistrarAceito();
    }

    /// <summary>Encerramento: nenhum item novo; os consumidores drenam os restantes.</summary>
    public void Concluir()
    {
        Encerrada = true;
        _canal.Writer.TryComplete();
    }
}
