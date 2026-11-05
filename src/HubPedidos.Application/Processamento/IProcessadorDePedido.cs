using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento;

/// <summary>
/// Contrato estático comum a todas as estratégias de processamento.
/// A reflexão decide QUAIS classes participam do catálogo; depois disso, toda
/// chamada passa por esta interface e continua verificada pelo compilador.
/// </summary>
public interface IProcessadorDePedido
{
    ResultadoProcessamento Processar(Pedido pedido);
}

/// <summary>Decisão tomada por um processador para um pedido.</summary>
public sealed record ResultadoProcessamento(
    string Processador,
    int Versao,
    int PrazoEstimadoEmDias,
    string Observacao);
