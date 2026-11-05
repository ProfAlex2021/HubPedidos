using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processamento;

/// <summary>Um processador registrado no catálogo.</summary>
public sealed record EntradaDoCatalogo(
    CategoriaProcessamento Categoria,
    int Versao,
    Type Tipo,
    IProcessadorDePedido Instancia);

/// <summary>Como o catálogo foi construído (útil para comparar as duas abordagens).</summary>
public enum OrigemDoCatalogo
{
    Reflexao,
    RegistroExplicito,
}

/// <summary>
/// Evidência do que a descoberta examinou: torna observável o limite da reflexão.
/// </summary>
public sealed record DiagnosticoDoCatalogo(
    string Assembly,
    string NamespaceAutorizado,
    int TiposNoAssembly,
    IReadOnlyList<Type> TiposNoEscopo,
    IReadOnlyList<Type> ImplementacoesSemAtributo);

/// <summary>Configuração inconsistente detectada na construção do catálogo.</summary>
public sealed class CatalogoInvalidoException : InvalidOperationException
{
    public CatalogoInvalidoException(IReadOnlyList<string> problemas)
        : base("O catálogo de processadores é inválido:" + Environment.NewLine + "- " +
               string.Join(Environment.NewLine + "- ", problemas))
    {
        Problemas = problemas;
    }

    public IReadOnlyList<string> Problemas { get; }
}

/// <summary>
/// Catálogo determinístico de processadores, construído uma única vez na inicialização.
/// </summary>
/// <remarks>
/// Reflexão controlada:
/// - examina um único assembly recebido explicitamente (nada de AppDomain.GetAssemblies);
/// - considera apenas tipos do namespace autorizado (e seus subnamespaces);
/// - processa os tipos em ordem de nome completo, para que mensagens de erro e
///   resultados sejam sempre os mesmos;
/// - valida tudo antes de criar qualquer instância e falha com mensagem legível;
/// - depois de pronto, as consultas usam um dicionário congelado: não há nova varredura.
/// </remarks>
public sealed class CatalogoDeProcessadores
{
    /// <summary>Namespace onde ficam as estratégias de produção.</summary>
    public const string NamespacePadrao = "HubPedidos.Application.Processamento.Processadores";

    private readonly FrozenDictionary<CategoriaProcessamento, EntradaDoCatalogo> _ativos;

    private CatalogoDeProcessadores(
        IReadOnlyList<EntradaDoCatalogo> entradas,
        OrigemDoCatalogo origem,
        DiagnosticoDoCatalogo? diagnostico,
        bool exigirCoberturaCompleta)
    {
        var problemas = ValidarEntradas(entradas, exigirCoberturaCompleta);
        if (problemas.Count > 0)
        {
            throw new CatalogoInvalidoException(problemas);
        }

        Entradas = [.. entradas.OrderBy(e => e.Categoria).ThenBy(e => e.Versao)];
        Origem = origem;
        Diagnostico = diagnostico;

        // Regra de seleção: a maior versão de cada categoria fica ativa.
        _ativos = Entradas
            .GroupBy(e => e.Categoria)
            .ToFrozenDictionary(g => g.Key, g => g.MaxBy(e => e.Versao)!);
    }

    /// <summary>Todos os processadores registrados, inclusive versões antigas.</summary>
    public IReadOnlyList<EntradaDoCatalogo> Entradas { get; }

    public OrigemDoCatalogo Origem { get; }

    /// <summary>Preenchido somente quando o catálogo veio da reflexão.</summary>
    public DiagnosticoDoCatalogo? Diagnostico { get; }

    /// <summary>Processador ativo (maior versão) de cada categoria.</summary>
    public IReadOnlyCollection<EntradaDoCatalogo> Ativos => _ativos.Values;

