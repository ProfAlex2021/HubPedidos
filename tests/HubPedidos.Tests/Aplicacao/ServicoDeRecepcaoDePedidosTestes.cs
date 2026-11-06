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
    public async Task Endpoint_traduz_resultados_para_respostas_tipadas()
    {
        // Roteiro 3: o endpoint passou a persistir o pedido aceito (201 Created).
        // Roteiro 6: via ServicoDePedidos, que também notifica depois de persistir.
        using var banco = new Persistencia.BancoDeTeste();
        using var contexto = banco.CriarContexto();
        var repositorio = new HubPedidos.Infrastructure.Persistencia.RepositorioDePedidos(contexto);
        var notificador = new NotificadorDeTeste();
        var servicoDePedidos = new ServicoDePedidos(_servico, repositorio, notificador, TimeProvider.System);

        var valido = await PedidosEndpoints.ReceberPedidoAsync(
            new CriarPedidoRequest("c", "Sul", "Normal", [new ItemPedidoRequest("A", 1, 10m)]), servicoDePedidos, CancellationToken.None);
        var invalido = await PedidosEndpoints.ReceberPedidoAsync(
            new CriarPedidoRequest("", "Sul", "Normal", [new ItemPedidoRequest("A", 0, 10m)]), servicoDePedidos, CancellationToken.None);

        var criado = Assert.IsInstanceOfType<Created<PedidoResponse>>(valido.Result);
        Assert.AreEqual("Padrao", criado.Value!.Categoria);
        Assert.AreEqual($"/api/pedidos/{IdFixo}", criado.Location);
        Assert.AreEqual(1, contexto.Pedidos.Count());
        // Uma notificação, só para o pedido aceito (e só depois de gravado).
        Assert.AreEqual(IdFixo, notificador.Publicadas.Single().PedidoId);

        var problema = Assert.IsInstanceOfType<ValidationProblem>(invalido.Result);
        CollectionAssert.AreEquivalent(
            new[] { "clienteId", "itens[0].quantidade" },
            problema.ProblemDetails.Errors.Keys.ToArray());
    }
}
