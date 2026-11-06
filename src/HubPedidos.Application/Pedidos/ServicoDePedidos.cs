using HubPedidos.Application.Notificacoes;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Pedidos;

/// <summary>
/// Coordena os casos de uso de escrita do módulo de pedidos. Recebe as dependências
/// por construtor (nunca resolve serviços do contêiner) e não conhece EF Core nem HTTP.
/// </summary>
/// <remarks>
/// Tempo de vida scoped: depende do repositório, que é scoped (um DbContext por requisição).
/// </remarks>
public sealed class ServicoDePedidos(
    ServicoDeRecepcaoDePedidos recepcao,
    IRepositorioDePedidos repositorio,
    INotificadorDePedidos notificador,
    TimeProvider relogio)
{
    /// <summary>Valida, classifica, persiste e só então notifica.</summary>
    public async Task<ResultadoDaRecepcao> CriarAsync(CriarPedidoRequest? requisicao, CancellationToken cancellationToken)
    {
        var resultado = recepcao.Receber(requisicao);
        if (resultado is ResultadoDaRecepcao.Aceito aceito)
        {
            await repositorio.AdicionarAsync(aceito.Entidade, aceito.Classificacao, aceito.Processamento, cancellationToken);
            await notificador.PublicarAsync(
                new MudancaDeSituacao(aceito.Entidade.Id, aceito.Entidade.Situacao, relogio.GetUtcNow().UtcDateTime), cancellationToken);
        }

        return resultado;
    }

    public async Task<ResultadoDoCancelamento> CancelarAsync(PedidoId id, CancellationToken cancellationToken)
    {
        var resultado = await repositorio.CancelarAsync(id, cancellationToken);
        if (resultado == ResultadoDoCancelamento.Cancelado)
        {
            await notificador.PublicarAsync(new MudancaDeSituacao(id, SituacaoPedido.Cancelado, relogio.GetUtcNow().UtcDateTime), cancellationToken);
        }

        return resultado;
    }
}