    /// <summary>Catálogo de produção: assembly da aplicação + namespace padrão.</summary>
    public static CatalogoDeProcessadores DescobrirPadrao() =>
        Descobrir(typeof(IProcessadorDePedido).Assembly, NamespacePadrao, exigirCoberturaCompleta: true);

    /// <summary>
    /// Descobre os processadores marcados com <see cref="ProcessadorPedidoAttribute"/>
    /// somente no assembly e no namespace informados.
    /// </summary>
    public static CatalogoDeProcessadores Descobrir(
        Assembly assemblyAutorizado,
        string namespaceAutorizado,
        bool exigirCoberturaCompleta = true)
    {
        ArgumentNullException.ThrowIfNull(assemblyAutorizado);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceAutorizado);

        var todosOsTipos = ObterTipos(assemblyAutorizado);
        Type[] tiposNoEscopo =
        [
            .. todosOsTipos
                .Where(tipo => PertenceAoNamespace(tipo, namespaceAutorizado))
                .Where(tipo => !tipo.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                .OrderBy(tipo => tipo.FullName, StringComparer.Ordinal),
        ];

        var contrato = typeof(IProcessadorDePedido);
        var problemas = new List<string>();
        var candidatos = new List<(Type Tipo, ProcessadorPedidoAttribute Atributo)>();
        var semAtributo = new List<Type>();

        foreach (var tipo in tiposNoEscopo)
        {
            var atributo = tipo.GetCustomAttribute<ProcessadorPedidoAttribute>(inherit: false);
            var implementaContrato = contrato.IsAssignableFrom(tipo);

            if (atributo is null)
            {
                // Implementação concreta sem marcação: não entra, mas fica visível no diagnóstico.
                if (implementaContrato && tipo is { IsClass: true, IsAbstract: false })
                {
                    semAtributo.Add(tipo);
                }

                continue;
            }

            // Metadados inconsistentes não são ignorados em silêncio: viram erro legível.
            switch (tipo)
            {
                case { IsClass: false } or { IsAbstract: true }:
                    problemas.Add($"'{tipo.FullName}' está marcado com [ProcessadorPedido], mas não é uma classe concreta.");
                    break;
                case var _ when !implementaContrato:
                    problemas.Add($"'{tipo.FullName}' está marcado com [ProcessadorPedido], mas não implementa {contrato.Name}.");
                    break;
                case var _ when tipo.GetConstructor(Type.EmptyTypes) is null:
                    problemas.Add($"'{tipo.FullName}' precisa declarar um construtor público sem parâmetros.");
                    break;
                default:
                    candidatos.Add((tipo, atributo));
                    break;
            }
        }

        // Valida categorias, versões e duplicidades antes de instanciar qualquer tipo.
        problemas.AddRange(ValidarMetadados(candidatos.Select(c => (c.Atributo.Categoria, c.Atributo.Versao, c.Tipo))));
        if (problemas.Count > 0)
        {
            throw new CatalogoInvalidoException(problemas);
        }

        var entradas = candidatos
            .Select(c => new EntradaDoCatalogo(
                c.Atributo.Categoria,
                c.Atributo.Versao,
                c.Tipo,
                (IProcessadorDePedido)Activator.CreateInstance(c.Tipo)!))
            .ToArray();

        var diagnostico = new DiagnosticoDoCatalogo(
            assemblyAutorizado.GetName().Name ?? "(sem nome)",
            namespaceAutorizado,
            todosOsTipos.Length,
            tiposNoEscopo,
            semAtributo);

        return new CatalogoDeProcessadores(entradas, OrigemDoCatalogo.Reflexao, diagnostico, exigirCoberturaCompleta);
    }

