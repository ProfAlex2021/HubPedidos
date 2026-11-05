using HubPedidos.Infrastructure.Persistencia.Modelos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>Contexto principal: endereço mapeado em colunas da tabela Pedidos.</summary>
public class ContextoDePedidos(DbContextOptions options) : DbContext(options)
{
    public DbSet<PedidoRegistro> Pedidos => Set<PedidoRegistro>();

    public DbSet<ItemPedidoRegistro> Itens => Set<ItemPedidoRegistro>();

    /// <summary>Estratégia de mapeamento do endereço; o contexto JSON sobrescreve.</summary>
    protected virtual bool EnderecoComoJson => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PedidoRegistro>(ConfigurarPedido);
        modelBuilder.Entity<ItemPedidoRegistro>(ConfigurarItem);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configuracao)
    {
        // Enums gravados como texto: legíveis no banco e estáveis se a ordem do enum mudar.
        configuracao.Properties<Enum>().HaveConversion<string>().HaveMaxLength(20);

        // SQLite devolve DateTime sem "Kind"; todos os instantes da aplicação são UTC.
        configuracao.Properties<DateTime>().HaveConversion<ConversorDeDataUtc>();
    }

    private void ConfigurarPedido(EntityTypeBuilder<PedidoRegistro> pedido)
    {
        pedido.ToTable("Pedidos");
        pedido.HasKey(p => p.Numero);
        pedido.Property(p => p.Numero).ValueGeneratedOnAdd();

        pedido.Property(p => p.Id).IsRequired();
        pedido.HasIndex(p => p.Id).IsUnique();

        pedido.Property(p => p.ClienteId).IsRequired().HasMaxLength(50);
        pedido.Property(p => p.MotivoDaCategoria).IsRequired().HasMaxLength(200);
        pedido.Property(p => p.Processador).IsRequired().HasMaxLength(60);

        // Índices escolhidos pelas consultas reais do painel (ver roteiro-03.md):
        // ordenação/paginação por data com desempate pelo número, e filtros por cliente e categoria.
        pedido.HasIndex(p => new { p.CriadoEmUtc, p.Numero }).HasDatabaseName("IX_Pedidos_CriadoEm_Numero");
        pedido.HasIndex(p => p.ClienteId);
        pedido.HasIndex(p => p.Categoria);

        pedido.HasMany(p => p.Itens)
            .WithOne()
            .HasForeignKey(i => i.PedidoNumero)
            .OnDelete(DeleteBehavior.Cascade);

        if (EnderecoComoJson)
        {
            // Alternativa JSON: o endereço inteiro em uma coluna TEXT do SQLite.
            pedido.ComplexProperty(p => p.Endereco, endereco => endereco.ToJson("EnderecoJson"));
        }
        else
        {
            // Principal: cada componente em uma coluna com prefixo explícito.
            pedido.ComplexProperty(p => p.Endereco, endereco =>
            {
                endereco.Property(e => e.Logradouro).HasColumnName("Entrega_Logradouro").HasMaxLength(120);
                endereco.Property(e => e.Cidade).HasColumnName("Entrega_Cidade").HasMaxLength(80).IsRequired();
                endereco.Property(e => e.Uf).HasColumnName("Entrega_Uf").HasMaxLength(2).IsRequired();
                endereco.Property(e => e.Cep).HasColumnName("Entrega_Cep").HasMaxLength(8).IsRequired();
            });
        }
    }

    private static void ConfigurarItem(EntityTypeBuilder<ItemPedidoRegistro> item)
    {
        item.ToTable("ItensPedido");
        item.HasKey(i => i.Id);
        item.Property(i => i.Sku).IsRequired().HasMaxLength(40);
        item.HasIndex(i => new { i.PedidoNumero, i.Posicao }).IsUnique();
    }
}

/// <summary>
/// Contexto alternativo (Roteiro 3, etapa 4): mesmo modelo, mas o endereço é um
/// documento JSON em uma única coluna. Usado só na comparação de mapeamentos.
/// </summary>
public sealed class ContextoDePedidosComEnderecoJson(DbContextOptions<ContextoDePedidosComEnderecoJson> options)
    : ContextoDePedidos(options)
{
    protected override bool EnderecoComoJson => true;
}

/// <summary>Grava como está e marca o valor lido como UTC.</summary>
internal sealed class ConversorDeDataUtc()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        valor => valor,
        valor => DateTime.SpecifyKind(valor, DateTimeKind.Utc));
