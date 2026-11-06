# Roteiro 7 — SignalR, reconexão, streaming, cache de resposta e ETags

**Objetivo:** notificar o cliente sobre o processamento de pedidos e reduzir transferências
de leitura com cache e validação condicional corretos.

## Como executar

```bash
dotnet test
```

Para usar a página de acompanhamento, crie antes os usuários de teste. O script gera
**senhas aleatórias** e as grava em User Secrets (fora do repositório):

```bash
powershell -ExecutionPolicy Bypass -File scripts/criar-usuarios-teste.ps1
```

```bash
dotnet user-secrets list --project src/HubPedidos.Api
```

```bash
dotnet run --project src/HubPedidos.Api --launch-profile http
```

Abra `http://localhost:5080/`, entre como `ana` (cliente-001), `bruno` (cliente-002) ou
`operador` e informe o identificador de um pedido. O cliente JavaScript do SignalR
(`@microsoft/signalr` 10.0.11) é restaurado pelo LibMan local (`dotnet tool restore` e
`dotnet libman restore --root src/HubPedidos.Api`); os arquivos já restaurados também
estão versionados em `wwwroot/lib`, para a página funcionar sem rede.

| Rota | Acesso | O que faz |
|---|---|---|
| `POST /api/autenticacao/token` | anônimo | Usuário + senha → bearer token (1 h) |
| `GET /api/v2/pedidos/{id}` | autenticado | Consulta com `ETag`; `If-None-Match` → 304 |
| `POST /api/v2/pedidos/{id}/cancelamento` | autenticado | Com `If-Match` obsoleto → 412 (extensão) |
| `GET /api/estatisticas/categorias?dias=N` | anônimo | Leitura pública com output cache |
| `POST /api/admin/usuarios/{usuario}/revogacao` | papel `operador` | Revoga o usuário e o tira dos grupos |
| `/hubs/pedidos` | autenticado | Hub `PedidosHub` (`AcompanharPedido`, `DeixarDeAcompanhar`) |

## Arquivos produzidos ou alterados

| Arquivo | Papel |
|---|---|
| `src/HubPedidos.Api/Seguranca/Autenticacao.cs` | Papéis, usuários de teste (configuração), revogação, emissão de token e a regra `AutorizacaoDePedidos` |
| `src/HubPedidos.Api/TempoReal/PedidosHub.cs` | Hub, contrato `PedidoAtualizado`, cliente tipado `IClienteDePedidos`, registro de acompanhamentos e `ServicoDeRevogacao` |
| `src/HubPedidos.Api/TempoReal/NotificadorDaApi.cs` | Implementa a porta `INotificadorDePedidos`: invalida a tag do cache e publica no grupo |
| `src/HubPedidos.Api/Cache/ValidacaoCondicional.cs` | Serialização canônica, ETag SHA-256, comparação `If-None-Match` (fraca) e `If-Match` (forte) |
| `src/HubPedidos.Api/Endpoints/ConsultaDePedidoVersionadaEndpoints.cs` | v2 autenticada com ETag/304 e cancelamento condicional |
| `src/HubPedidos.Api/Endpoints/TempoRealEndpoints.cs` | Token, revogação, estatísticas em cache e `MapHub` |
| `src/HubPedidos.Api/Composicao/ComposicaoDaAplicacao.cs` | `AddTempoRealECache`: bearer token, autorização, SignalR, política de output cache |
| `src/HubPedidos.Api/Program.cs` | Ordem do pipeline: arquivos estáticos → autenticação → autorização → output cache |
| `src/HubPedidos.Api/wwwroot/` | Página `index.html` e `js/acompanhamento.js` (estados, reconexão, reconciliação) |
| `src/HubPedidos.Api/libman.json`, `dotnet-tools.json` | Cliente JS do SignalR por ferramenta local (LibMan CLI) |
| `src/HubPedidos.Application/.../ConsultasDePedidos.cs`, `Infrastructure/Consultas/ConsultasDePedidos.cs` | `ContarPorCategoriaAsync` (GROUP BY no banco) para a leitura pública |
| `scripts/criar-usuarios-teste.ps1` | Usuários de teste com senhas aleatórias em User Secrets |
| `tests/.../Api/CacheEValidacaoCondicionalTestes.cs`, `TempoRealTestes.cs` | Testes obrigatórios e da extensão |
| `tests/.../Api/FabricaComBancoTemporario.cs` | Usuários com senha aleatória por execução, token e conexão SignalR em processo |

## Decisões técnicas

### Etapa 1 — Eventos com SignalR

- **Contrato mínimo:** `PedidoAtualizado(PedidoId, Situacao, Sequencia, InstanteUtc)`. Não
  vai o objeto de domínio, nem o cliente, nem os valores. O evento só avisa que algo mudou;
  quem quiser os detalhes consulta a API, que aplica a autorização outra vez.
- **Cliente tipado** (`Hub<IClienteDePedidos>`): o nome do método do cliente é verificado
  pelo compilador, sem as strings de `SendAsync("...")`.