    /// <summary>
    /// Alternativa sem reflexão: a lista é escrita à mão (ver <see cref="RegistroExplicitoDeProcessadores"/>).
    /// As mesmas validações de duplicidade e cobertura são aplicadas.
    /// </summary>
    public static CatalogoDeProcessadores CriarComRegistroExplicito(
        IEnumerable<(CategoriaProcessamento Categoria, int Versao, IProcessadorDePedido Instancia)> registros,
        bool exigirCoberturaCompleta = true)
    {
        ArgumentNullException.ThrowIfNull(registros);
        var entradas = registros
            .Select(r => new EntradaDoCatalogo(r.Categoria, r.Versao, r.Instancia.GetType(), r.Instancia))
            .ToArray();

        var problemas = ValidarMetadados(entradas.Select(e => (e.Categoria, e.Versao, e.Tipo)));
        if (problemas.Count > 0)
        {
            throw new CatalogoInvalidoException(problemas);
        }

        return new CatalogoDeProcessadores(entradas, OrigemDoCatalogo.RegistroExplicito, diagnostico: null, exigirCoberturaCompleta);
    }

    /// <summary>Obtém o processador ativo da categoria; não repete nenhuma reflexão.</summary>
    public bool TentarObter(CategoriaProcessamento categoria, out EntradaDoCatalogo entrada) =>
        _ativos.TryGetValue(categoria, out entrada!);

    private static List<string> ValidarMetadados(
        IEnumerable<(CategoriaProcessamento Categoria, int Versao, Type Tipo)> metadados)
    {
        var lista = metadados.ToList();
        var problemas = new List<string>();

        foreach (var (categoria, versao, tipo) in lista.OrderBy(m => m.Tipo.FullName, StringComparer.Ordinal))
        {
            if (categoria == CategoriaProcessamento.Invalida || !Enum.IsDefined(categoria))
            {
                problemas.Add($"'{tipo.FullName}' declara a categoria '{categoria}', que não pode ter processador.");
            }

            if (versao < 1)
            {
                problemas.Add($"'{tipo.FullName}' declara a versão {versao}; a versão deve ser maior ou igual a 1.");
            }
        }

        var duplicados = lista
            .GroupBy(m => (m.Categoria, m.Versao))
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key.Categoria)
            .ThenBy(g => g.Key.Versao);

        foreach (var grupo in duplicados)
        {
            var nomes = grupo
                .Select(m => $"'{m.Tipo.FullName}'")
                .Order(StringComparer.Ordinal);
            problemas.Add(
                $"A categoria '{grupo.Key.Categoria}' versão {grupo.Key.Versao} foi declarada por mais de um tipo: {string.Join(" e ", nomes)}.");
        }

        return problemas;
    }

    private static List<string> ValidarEntradas(IReadOnlyList<EntradaDoCatalogo> entradas, bool exigirCoberturaCompleta)
    {
        var problemas = new List<string>();
        if (!exigirCoberturaCompleta)
        {
            return problemas;
        }

        var cobertas = entradas.Select(e => e.Categoria).ToHashSet();
        var faltantes = Enum.GetValues<CategoriaProcessamento>()
            .Where(c => c != CategoriaProcessamento.Invalida && !cobertas.Contains(c))
            .ToArray();

        if (faltantes.Length > 0)
        {
            problemas.Add($"Nenhum processador registrado para: {string.Join(", ", faltantes)}.");
        }

        return problemas;
    }

    private static bool PertenceAoNamespace(Type tipo, string namespaceAutorizado) =>
        tipo.Namespace is { } ns &&
        (ns.Equals(namespaceAutorizado, StringComparison.Ordinal) ||
         ns.StartsWith(namespaceAutorizado + ".", StringComparison.Ordinal));

    private static Type[] ObterTipos(Assembly assemblyAutorizado)
    {
        try
        {
            // Somente o assembly recebido é examinado.
            return assemblyAutorizado.GetTypes();
        }
        catch (ReflectionTypeLoadException excecao)
        {
            // Um catálogo parcial seria pior que nenhum: falha com o nome do assembly.
            throw new InvalidOperationException(
                $"Não foi possível examinar todos os tipos do assembly '{assemblyAutorizado.GetName().Name}'.",
                excecao);
        }
    }
}
