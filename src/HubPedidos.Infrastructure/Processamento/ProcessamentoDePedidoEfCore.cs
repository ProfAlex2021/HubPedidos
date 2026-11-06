using HubPedidos.Application.Processamento.Fila;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Infrastructure.Processamento;

/// <summary>
/// Processa um pedido retirado da fila. Instância scoped (uma por item): recebe o
/// DbContext do escopo criado pelo worker.
/// </summary>
/// <remarks>
/// 1. Trabalho externo simulado (separação/estoque) FORA do limitador: não segura uma
///    permissão do banco enquanto faz algo que não usa o banco.
/// 2. Banco DENTRO do limitador, com um único comando condicional:
///    <c>UPDATE ... SET Situacao='Processado', ProcessadoEmUtc=@agora WHERE Id=@id AND Situacao='Recebido'</c>.
///    É a idempotência persistida (extensão): reprocessar o mesmo pedido (reenvio,
///    item duplicado, reinício da aplicação) não altera nada, e um pedido cancelado
///    nunca é processado. A regra vale entre processos, não só na memória.
/// </remarks>
public sealed class ProcessamentoDePedidoEfCore(
    ContextoDePedidos contexto,
    LimitadorDeAcessoAoBanco limitador,
    OpcoesDaFila opcoes,
    TimeProvider relogio) : IProcessamentoDePedido
{
    public async Task<ResultadoDoProcessamento> ProcessarAsync(PedidoId pedidoId, CancellationToken cancellationToken)
    {
        if (opcoes.DuracaoSimuladaEmMs > 0)
        {
            await Task.Delay(opcoes.DuracaoSimuladaEmMs, cancellationToken);
        }

        return await limitador.ExecutarAsync(async token =>
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            var afetados = await contexto.Pedidos
                .Where(p => p.Id == pedidoId.Valor && p.Situacao == SituacaoPedido.Recebido)
                .ExecuteUpdateAsync(
                    atualizacao => atualizacao
                        .SetProperty(p => p.Situacao, SituacaoPedido.Processado)
                        .SetProperty(p => p.ProcessadoEmUtc, agora),
                    token);

            if (afetados == 1)
            {
                return ResultadoDoProcessamento.Processado;
            }

            return await contexto.Pedidos.AnyAsync(p => p.Id == pedidoId.Valor, token)
                ? ResultadoDoProcessamento.IgnoradoPorIdempotencia
                : ResultadoDoProcessamento.NaoEncontrado;
        }, cancellationToken);
    }
}
