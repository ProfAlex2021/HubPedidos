using System.Buffers.Text;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using HubPedidos.Application.Pedidos.Contratos;
using HubPedidos.Application.Pedidos.Portas;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Infrastructure.Persistencia.Modelos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HubPedidos.Infrastructure.Consultas;

/// <summary>
/// Consultas de leitura do painel; implementa a porta <see cref="IConsultasDePedidos"/>.
/// Nada aqui altera dados: projeções direto para DTO (que não são rastreadas) e, onde
/// entidades são lidas, <c>AsNoTracking</c>.
/// </summary>
public sealed class ConsultasDePedidos(ContextoDePedidos contexto) : IConsultasDePedidos
{
    public const int TamanhoMaximoDaPagina = LimitesDeConsulta.TamanhoMaximoDaPagina;

    /// <summary>
    /// Pela porta, o detalhe usa a consulta compilada (decisão medida no Roteiro 3).
    /// A versão comum continua pública para comparação e testes.
    /// </summary>
    Task<PedidoResponse?> IConsultasDePedidos.ObterDetalheAsync(PedidoId id, CancellationToken cancellationToken) =>
        ObterDetalheCompiladoAsync(id);

    /// <summary>Projeção do detalhe, compartilhada pelas versões normal e compilada.</summary>
    private static readonly Expression<Func<PedidoRegistro, LinhaDeDetalhe>> ProjecaoDeDetalhe = p => new LinhaDeDetalhe(
        p.Id,
        p.ClienteId,
        p.Regiao,
        p.Prioridade,
        p.Categoria,
        p.MotivoDaCategoria,
        p.Processador,
        p.PrazoEstimadoEmDias,
        p.Situacao,
        p.CriadoEmUtc,
        p.ValorTotalCentavos,
        p.Endereco,
        p.Itens.OrderBy(i => i.Posicao).Select(i => new LinhaDeItem(i.Sku, i.Quantidade, i.PrecoUnitarioCentavos)).ToList());

    /// <summary>
    /// Consulta compilada (etapa 3 do Roteiro 3): consulta estável, repetida e com
    /// parâmetro escalar. Evita, a cada chamada, comparar a árvore de expressão com o
    /// cache interno do EF.
    /// </summary>
    /// <remarks>
    /// Correção no Roteiro 6: um delegate compilado só pode ser usado com UM modelo do EF.
    /// O delegate estático único falhou quando vários hosts (testes de integração) criaram
    /// modelos distintos no mesmo processo. Agora há um delegate por modelo; em produção
    /// existe um único modelo, então o comportamento medido não muda.
    /// </remarks>
    private static readonly ConditionalWeakTable<IModel, Func<ContextoDePedidos, Guid, Task<LinhaDeDetalhe?>>> DetalheCompiladoPorModelo = new();

    private static Func<ContextoDePedidos, Guid, Task<LinhaDeDetalhe?>> CompilarDetalhe() =>
        EF.CompileAsyncQuery((ContextoDePedidos c, Guid id) =>
            c.Pedidos.Where(p => p.Id == id).Select(ProjecaoDeDetalhe).FirstOrDefault());

    /// <summary>Consulta (ainda não executada) do painel; exposta para inspeção do SQL.</summary>
    public IQueryable<LinhaDeResumo> ConsultaDeResumos(FiltroDePedidos filtro) =>
        Ordenar(Filtrar(contexto.Pedidos, filtro))
            .Select(p => new LinhaDeResumo(
                p.Numero,
                p.Id,
                p.ClienteId,
                p.CriadoEmUtc,
                p.Categoria,
                p.Situacao,
                p.Itens.Sum(i => i.Quantidade), // agregação traduzida para SQL (subconsulta)
                p.ValorTotalCentavos));

    /// <summary>Paginação por deslocamento com ordenação determinística e contagem total.</summary>
    public async Task<PaginaDeResultados<PedidoResumo>> ListarResumosAsync(
        FiltroDePedidos filtro, int pagina, int tamanhoDaPagina, CancellationToken cancellationToken = default)
    {
        ValidarPagina(pagina, tamanhoDaPagina);

        var total = await Filtrar(contexto.Pedidos, filtro).CountAsync(cancellationToken);
        var linhas = await ConsultaDeResumos(filtro)
            .Skip((pagina - 1) * tamanhoDaPagina)
            .Take(tamanhoDaPagina)
            .ToListAsync(cancellationToken);

        return new PaginaDeResultados<PedidoResumo>([.. linhas.Select(ParaResumo)], pagina, tamanhoDaPagina, total);
    }

