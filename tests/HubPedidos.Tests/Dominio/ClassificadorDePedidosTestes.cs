using System.Globalization;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests.Dominio;

/// <summary>
/// Testes parametrizados: uma linha por classe de equivalência e por limite.
/// A tabela de casos cobertos do guia do roteiro foi gerada a partir destas linhas.
/// </summary>
[TestClass]
public sealed class ClassificadorDePedidosTestes
{
    [TestMethod]
    // Normal (padrão)
    [DataRow("100.00", 2, Regiao.Nordeste, Prioridade.Normal, CategoriaProcessamento.Padrao, DisplayName = "Padrão: pedido comum")]
    [DataRow("10000.00", 1, Regiao.Sul, Prioridade.Normal, CategoriaProcessamento.Padrao, DisplayName = "Limite: R$ 10.000,00 ainda é Padrão")]
    [DataRow("100.00", 50, Regiao.Sul, Prioridade.Normal, CategoriaProcessamento.Padrao, DisplayName = "Limite: 50 unidades ainda é Padrão")]
    // Prioritário
    [DataRow("100.00", 2, Regiao.Norte, Prioridade.Alta, CategoriaProcessamento.Prioritaria, DisplayName = "Prioritário: Alta")]
    [DataRow("100.00", 2, Regiao.Nordeste, Prioridade.Urgente, CategoriaProcessamento.Prioritaria, DisplayName = "Prioritário: urgente fora de Sul/Sudeste")]
    [DataRow("5000.01", 2, Regiao.Sudeste, Prioridade.Urgente, CategoriaProcessamento.Prioritaria, DisplayName = "Limite: urgente acima de R$ 5.000 não é expressa")]
    [DataRow("100.00", 21, Regiao.Sudeste, Prioridade.Urgente, CategoriaProcessamento.Prioritaria, DisplayName = "Limite: urgente com 21 unidades não é expressa")]
    // Expressa
    [DataRow("100.00", 2, Regiao.Sul, Prioridade.Urgente, CategoriaProcessamento.Expressa, DisplayName = "Expressa: urgente no Sul")]
    [DataRow("5000.00", 20, Regiao.Sudeste, Prioridade.Urgente, CategoriaProcessamento.Expressa, DisplayName = "Limite: R$ 5.000 e 20 unidades ainda é expressa")]
    // Alto valor (precedência sobre todas as outras)
    [DataRow("10000.01", 1, Regiao.Sul, Prioridade.Normal, CategoriaProcessamento.AltoValor, DisplayName = "Limite: R$ 10.000,01 é AltoValor")]
    [DataRow("20000.00", 2, Regiao.Sudeste, Prioridade.Urgente, CategoriaProcessamento.AltoValor, DisplayName = "Precedência: alto valor vence urgência")]
    [DataRow("20000.00", 80, Regiao.Norte, Prioridade.Alta, CategoriaProcessamento.AltoValor, DisplayName = "Precedência: alto valor vence volume")]
    // Volumosa
    [DataRow("100.00", 51, Regiao.Norte, Prioridade.Normal, CategoriaProcessamento.Volumosa, DisplayName = "Limite: 51 unidades é Volumosa")]
    [DataRow("100.00", 60, Regiao.Norte, Prioridade.Alta, CategoriaProcessamento.Volumosa, DisplayName = "Precedência: volume vence prioridade alta")]
    // Inválido
    [DataRow("0", 1, Regiao.Sul, Prioridade.Normal, CategoriaProcessamento.Invalida, DisplayName = "Inválido: valor zero")]
    [DataRow("-1", 1, Regiao.Sul, Prioridade.Normal, CategoriaProcessamento.Invalida, DisplayName = "Inválido: valor negativo")]
    [DataRow("10", 0, Regiao.Sul, Prioridade.Normal, CategoriaProcessamento.Invalida, DisplayName = "Inválido: nenhuma unidade")]
    [DataRow("10", 1, (Regiao)99, Prioridade.Normal, CategoriaProcessamento.Invalida, DisplayName = "Desconhecido: região fora da enumeração")]
    [DataRow("10", 1, (Regiao)0, Prioridade.Normal, CategoriaProcessamento.Invalida, DisplayName = "Desconhecido: região default(0)")]
    [DataRow("10", 1, Regiao.Sul, (Prioridade)42, CategoriaProcessamento.Invalida, DisplayName = "Desconhecido: prioridade fora da enumeração")]
    public void Classifica_cada_classe_de_equivalencia(
        string valorTotal,
        int quantidadeDeItens,
        Regiao regiao,
        Prioridade prioridade,
        CategoriaProcessamento esperada)
    {
        var perfil = new PerfilDeClassificacao(
            decimal.Parse(valorTotal, CultureInfo.InvariantCulture),
            quantidadeDeItens,
            regiao,
            prioridade);

        var resultado = ClassificadorDePedidos.Classificar(perfil);

        Assert.AreEqual(esperada, resultado.Categoria, resultado.Motivo);
        Assert.IsFalse(string.IsNullOrWhiteSpace(resultado.Motivo));
    }

