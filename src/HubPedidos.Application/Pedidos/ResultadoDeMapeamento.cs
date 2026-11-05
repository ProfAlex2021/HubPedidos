using System.Diagnostics.CodeAnalysis;
using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Application.Pedidos;

/// <summary>
/// Resultado de uma conversão que pode falhar. Em vez de lançar exceção na primeira
/// falha, o mapeador devolve todos os erros encontrados de uma vez.
/// </summary>
public sealed class ResultadoDeMapeamento<T>
    where T : class
{
    private ResultadoDeMapeamento(T? valor, IReadOnlyList<ErroDeValidacao> erros)
    {
        Valor = valor;
        Erros = erros;
    }

    public T? Valor { get; }

    public IReadOnlyList<ErroDeValidacao> Erros { get; }

    [MemberNotNullWhen(true, nameof(Valor))]
    public bool Sucesso => Valor is not null;

    public static ResultadoDeMapeamento<T> Ok(T valor) => new(valor, []);

    public static ResultadoDeMapeamento<T> Falha(IReadOnlyList<ErroDeValidacao> erros)
    {
        if (erros.Count == 0)
        {
            throw new ArgumentException("Uma falha precisa de ao menos um erro.", nameof(erros));
        }

        return new(null, erros);
    }
}
