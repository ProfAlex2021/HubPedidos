using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>
/// Usada somente por "dotnet ef migrations add". Gerar uma migration não abre conexão;
/// o arquivo indicado aqui nunca é criado. Assim as ferramentas não dependem da API
/// nem de segredos de desenvolvimento.
/// </summary>
public sealed class FabricaDeContextoEmTempoDeDesign : IDesignTimeDbContextFactory<ContextoDePedidos>
{
    public ContextoDePedidos CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ContextoDePedidos>()
            .UseSqlite("Data Source=somente-para-design.db")
            .Options);
}