    [TestMethod]
    public void Entrada_desconhecida_recebe_resultado_explicito_e_nao_cai_em_padrao()
    {
        var regiaoDesconhecida = ClassificadorDePedidos.Classificar(
            new PerfilDeClassificacao(100m, 1, (Regiao)77, Prioridade.Normal));
        var prioridadeDesconhecida = ClassificadorDePedidos.Classificar(
            new PerfilDeClassificacao(100m, 1, Regiao.Sul, (Prioridade)77));

        Assert.AreEqual(CategoriaProcessamento.Invalida, regiaoDesconhecida.Categoria);
        Assert.AreEqual("Região desconhecida.", regiaoDesconhecida.Motivo);
        Assert.AreEqual(CategoriaProcessamento.Invalida, prioridadeDesconhecida.Categoria);
        Assert.AreEqual("Prioridade desconhecida.", prioridadeDesconhecida.Motivo);
    }

    [TestMethod]
    public void Todo_braco_do_switch_e_alcancavel_por_algum_caso_de_negocio()
    {
        // Complementa a verificação do compilador (CS8509/CS8510): cada categoria
        // é produzida por pelo menos um perfil, então nenhum braço é "morto".
        PerfilDeClassificacao[] perfis =
        [
            new(100m, 2, Regiao.Norte, Prioridade.Normal),
            new(100m, 2, Regiao.Norte, Prioridade.Alta),
            new(100m, 2, Regiao.Sul, Prioridade.Urgente),
            new(100m, 51, Regiao.Norte, Prioridade.Normal),
            new(10_000.01m, 1, Regiao.Norte, Prioridade.Normal),
            new(0m, 1, Regiao.Norte, Prioridade.Normal),
        ];

        var produzidas = perfis.Select(p => ClassificadorDePedidos.Classificar(p).Categoria).ToHashSet();

        CollectionAssert.AreEquivalent(Enum.GetValues<CategoriaProcessamento>(), produzidas.ToArray());
    }

    [TestMethod]
    public void Classificar_pedido_usa_os_totais_calculados_pela_entidade()
    {
        var pedido = new Pedido(
            PedidoId.Novo(),
            "cliente-1",
            Regiao.Sudeste,
            Prioridade.Urgente,
            [new ItemPedido("A", 3, 100m), new ItemPedido("B", 1, 50m)]);

        var resultado = ClassificadorDePedidos.Classificar(pedido);

        Assert.AreEqual(CategoriaProcessamento.Expressa, resultado.Categoria);
    }

    [TestMethod]
    public void Perfil_copiado_com_with_nao_altera_o_original()
    {
        var original = new PerfilDeClassificacao(100m, 2, Regiao.Sul, Prioridade.Normal);

        var copia = original with { Prioridade = Prioridade.Urgente };

        Assert.AreEqual(Prioridade.Normal, original.Prioridade);
        Assert.AreEqual(Prioridade.Urgente, copia.Prioridade);
        Assert.AreNotEqual(original, copia);
        Assert.AreEqual(CategoriaProcessamento.Padrao, ClassificadorDePedidos.Classificar(original).Categoria);
        Assert.AreEqual(CategoriaProcessamento.Expressa, ClassificadorDePedidos.Classificar(copia).Categoria);
    }
}
