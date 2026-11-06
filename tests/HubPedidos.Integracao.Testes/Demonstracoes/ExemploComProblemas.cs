namespace HubPedidos.Integracao.Testes.Demonstracoes;

/// <summary>
/// Falha CONTROLADA de analisador (Roteiro 8, Etapa 4). Fica fora do build normal e só é
/// compilado com -p:DemonstrarFalhaDeAnalisador=true (scripts/revisao.ps1 -DemonstrarFalha analisador).
/// Esperado: CS8602 vira ERRO (política em Directory.Build.props) e CA1822 aparece como aviso.
/// </summary>
public sealed class ExemploComProblemas
{
    // Pode desreferenciar null (CS8602) e não usa estado da instância (CA1822).
    public int ObterTamanho(string? texto)
    {
        return texto.Length;
    }
}
