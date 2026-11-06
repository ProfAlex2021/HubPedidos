namespace HubPedidos.Api.Composicao;

/// <summary>
/// Mensagens de log da inicialização, geradas em tempo de compilação ([LoggerMessage]).
/// </summary>
/// <remarks>
/// Roteiro 8 (CA1848/CA1873): o gerador cria um delegate por mensagem e só monta os
/// argumentos quando o nível está habilitado. Na inicialização o ganho de desempenho é
/// pequeno; adotamos o mesmo padrão em toda a solução para que a regra permaneça
/// ativa como aviso e aponte qualquer log novo escrito do jeito antigo.
/// </remarks>
internal static partial class RegistrosDeInicializacao
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Catálogo de processadores pronto: {Quantidade} registros, {Ativos} ativos.")]
    public static partial void CatalogoPronto(this ILogger logger, int quantidade, int ativos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations aplicadas.")]
    public static partial void MigracoesAplicadas(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite journal_mode = {Modo}.")]
    public static partial void ModoDeJournal(this ILogger logger, object? modo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Massa de dados gerada: {Total} pedidos no banco.")]
    public static partial void MassaGerada(this ILogger logger, int total);
}