- **Autorização antes do grupo:** `AcompanharPedido` chama `AutorizacaoDePedidos` e só
  então faz `Groups.AddToGroupAsync`. O grupo `pedido:{id}` é só um endereço de entrega,
  não um controle de acesso. Quem não pode ver o pedido recebe `HubException` com a mesma
  mensagem para "não existe" e "não é seu", para não revelar quais pedidos existem.
- **Publicar depois de persistir:** a porta `INotificadorDePedidos` (criada no Roteiro 6) é
  chamada pelo serviço só depois do `SaveChanges`, então o evento nunca anuncia um estado
  que a API ainda não mostra. O SignalR substitui o `NotificadorPorLog` só na composição da
  API: nenhuma linha da Application mudou (inversão de dependência do Roteiro 6).
- **Ordem e duplicidade:** `Sequencia` são os ticks do instante da mudança. O cliente
  descarta eventos com sequência menor ou igual à última aplicada.

### Etapa 2 — Reconexão e reconciliação

- **Autenticação do hub:** bearer token (`AddBearerToken`, da própria ASP.NET Core). Os
  navegadores não mandam cabeçalho `Authorization` em WebSocket, então o token vai na
  query `access_token`, aceita **somente** no caminho `/hubs/pedidos` (`OnMessageReceived`).
  Escolhi o bearer token em vez de JWT porque não exige pacote nem chave de assinatura e é
  o recurso mostrado no material para APIs; a limitação é ser um formato opaco da ASP.NET.
- **Estados no cliente:** `withAutomaticReconnect([0, 2000, 10000, 30000])` (backoff),
  com `onreconnecting` (aviso "reconectando", botões desabilitados), `onreconnected` e
  `onclose` (desconectado, com o botão "Conectar novamente").
- **Reconectar é uma conexão nova:** o SignalR não guarda grupos nem reenvia eventos
  perdidos. Em `onreconnected` o cliente chama `AcompanharPedido` de novo (o servidor
  **revalida** a permissão) e faz um GET condicional para reconciliar com a fonte HTTP.
  Só existe uma requisição de reconciliação por vez; se chegar um evento no meio, ela é
  marcada para repetir.
- **Revogação:** o operador revoga um usuário; o servidor tira todas as conexões dele dos
  grupos (`RegistroDeAcompanhamentos`), envia `AcessoRevogado`, e dali em diante o login,
  a consulta v2 e o hub negam acesso, mesmo com o token antigo ainda válido.
  *Limitação didática:* o registro de revogações fica em memória e se perde ao reiniciar.

### Etapa 3 — ETag

- **Representação:** a ETag é o SHA-256 dos bytes JSON da resposta v2 (`"sha256-…"`), uma
  ETag **forte**: muda quando qualquer campo serializado muda e se repete quando nada
  muda. Calculei sobre os bytes em vez de usar uma coluna de versão no banco para não
  mudar o esquema e porque v1 e v2 têm representações diferentes do mesmo pedido.
- **If-None-Match:** comparação fraca (RFC 9110), aceita `W/` e `*`. Valor obsoleto ou lixo
  recebe 200 com a representação atual. O 304 tem corpo vazio e repete `ETag` e
  `Cache-Control`.
- **Autorização vem antes da ETag:** quem não pode ver o pedido recebe 404 mesmo com a
  ETag correta. Se o 304 viesse antes, um usuário poderia confirmar o estado de um pedido
  alheio.
- **Cabeçalhos:** `Cache-Control: private, no-cache` (só o navegador do próprio usuário
  guarda, e sempre revalida) e `Vary: Authorization`.
- **Decisão que afeta o Roteiro 6:** a v2 passou a exigir autenticação, porque é uma
  resposta personalizada. A v1 continua anônima, como o cliente v1 sempre usou (o teste de
  compatibilidade do Roteiro 6 continua passando). O teste `V2_tem_o_contrato_novo...`
  foi ajustado para usar o operador.

### Etapa 4 — Output cache com limites

- **Leitura escolhida:** `GET /api/estatisticas/categorias?dias=N`, pública e idempotente
  (quantidade e valor por categoria, agregados com GROUP BY no banco). Não tem dado de
  usuário, então pode ser compartilhada.
- **Rotas excluídas:** nenhuma rota autenticada recebe `CacheOutput`. Além disso, o output
  cache padrão da ASP.NET não guarda respostas de requisições com `Authorization` ou que
  gravam cookies. A v2 personalizada usa ETag (validação), não cache compartilhado.
- **Pipeline:** `UseAuthentication` → `UseAuthorization` → `UseOutputCache` (verificado
  por teste).
- **Política:** expira em 30 s, varia pela query `dias` e leva a tag `pedidos`. O
  `NotificadorDaApi` chama `EvictByTagAsync("pedidos")` a cada mudança de pedido, antes
  de publicar o evento.
- **Risco de inconsistência (controlado):** uma mudança que não passe pelo notificador
  (por exemplo, edição direta no banco) fica invisível por até 30 s. Por isso a expiração
  é curta. O cabeçalho `X-Gerado-Em` mostra quando a resposta foi calculada e permite
  medir acertos.

## Extensão realizada