    /// <summary>
    /// Extensão: paginação por cursor (keyset). Em vez de pular N linhas, continua a
    /// partir da última linha vista: (CriadoEmUtc, Numero) menores que os do cursor.
    /// </summary>
    public async Task<PaginaPorCursor<PedidoResumo>> ListarResumosPorCursorAsync(
        FiltroDePedidos filtro, string? cursor, int tamanhoDaPagina, CancellationToken cancellationToken = default)
    {
        ValidarPagina(1, tamanhoDaPagina);

        var consulta = Filtrar(contexto.Pedidos, filtro);
        if (cursor is not null)
        {
            var (criado, numero) = DecodificarCursor(cursor);
            consulta = consulta.Where(p => p.CriadoEmUtc < criado || (p.CriadoEmUtc == criado && p.Numero < numero));
        }

        var linhas = await Ordenar(consulta)
            .Select(p => new LinhaDeResumo(p.Numero, p.Id, p.ClienteId, p.CriadoEmUtc, p.Categoria, p.Situacao, p.Itens.Sum(i => i.Quantidade), p.ValorTotalCentavos))
            .Take(tamanhoDaPagina + 1) // uma linha a mais só para saber se há próxima página
            .ToListAsync(cancellationToken);

        var temProxima = linhas.Count > tamanhoDaPagina;
        var pagina = linhas.Take(tamanhoDaPagina).ToList();
        var proximo = temProxima ? CodificarCursor(pagina[^1].CriadoEmUtc, pagina[^1].Numero) : null;
        return new PaginaPorCursor<PedidoResumo>([.. pagina.Select(ParaResumo)], proximo);
    }

    /// <summary>Detalhe de um pedido com itens em UMA consulta (sem N+1).</summary>
    public async Task<PedidoResponse?> ObterDetalheAsync(PedidoId id, CancellationToken cancellationToken = default)
    {
        var linha = await contexto.Pedidos
            .Where(p => p.Id == id.Valor)
            .Select(ProjecaoDeDetalhe)
            .FirstOrDefaultAsync(cancellationToken);
        return linha is null ? null : ParaResposta(linha);
    }

    /// <summary>Mesmo resultado de <see cref="ObterDetalheAsync"/>, via consulta compilada.</summary>
    public async Task<PedidoResponse?> ObterDetalheCompiladoAsync(PedidoId id)
    {
        var consulta = DetalheCompiladoPorModelo.GetValue(contexto.Model, _ => CompilarDetalhe());
        var linha = await consulta(contexto, id.Valor);
        return linha is null ? null : ParaResposta(linha);
    }

    /// <summary>Relatório de pedidos com itens, paginado, com a estratégia escolhida.</summary>
    public async Task<IReadOnlyList<PedidoResponse>> ListarRelatorioAsync(
        int pagina, int tamanhoDaPagina, EstrategiaDeCarregamento estrategia, CancellationToken cancellationToken = default)
    {
        ValidarPagina(pagina, tamanhoDaPagina);
        var pedidosDaPagina = Ordenar(contexto.Pedidos).Skip((pagina - 1) * tamanhoDaPagina).Take(tamanhoDaPagina);

        switch (estrategia)
        {
            case EstrategiaDeCarregamento.Projecao:
                return [.. (await pedidosDaPagina.Select(ProjecaoDeDetalhe).ToListAsync(cancellationToken)).Select(ParaResposta)];

            case EstrategiaDeCarregamento.Dividida:
                return [.. (await pedidosDaPagina.Select(ProjecaoDeDetalhe).AsSplitQuery().ToListAsync(cancellationToken)).Select(ParaResposta)];

            case EstrategiaDeCarregamento.Include:
                var registros = await pedidosDaPagina.AsNoTracking().Include(p => p.Itens).ToListAsync(cancellationToken);
                return [.. registros.Select(r => ParaResposta(new LinhaDeDetalhe(
                    r.Id, r.ClienteId, r.Regiao, r.Prioridade, r.Categoria, r.MotivoDaCategoria, r.Processador,
                    r.PrazoEstimadoEmDias, r.Situacao, r.CriadoEmUtc, r.ValorTotalCentavos, r.Endereco,
                    [.. r.Itens.OrderBy(i => i.Posicao).Select(i => new LinhaDeItem(i.Sku, i.Quantidade, i.PrecoUnitarioCentavos))])))];

            default:
                throw new ArgumentOutOfRangeException(nameof(estrategia), estrategia, "Estratégia desconhecida.");
        }
    }

    /// <summary>Agregação feita no banco (GROUP BY); só totais saem da consulta.</summary>
    public async Task<IReadOnlyList<EstatisticaPorCategoria>> ContarPorCategoriaAsync(DateTime desdeUtc, CancellationToken cancellationToken)
    {
        var linhas = await contexto.Pedidos
            .Where(p => p.CriadoEmUtc >= desdeUtc)
            .GroupBy(p => p.Categoria)
            .Select(g => new { Categoria = g.Key, Quantidade = g.Count(), Centavos = g.Sum(p => p.ValorTotalCentavos) })
            .ToListAsync(cancellationToken);

        return [.. linhas
            .OrderBy(l => l.Categoria)
            .Select(l => new EstatisticaPorCategoria(l.Categoria.ToString(), l.Quantidade, Dinheiro.DeCentavos(l.Centavos)))];
    }

