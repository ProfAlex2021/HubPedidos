namespace HubPedidos.Integracao.Testes.Demonstracoes;

[TestClass]
public sealed class DemonstracoesTestes
{
    [TestMethod]
    [DataRow(null, 0)]
    [DataRow("", 0)]
    [DataRow("pedido", 6)]
    public void Exemplo_corrigido_trata_null_explicitamente(string? texto, int esperado) =>
        Assert.AreEqual(esperado, ExemploCorrigido.ObterTamanho(texto));

#if DEMONSTRAR_FALHA_DE_TESTE
    /// <summary>
    /// Falha CONTROLADA de teste: só existe com -p:DemonstrarFalhaDeTeste=true
    /// (scripts/revisao.ps1 -DemonstrarFalha teste) e prova que a revisão bloqueia.
    /// </summary>
    [TestMethod]
    public void Falha_demonstrativa_da_revisao_automatizada() =>
        Assert.Fail("Falha provocada de propósito para demonstrar o bloqueio da revisão automatizada.");
#endif
}
