using System.Reflection;
using HubPedidos.Application.Processamento;
using HubPedidos.Application.Processamento.Processadores;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.Valido;
using HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.Valido.Interno;
using HubPedidos.Tests.Aplicacao.Catalogo.Cenarios.ValidoExtra;

namespace HubPedidos.Tests.Aplicacao.Catalogo;

[TestClass]
public sealed class CatalogoDeProcessadoresTestes
{
    private const string Cenarios = "HubPedidos.Tests.Aplicacao.Catalogo.Cenarios";
    private static readonly Assembly AssemblyDosTestes = typeof(CatalogoDeProcessadoresTestes).Assembly;

    [TestMethod]
    public void Catalogo_de_producao_cobre_todas_as_categorias_e_ativa_a_maior_versao()
    {
        var catalogo = CatalogoDeProcessadores.DescobrirPadrao();

        Assert.AreEqual(OrigemDoCatalogo.Reflexao, catalogo.Origem);
        Assert.HasCount(6, catalogo.Entradas);
        Assert.HasCount(5, catalogo.Ativos);
        Assert.IsTrue(catalogo.TentarObter(CategoriaProcessamento.Expressa, out var expressa));
        Assert.AreEqual(typeof(ProcessadorExpressoV2), expressa.Tipo);
        Assert.IsFalse(catalogo.TentarObter(CategoriaProcessamento.Invalida, out _));
    }

    [TestMethod]
    public void Reflexao_de_producao_fica_restrita_ao_assembly_e_namespace_previstos()
    {
        var catalogo = CatalogoDeProcessadores.DescobrirPadrao();
        var diagnostico = catalogo.Diagnostico!;

        Assert.AreEqual("HubPedidos.Application", diagnostico.Assembly);
        Assert.IsTrue(diagnostico.TiposNoEscopo.Count < diagnostico.TiposNoAssembly);
        Assert.IsTrue(catalogo.Entradas.All(e =>
            e.Tipo.Assembly == typeof(IProcessadorDePedido).Assembly &&
            e.Tipo.Namespace == CatalogoDeProcessadores.NamespacePadrao));
        // Os processadores marcados deste assembly de testes nunca entram no catálogo de produção.
        Assert.IsFalse(catalogo.Entradas.Any(e => e.Tipo.Assembly == AssemblyDosTestes));
    }

    [TestMethod]
    public void Escopo_inclui_subnamespace_e_exclui_namespace_com_mesmo_prefixo()
    {
        var catalogo = CatalogoDeProcessadores.Descobrir(AssemblyDosTestes, $"{Cenarios}.Valido", exigirCoberturaCompleta: false);

        var tipos = catalogo.Entradas.Select(e => e.Tipo).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { typeof(ProcessadorTestePadrao), typeof(ProcessadorTestePadraoV3), typeof(ProcessadorTesteVolumoso) },
            tipos);
        CollectionAssert.DoesNotContain(tipos, typeof(ProcessadorForaDoEscopo));
        CollectionAssert.AreEqual(new[] { typeof(ProcessadorSemAtributo) }, catalogo.Diagnostico!.ImplementacoesSemAtributo.ToArray());
    }

    [TestMethod]
    public void Descoberta_e_deterministica_e_nao_duplica_processadores()
    {
        var primeira = CatalogoDeProcessadores.DescobrirPadrao();
        var segunda = CatalogoDeProcessadores.DescobrirPadrao();

        CollectionAssert.AreEqual(
            primeira.Entradas.Select(e => e.Tipo.FullName).ToArray(),
            segunda.Entradas.Select(e => e.Tipo.FullName).ToArray());
        Assert.AreEqual(primeira.Entradas.Count, primeira.Entradas.Select(e => e.Tipo).Distinct().Count());

        // Consultas repetidas devolvem a mesma instância: nenhuma nova reflexão nem nova criação.
        primeira.TentarObter(CategoriaProcessamento.Padrao, out var a);
        primeira.TentarObter(CategoriaProcessamento.Padrao, out var b);
        Assert.AreSame(a.Instancia, b.Instancia);
    }

    [TestMethod]
    public void Chave_duplicada_falha_com_mensagem_deterministica()
    {
        var excecao = Assert.ThrowsExactly<CatalogoInvalidoException>(
            () => CatalogoDeProcessadores.Descobrir(AssemblyDosTestes, $"{Cenarios}.Duplicado", exigirCoberturaCompleta: false));

        Assert.AreEqual(
            $"A categoria 'Padrao' versão 1 foi declarada por mais de um tipo: '{Cenarios}.Duplicado.PrimeiroDuplicado' e '{Cenarios}.Duplicado.SegundoDuplicado'.",
            excecao.Problemas.Single());
    }

    [TestMethod]
    [DataRow("Incompativel", "não implementa IProcessadorDePedido", DisplayName = "marcado sem implementar o contrato")]
    [DataRow("Abstrato", "não é uma classe concreta", DisplayName = "marcado e abstrato")]
    [DataRow("SemConstrutor", "construtor público sem parâmetros", DisplayName = "sem construtor utilizável")]
    [DataRow("CategoriaInvalida", "não pode ter processador", DisplayName = "categoria Invalida")]
    [DataRow("VersaoInvalida", "a versão deve ser maior ou igual a 1", DisplayName = "versão zero")]
    public void Metadados_inconsistentes_falham_de_forma_legivel(string cenario, string trechoEsperado)
    {
        var excecao = Assert.ThrowsExactly<CatalogoInvalidoException>(
            () => CatalogoDeProcessadores.Descobrir(AssemblyDosTestes, $"{Cenarios}.{cenario}", exigirCoberturaCompleta: false));

        StringAssert.Contains(excecao.Message, trechoEsperado);
        StringAssert.Contains(excecao.Message, $"{Cenarios}.{cenario}.");
    }

    [TestMethod]
    public void Configuracao_sem_cobertura_completa_e_rejeitada()
    {
        var excecao = Assert.ThrowsExactly<CatalogoInvalidoException>(
            () => CatalogoDeProcessadores.Descobrir(AssemblyDosTestes, $"{Cenarios}.Valido", exigirCoberturaCompleta: true));

        StringAssert.Contains(excecao.Message, "Nenhum processador registrado para: Prioritaria, Expressa, AltoValor.");
    }

    [TestMethod]
    public void Registro_explicito_produz_o_mesmo_catalogo_que_a_reflexao()
    {
        var porReflexao = CatalogoDeProcessadores.DescobrirPadrao();
        var explicito = RegistroExplicitoDeProcessadores.Criar();

        Assert.AreEqual(OrigemDoCatalogo.RegistroExplicito, explicito.Origem);
        Assert.IsNull(explicito.Diagnostico);
        CollectionAssert.AreEqual(
            porReflexao.Entradas.Select(e => (e.Categoria, e.Versao, e.Tipo)).ToArray(),
            explicito.Entradas.Select(e => (e.Categoria, e.Versao, e.Tipo)).ToArray());
    }

    [TestMethod]
    public void Atributo_restringe_destino_multiplicidade_e_heranca()
    {
        var uso = typeof(ProcessadorPedidoAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.IsNotNull(uso);
        Assert.AreEqual(AttributeTargets.Class, uso.ValidOn);
        Assert.IsFalse(uso.AllowMultiple);
        Assert.IsFalse(uso.Inherited);
    }
}
