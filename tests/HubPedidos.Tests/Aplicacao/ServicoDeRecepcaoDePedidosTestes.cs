using HubPedidos.Api.Endpoints;
using HubPedidos.Application.Pedidos;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Processamento;
using HubPedidos.Domain.Pedidos;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HubPedidos.Tests.Aplicacao;

/// <summary>Fluxo completo do Roteiro 1: DTO -> domínio -> classificação -> processador -> resposta.</summary>
[TestClass]
public sealed class ServicoDeRecepcaoDePedidosTestes
{
    private static readonly PedidoId IdFixo = new(Guid.Parse("0199f3c2-7c4e-7a10-9d2b-3f1a2b3c4d5e"));
    private readonly ServicoDeRecepcaoDePedidos _servico = new(CatalogoDeProcessadores.DescobrirPadrao(), () => IdFixo);

    [TestMethod]
    [DataRow("Norte", "Normal", 2, "10.00", "Padrao", "ProcessadorPadrao v1")]
    [DataRow("Norte", "Alta", 2, "10.00", "Prioritaria", "ProcessadorPrioritario v1")]
    [DataRow("Sudeste", "Urgente", 2, "10.00", "Expressa", "ProcessadorExpressoV2 v2")]
    [DataRow("Norte", "Normal", 60, "10.00", "Volumosa", "ProcessadorVolumoso v1")]
    [DataRow("Norte", "Normal", 2, "6000.00", "AltoValor", "ProcessadorAltoValor v1")]
    public void Pedido_valido_e_classificado_e_encaminhado_ao_processador_ativo(
        string regiao, string prioridade, int quantidade, string preco, string categoria, string processador)
    {
        var requisicao = new CriarPedidoRequest(
            "cliente-1",
            regiao,
            prioridade,
            [new ItemPedidoRequest("SKU", quantidade, decimal.Parse(preco, System.Globalization.CultureInfo.InvariantCulture))]);

        var resultado = _servico.Receber(requisicao);

        var aceito = Assert.IsInstanceOfType<ResultadoDaRecepcao.Aceito>(resultado);
        Assert.AreEqual(IdFixo.ToString(), aceito.Pedido.Id);
        Assert.AreEqual(categoria, aceito.Pedido.Categoria);
        Assert.AreEqual(processador, aceito.Pedido.Processador);
    }

    [TestMethod]
    public void Endpoint_traduz_resultados_para_respostas_tipadas()
    {
        var valido = PedidosEndpoints.ReceberPedido(
            new CriarPedidoRequest("c", "Sul", "Normal", [new ItemPedidoRequest("A", 1, 10m)]), _servico);
        var invalido = PedidosEndpoints.ReceberPedido(
            new CriarPedidoRequest("", "Sul", "Normal", [new ItemPedidoRequest("A", 0, 10m)]), _servico);

        var ok = Assert.IsInstanceOfType<Ok<PedidoResponse>>(valido.Result);
        Assert.AreEqual("Padrao", ok.Value!.Categoria);

        var problema = Assert.IsInstanceOfType<ValidationProblem>(invalido.Result);
        CollectionAssert.AreEquivalent(
            new[] { "clienteId", "itens[0].quantidade" },
            problema.ProblemDetails.Errors.Keys.ToArray());
    }
}