    internal static IQueryable<PedidoRegistro> Filtrar(IQueryable<PedidoRegistro> consulta, FiltroDePedidos filtro)
    {
        // Cada valor do usuário entra como variável local capturada: o EF a transforma em
        // PARÂMETRO SQL (@desde, @minimo...). A forma do comando não muda com os valores.
        if (filtro.CriadoDesdeUtc is { } desde)
        {
            consulta = consulta.Where(p => p.CriadoEmUtc >= desde);
        }

        if (filtro.CriadoAteUtc is { } ate)
        {
            consulta = consulta.Where(p => p.CriadoEmUtc <= ate);
        }

        if (filtro.Categoria is { } textoCategoria)
        {
            if (!Enum.TryParse<CategoriaProcessamento>(textoCategoria, ignoreCase: true, out var categoria) || !Enum.IsDefined(categoria))
            {
                throw new ArgumentException($"Categoria '{textoCategoria}' desconhecida.", nameof(filtro));
            }

            consulta = consulta.Where(p => p.Categoria == categoria);
        }

        if (filtro.ValorMinimo is { } valorMinimo)
        {
            var minimoEmCentavos = Dinheiro.ParaCentavos(valorMinimo);
            consulta = consulta.Where(p => p.ValorTotalCentavos >= minimoEmCentavos);
        }

        if (!string.IsNullOrWhiteSpace(filtro.ClienteId))
        {
            var cliente = filtro.ClienteId.Trim();
            consulta = consulta.Where(p => p.ClienteId == cliente);
        }

        return consulta;
    }

    /// <summary>Ordenação determinística: data decrescente e, no empate, o número.</summary>
    private static IOrderedQueryable<PedidoRegistro> Ordenar(IQueryable<PedidoRegistro> consulta) =>
        consulta.OrderByDescending(p => p.CriadoEmUtc).ThenByDescending(p => p.Numero);

    private static void ValidarPagina(int pagina, int tamanhoDaPagina)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pagina, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tamanhoDaPagina, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tamanhoDaPagina, TamanhoMaximoDaPagina);
    }

    private static PedidoResumo ParaResumo(LinhaDeResumo l) => new(
        l.Id.ToString(), l.ClienteId, l.CriadoEmUtc, l.Categoria.ToString(), l.Situacao.ToString(),
        l.QuantidadeDeItens, Dinheiro.DeCentavos(l.ValorTotalCentavos));

    private static PedidoResponse ParaResposta(LinhaDeDetalhe l)
    {
        ItemPedidoResponse[] itens =
        [
            .. l.Itens.Select(i =>
            {
                var preco = Dinheiro.DeCentavos(i.PrecoUnitarioCentavos);
                return new ItemPedidoResponse(i.Sku, i.Quantidade, preco, i.Quantidade * preco);
            }),
        ];

        return new PedidoResponse
        {
            Id = l.Id.ToString(),
            ClienteId = l.ClienteId,
            Regiao = l.Regiao.ToString(),
            Prioridade = l.Prioridade.ToString(),
            Categoria = l.Categoria.ToString(),
            MotivoDaCategoria = l.MotivoDaCategoria,
            Processador = l.Processador,
            PrazoEstimadoEmDias = l.PrazoEstimadoEmDias,
            ValorTotal = Dinheiro.DeCentavos(l.ValorTotalCentavos),
            QuantidadeDeItens = itens.Sum(i => i.Quantidade),
            Itens = Array.AsReadOnly(itens),
            Situacao = l.Situacao.ToString(),
            CriadoEmUtc = l.CriadoEmUtc,
            Endereco = l.Endereco is { } e ? new EnderecoResponse(e.Logradouro, e.Cidade, e.Uf, e.Cep) : null,
        };
    }

    private static string CodificarCursor(DateTime criadoEmUtc, long numero) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{criadoEmUtc.Ticks}.{numero}")));

    private static (DateTime CriadoEmUtc, long Numero) DecodificarCursor(string cursor)
    {
        try
        {
            var partes = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split('.');
            return (new DateTime(long.Parse(partes[0], CultureInfo.InvariantCulture), DateTimeKind.Utc),
                    long.Parse(partes[1], CultureInfo.InvariantCulture));
        }
        catch (Exception excecao) when (excecao is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("Cursor inválido.", nameof(cursor), excecao);
        }
    }
}

/// <summary>Colunas do painel, como lidas do banco (antes da conversão para o DTO público).</summary>
public sealed record LinhaDeResumo(
    long Numero, Guid Id, string ClienteId, DateTime CriadoEmUtc,
    CategoriaProcessamento Categoria, SituacaoPedido Situacao, int QuantidadeDeItens, long ValorTotalCentavos);

/// <summary>Colunas do detalhe, como lidas do banco.</summary>
public sealed record LinhaDeDetalhe(
    Guid Id, string ClienteId, Regiao Regiao, Prioridade Prioridade, CategoriaProcessamento Categoria,
    string MotivoDaCategoria, string Processador, int PrazoEstimadoEmDias, SituacaoPedido Situacao,
    DateTime CriadoEmUtc, long ValorTotalCentavos, EnderecoRegistro? Endereco, List<LinhaDeItem> Itens);

/// <summary>Item como lido do banco.</summary>
public sealed record LinhaDeItem(string Sku, int Quantidade, long PrecoUnitarioCentavos);
