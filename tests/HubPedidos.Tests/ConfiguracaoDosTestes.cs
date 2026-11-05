// Os testes unitários não compartilham estado; podem rodar em paralelo por método.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel, Workers = 0)]
