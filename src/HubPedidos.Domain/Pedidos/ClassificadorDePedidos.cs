namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Dados usados pela classificação. É um record struct separado de <see cref="Pedido"/>
/// porque precisa representar também entradas inválidas (valor negativo, região
/// desconhecida), que um <see cref="Pedido"/> válido nunca contém.
/// </summary>
public readonly record struct PerfilDeClassificacao(
    decimal ValorTotal,
    int QuantidadeDeItens,
    Regiao Regiao,
    Prioridade Prioridade);

/// <summary>Resultado da classificação: a categoria e o motivo legível.</summary>
public sealed record ResultadoClassificacao(CategoriaProcessamento Categoria, string Motivo)
{
    public bool EhValida => Categoria != CategoriaProcessamento.Invalida;
}

/// <summary>
/// Classifica pedidos em categorias de processamento com uma expressão switch,
/// padrões de propriedade e padrões relacionais/lógicos.
/// </summary>
/// <remarks>
/// Ordem dos braços (a primeira correspondência vence):
/// 1. Dados inválidos (valor, quantidade, região ou prioridade desconhecidas).
/// 2. Alto valor: acima de R$ 10.000 exige revisão, mesmo se urgente.
/// 3. Expressa: urgente, Sul/Sudeste, até R$ 5.000 e até 20 unidades.
/// 4. Volumosa: mais de 50 unidades.
/// 5. Prioritária: prioridade Alta ou Urgente que não se encaixou acima.
/// 6. Padrão: prioridade Normal.
///
/// Não há braço de descarte (<c>_</c>): o compilador comprova a exaustividade
/// (CS8509 está configurado como erro no projeto) e rejeita braços inalcançáveis
/// (CS8510). Uma região ou prioridade fora da enumeração cai no braço explícito
/// de entrada desconhecida, nunca em "Padrão" por acidente.
/// </remarks>
public static class ClassificadorDePedidos
{
    public const decimal LimiteAltoValor = 10_000m;
    public const decimal LimiteValorExpresso = 5_000m;
    public const int LimiteItensExpresso = 20;
    public const int LimiteItensVolumoso = 50;

    public static ResultadoClassificacao Classificar(Pedido pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return Classificar(pedido.ObterPerfil());
    }

    public static ResultadoClassificacao Classificar(PerfilDeClassificacao perfil) => perfil switch
    {
        // 1. Dados inválidos: tratados primeiro para que nenhuma regra de negócio
        //    seja aplicada a valores sem significado.
        { ValorTotal: <= 0m } =>
            Invalida("O valor total deve ser maior que zero."),
        { QuantidadeDeItens: <= 0 } =>
            Invalida("A quantidade de itens deve ser maior que zero."),
        { Regiao: not (Regiao.Norte or Regiao.Nordeste or Regiao.CentroOeste or Regiao.Sudeste or Regiao.Sul) } =>
            Invalida("Região desconhecida."),
        { Prioridade: not (Prioridade.Normal or Prioridade.Alta or Prioridade.Urgente) } =>
            Invalida("Prioridade desconhecida."),

        // 2. Alto valor tem precedência: um pedido urgente de R$ 20.000 ainda exige revisão.
        { ValorTotal: > LimiteAltoValor } =>
            new(CategoriaProcessamento.AltoValor, "Valor acima de R$ 10.000 exige revisão manual."),

        // 3. Expressa: combinação de prioridade, região e limites (padrões relacionais com "and").
        {
            Prioridade: Prioridade.Urgente,
            Regiao: Regiao.Sudeste or Regiao.Sul,
            ValorTotal: <= LimiteValorExpresso,
            QuantidadeDeItens: <= LimiteItensExpresso,
        } =>
            new(CategoriaProcessamento.Expressa, "Urgente, Sul/Sudeste, até R$ 5.000 e até 20 unidades."),

        // 4. Volumosa: muitas unidades, independentemente da prioridade.
        { QuantidadeDeItens: > LimiteItensVolumoso } =>
            new(CategoriaProcessamento.Volumosa, "Mais de 50 unidades exigem separação em remessas."),

        // 5. Prioritária: Alta ou Urgente que não atendeu às condições da expressa.
        { Prioridade: Prioridade.Alta or Prioridade.Urgente } =>
            new(CategoriaProcessamento.Prioritaria, "Prioridade alta ou urgente."),

        // 6. Padrão: o único caso restante é prioridade Normal.
        { Prioridade: Prioridade.Normal } =>
            new(CategoriaProcessamento.Padrao, "Pedido sem condições especiais."),
    };

    private static ResultadoClassificacao Invalida(string motivo) =>
        new(CategoriaProcessamento.Invalida, motivo);
}
