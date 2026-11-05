using HubPedidos.Application.Pedidos.Contratos;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Resultado da interpretação de uma linha do lote: um DTO pronto para o mapeador
/// ou uma mensagem de erro de formato. É um record struct para não alocar um objeto
/// extra por linha além do próprio DTO.
/// </summary>
public readonly record struct LinhaImportada(int Numero, CriarPedidoRequest? Pedido, string? Erro)
{
    public bool Valida => Pedido is not null;

    public static LinhaImportada Ok(int numero, CriarPedidoRequest pedido) => new(numero, pedido, null);

    public static LinhaImportada Falha(int numero, string erro) => new(numero, null, erro);
}

/// <summary>
/// Mensagens compartilhadas pelas três implementações. Um teste exige que todas
/// produzam exatamente o mesmo texto para a mesma entrada inválida.
/// </summary>
public static class MensagensDeImportacao
{
    public const string LinhaVazia = "A linha está vazia.";
    public const string QuantidadeDeCampos = "A linha deve conter 4 campos separados por ';': cliente;regiao;prioridade;itens.";
    public const string SemItens = "A linha deve conter ao menos um item.";
    public const string Utf8Invalido = "A linha contém bytes UTF-8 inválidos.";

    public static string LinhaLonga(int limiteEmBytes) =>
        $"A linha excede o limite de {limiteEmBytes} bytes.";

    public static string ItemMalformado(int posicao) =>
        $"O item {posicao} deve ter 3 partes separadas por ':': sku:quantidade:preco.";

    public static string QuantidadeInvalida(int posicao) =>
        $"O item {posicao} tem quantidade inválida (use um número inteiro).";

    public static string PrecoInvalido(int posicao) =>
        $"O item {posicao} tem preço inválido (use ponto como separador decimal).";
}
