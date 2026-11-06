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

    // ---- Roteiro 6 (extensão): teste arquitetural automatizado entre assemblies ----

    [TestMethod]
    public void Infraestrutura_nao_depende_da_API()
    {
        var infraestrutura = typeof(HubPedidos.Infrastructure.Persistencia.ContextoDePedidos).Assembly;

        Assert.IsFalse(infraestrutura.GetReferencedAssemblies().Any(a => a.Name == "HubPedidos.Api"));
    }

    [TestMethod]
    public void Endpoints_dependem_so_de_portas_e_servicos_da_aplicacao()
    {
        // Nenhum parâmetro ou retorno dos endpoints usa tipos da Infrastructure ou do EF Core:
        // a API conhece a infraestrutura só no composition root.
        var api = typeof(HubPedidos.Api.Endpoints.PedidosEndpoints).Assembly;
        var proibidos = new[] { "HubPedidos.Infrastructure", "Microsoft.EntityFrameworkCore" };

        var violacoes = api.GetTypes()
            .Where(t => t.Namespace == "HubPedidos.Api.Endpoints")
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)
                .SelectMany(TiposEnvolvidos)
                .Where(tipo => proibidos.Any(p => tipo.Assembly.GetName().Name!.StartsWith(p, StringComparison.Ordinal)))
                .Select(tipo => $"{m.DeclaringType!.Name}.{m.Name} usa {tipo.FullName}"))
            .Distinct()
            .ToArray();

        Assert.IsEmpty(violacoes, string.Join(Environment.NewLine, violacoes));
    }

    [TestMethod]
    public void Portas_da_aplicacao_sao_implementadas_somente_na_infraestrutura()
    {
        var portas = Aplicacao.GetTypes().Where(t => t.IsInterface && t.Namespace!.EndsWith(".Portas", StringComparison.Ordinal)).ToArray();
        var infraestrutura = typeof(HubPedidos.Infrastructure.Persistencia.ContextoDePedidos).Assembly;

        Assert.IsNotEmpty(portas);
        foreach (var porta in portas)
        {
            Assert.IsTrue(infraestrutura.GetTypes().Any(t => !t.IsAbstract && porta.IsAssignableFrom(t)), $"{porta.Name} sem implementação na infraestrutura.");
            Assert.IsFalse(Aplicacao.GetTypes().Any(t => !t.IsInterface && porta.IsAssignableFrom(t)), $"{porta.Name} implementada dentro da aplicação.");
        }
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
