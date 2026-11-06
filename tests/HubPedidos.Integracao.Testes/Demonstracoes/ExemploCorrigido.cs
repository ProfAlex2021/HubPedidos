namespace HubPedidos.Integracao.Testes.Demonstracoes;

/// <summary>
/// Versão corrigida do exemplo da Unidade IV: cada causa é resolvida, sem NoWarn,
/// #pragma ou SuppressMessage. Null tem resultado explícito (CS8602) e o método sem
/// estado é estático (CA1822).
/// </summary>
public static class ExemploCorrigido
{
    public static int ObterTamanho(string? texto) => texto?.Length ?? 0;
}
