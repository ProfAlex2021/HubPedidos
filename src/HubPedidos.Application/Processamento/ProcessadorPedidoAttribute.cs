using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento;

/// <summary>
/// Metadado declarativo que associa uma categoria e uma versão a uma implementação
/// de <see cref="IProcessadorDePedido"/>.
/// </summary>
/// <remarks>
/// O atributo sozinho não executa nada: quem o interpreta é o
/// <see cref="CatalogoDeProcessadores"/>. <c>AttributeUsage</c> restringe a aplicação
/// a classes, uma única vez por classe e sem herança, para que cada implementação
/// declare diretamente sua participação.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProcessadorPedidoAttribute : Attribute
{
    public ProcessadorPedidoAttribute(CategoriaProcessamento categoria, int versao)
    {
        Categoria = categoria;
        Versao = versao;
    }

    public CategoriaProcessamento Categoria { get; }

    public int Versao { get; }
}
