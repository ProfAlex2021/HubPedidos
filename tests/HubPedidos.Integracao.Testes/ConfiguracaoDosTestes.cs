// Extensão do Roteiro 8: testes de integração em paralelo por método. Isso só é seguro
// porque não há estado compartilhado: cada teste cria a sua FabricaDaAplicacao, com
// banco em memória de nome único, senhas próprias e host próprio.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel, Workers = 0)]
