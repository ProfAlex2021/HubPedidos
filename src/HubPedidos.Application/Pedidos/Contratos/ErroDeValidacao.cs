namespace HubPedidos.Application.Pedidos.Contratos;

/// <summary>Um problema encontrado em um campo do DTO de entrada.</summary>
/// <param name="Campo">Caminho do campo no JSON, por exemplo <c>itens[1].quantidade</c>.</param>
/// <param name="Mensagem">Texto legível para o consumidor.</param>
public sealed record ErroDeValidacao(string Campo, string Mensagem);
