namespace HubPedidos.Integracao.Testes.Infraestrutura;

/// <summary>Localiza a raiz do repositório subindo a partir da pasta de saída dos testes.</summary>
public static class Repositorio
{
    public static string Raiz { get; } = Localizar();

    private static string Localizar()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, "HubPedidos.slnx")))
        {
            pasta = pasta.Parent;
        }

        return pasta?.FullName ?? throw new InvalidOperationException("Raiz do repositório (HubPedidos.slnx) não encontrada.");
    }
}