**If-Match para atualização otimista e tratamento de 412:** `POST
/api/v2/pedidos/{id}/cancelamento` aceita `If-Match`. Com ETag diferente da atual → 412,
com a ETag atual na resposta e o pedido intocado; com a ETag atual → 204. Sem o cabeçalho,
vale a regra normal de cancelamento, como na v1. A página usa esse caminho no botão
"Cancelar (If-Match)".

*Limitação registrada no código:* comparar a ETag e cancelar são dois passos. Uma mudança
entre eles ainda é barrada pela regra do domínio (só "Recebido" pode ser cancelado), mas
não pela ETag. Uma garantia atômica exigiria um token de concorrência na tabela.

## Testes obrigatórios

| Teste obrigatório (relatório) | Onde está |
|---|---|
| Cliente recebe atualização somente após autorização validada no servidor; o grupo não substitui a autorização | `TempoRealTestes.Evento_chega_so_ao_cliente_autorizado_e_depois_da_mudanca_persistida` (Bruno recebe `HubException` e não recebe o evento da Ana), `Conexao_sem_autenticacao_e_recusada` |
| Reconexão revalida a autorização, restaura a assinatura permitida e reconcilia eventos perdidos | `TempoRealTestes.Reconexao_exige_novo_ingresso_e_estado_perdido_e_recuperado_por_HTTP`, `Revogacao_remove_associacoes_e_reconexao_revalida_a_autorizacao`, `Somente_operador_pode_revogar` |
| ETag muda quando a representação muda e permanece estável quando não muda | `CacheEValidacaoCondicionalTestes.ETag_e_estavel_e_If_None_Match_valido_devolve_304_sem_corpo`, `ETag_muda_quando_a_representacao_muda_e_valor_obsoleto_recebe_200` |
| If-None-Match válido → 304 sem corpo; valor obsoleto → representação atual | Mesmos dois testes (forte, `W/`, `*`, obsoleto e inválido) |
| Output cache registrado após autenticação e autorização e sem compartilhar respostas privadas | `Output_cache_e_registrado_depois_de_autenticacao_e_autorizacao`, `Autorizacao_vem_antes_da_ETag_e_respostas_privadas_nao_sao_compartilhadas`, `Output_cache_reutiliza_a_leitura_publica_varia_por_query_e_e_invalidado_por_tag` |
| Extensão If-Match/412 | `If_Match_obsoleto_devolve_412_e_nao_altera_o_pedido` |

**Prova de que os testes detectam a falha:** com a checagem de autorização do hub
desligada de propósito, 2 testes falharam ("Expected exception of type HubException").
A checagem foi restaurada.

Os testes de SignalR usam o cliente .NET real (`Microsoft.AspNetCore.SignalR.Client`) com
transporte Long Polling pelo `TestServer`, em processo. Para provar que um evento **não**
chegou, o teste dispara depois um evento que o cliente pode receber e confere que ele é o
primeiro da fila. Isso é determinístico e dispensa esperas por tempo.

## Verificação manual (06/11/2026)

Usuários criados com o script, API em `http://localhost:5080`:

- Ana: `GET /api/v2/pedidos/{id}` → 200, `ETag: "sha256-70a3…"`, `Cache-Control: private,
  no-cache`, `Vary: Authorization`; com `If-None-Match` → **304, 0 bytes**.
- Bruno com a mesma ETag → **404**; anônimo → **401**.
- `If-Match` obsoleto → **412**; `If-Match` atual → **204**; a ETag antiga depois disso
  → 200 com `"situacao":"cancelado"`.
- Estatísticas: **200 leituras seguidas, 1 único `X-Gerado-Em`** (199 acertos). Mediana
  local de 10 requisições: 4,1 ms sem cache (variação nova de `dias`) contra 1,6 ms com
  cache. O banco local é pequeno; a diferença cresce com o volume do GROUP BY.
- Hub: `negotiate` sem token → 401; com token → 200.
- Página: carrega sem erros no console, `signalR.VERSION` = 10.0.11; senha errada mostra
  "Usuário ou senha inválidos.".

**Não verificado manualmente:** o login com senha correta e a reconexão pela página no
navegador. A senha de teste fica em User Secrets e não foi lida nesta verificação
automatizada. O fluxo do hub (entrar no grupo, receber o evento, reconectar e ser
revogado) está coberto pelos testes automatizados com o cliente .NET. Para conferir na
página: entre como `ana`, acompanhe um pedido de `cliente-001`, cancele-o por outra aba e
pare/reinicie a API para ver os estados "reconectando" e "conectado".

## Versões utilizadas

| Item | Versão |
|---|---|
| SignalR (servidor) | parte do ASP.NET Core 10.0.12 (runtime) |
| Microsoft.AspNetCore.SignalR.Client | 10.0.13 (só no projeto de testes) |
| @microsoft/signalr (navegador) | 10.0.11, via LibMan |
| microsoft.web.librarymanager.cli | 3.0.114 (ferramenta local) |
| SDK / runtime | .NET 10.0.401 / 10.0.12 |
| Testes | 170 aprovados (11 novos neste roteiro) |
