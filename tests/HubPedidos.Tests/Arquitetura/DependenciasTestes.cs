using System.Reflection;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests.Arquitetura;

/// <summary>
/// Extensão do Roteiro 1: teste arquitetural, sem pacotes externos, que impede que os
/// DTOs dependam de domínio ou de infraestrutura e que as camadas internas
/// referenciem a API.
/// </summary>
[TestClass]
public sealed class DependenciasTestes
{
    private static readonly Assembly Dominio = typeof(Pedido).Assembly;
    private static readonly Assembly Aplicacao = typeof(CriarPedidoRequest).Assembly;

    [TestMethod]
    public void DTOs_expoem_somente_tipos_da_BCL_ou_outros_contratos()
    {
        var contratos = Aplicacao.GetTypes()
            .Where(t => t.Namespace == typeof(CriarPedidoRequest).Namespace && t.IsPublic)
            .ToArray();

        var violacoes = contratos
            .SelectMany(contrato => contrato.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(p => TiposEnvolvidos(p.PropertyType))
                .Where(tipo => !EhPermitidoEmContrato(tipo))
                .Select(tipo => $"{contrato.Name} expõe {tipo.FullName}"))
            .Distinct()
            .ToArray();

        Assert.IsNotEmpty(contratos);
        Assert.IsEmpty(violacoes, string.Join(Environment.NewLine, violacoes));
    }

    [TestMethod]
    public void Dominio_nao_referencia_nenhum_outro_projeto_nem_framework_web()
    {
        var referencias = Dominio.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.IsTrue(
            referencias.All(nome => nome.StartsWith("System", StringComparison.Ordinal) || nome == "netstandard"),
            string.Join(", ", referencias));
    }

    [TestMethod]
    public void Aplicacao_nao_referencia_API_ASPNET_nem_infraestrutura()
    {
        var referencias = Aplicacao.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        string[] proibidos = ["HubPedidos.Api", "HubPedidos.Infrastructure", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore"];
        var violacoes = referencias.Where(r => proibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal))).ToArray();

        Assert.IsEmpty(violacoes, string.Join(", ", violacoes));
    }

    private static IEnumerable<Type> TiposEnvolvidos(Type tipo)
    {
        yield return tipo;
        foreach (var argumento in tipo.GetGenericArguments().SelectMany(TiposEnvolvidos))
        {
            yield return argumento;
        }
    }

    private static bool EhPermitidoEmContrato(Type tipo) =>
        tipo.Namespace is "System" or "System.Collections.Generic" ||
        tipo.Namespace == typeof(CriarPedidoRequest).Namespace;
}
