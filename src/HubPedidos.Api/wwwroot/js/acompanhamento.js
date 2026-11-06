// Cliente de acompanhamento (Roteiro 7).
// Regras: o SignalR só AVISA que algo mudou; o estado confiável vem sempre da API HTTP
// (GET /api/v2/pedidos/{id}) com If-None-Match. Depois de cada reconexão o cliente
// reingressa no grupo (o servidor revalida a permissão) e reconcilia o estado, porque
// eventos emitidos durante a queda não são reenviados.
"use strict";

const elementos = Object.fromEntries(
  ["usuario", "senha", "entrar", "reconectar", "estado", "pedido", "acompanhar", "cancelar", "mensagem", "representacao", "eventos"]
    .map(id => [id, document.getElementById(id)]));

let token = null;
let conexao = null;
let pedidoAcompanhado = null;
let etagAtual = null;            // ETag da representação exibida
let ultimaSequencia = 0;         // descarta eventos repetidos ou atrasados
let consultaEmAndamento = null;  // no máximo UMA consulta por vez
let revalidarDepois = false;     // evento novo durante uma consulta: revalida uma vez ao final

function mostrarEstado(estado) {
  elementos.estado.textContent = estado;
  elementos.estado.className = estado;
  const ativa = estado === "conectado";
  elementos.acompanhar.disabled = !ativa;
  elementos.cancelar.disabled = !ativa || !pedidoAcompanhado;
  elementos.reconectar.hidden = estado !== "desconectado" || !token;
}

function registrarEvento(texto) {
  const item = document.createElement("li");
  item.textContent = `${new Date().toLocaleTimeString()} — ${texto}`;
  elementos.eventos.prepend(item);
}

async function entrar() {
  const resposta = await fetch("/api/autenticacao/token", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ usuario: elementos.usuario.value, senha: elementos.senha.value }),
  });
  if (!resposta.ok) {
    elementos.mensagem.textContent = "Usuário ou senha inválidos.";
    return;
  }
  token = (await resposta.json()).accessToken;
  await conectar();
}

async function conectar() {
  conexao = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/pedidos", { accessTokenFactory: () => token })
    // Tenta imediatamente e depois de 2, 10 e 30 s. Se tudo falhar, vai para "desconectado".
    .withAutomaticReconnect([0, 2000, 10000, 30000])
    .build();

  conexao.on("PedidoAtualizado", evento => {
    if (evento.pedidoId !== pedidoAcompanhado || evento.sequencia <= ultimaSequencia) {
      registrarEvento(`ignorado (repetido ou antigo): ${evento.situacao}`);
      return;
    }
    ultimaSequencia = evento.sequencia;
    registrarEvento(`PedidoAtualizado: ${evento.situacao}`);
    reconciliar(); // o evento só avisa; o estado vem da API
  });

  conexao.on("AcessoRevogado", motivo => {
    registrarEvento(`AcessoRevogado: ${motivo}`);
    elementos.mensagem.textContent = motivo;
    pedidoAcompanhado = null;
    conexao.stop();
  });

  conexao.onreconnecting(() => {
    mostrarEstado("reconectando");
    elementos.mensagem.textContent = "Conexão perdida. Reconectando...";
  });

  conexao.onreconnected(async () => {
    mostrarEstado("conectado");
    elementos.mensagem.textContent = "Reconectado.";
    if (pedidoAcompanhado) {
      // 1º reingressa no grupo (nova conexão, servidor revalida a permissão);
      // 2º consulta o estado atual: cobre o que mudou durante a queda.
      await acompanhar(pedidoAcompanhado);
    }
  });

  conexao.onclose(() => {
    mostrarEstado("desconectado");
    elementos.mensagem.textContent = "Desconectado. Use \"Conectar novamente\".";
  });

  try {
    await conexao.start();
    mostrarEstado("conectado");
    elementos.mensagem.textContent = "Conectado.";
  } catch (erro) {
    mostrarEstado("desconectado");
    elementos.mensagem.textContent = `Não foi possível conectar: ${erro}`;
  }
}

async function acompanhar(id) {
  try {
    await conexao.invoke("AcompanharPedido", id);
  } catch (erro) {
    elementos.mensagem.textContent = "Pedido não encontrado ou sem permissão.";
    pedidoAcompanhado = null;
    mostrarEstado(elementos.estado.textContent);
    return;
  }
  if (pedidoAcompanhado !== id) {
    etagAtual = null;
    ultimaSequencia = 0;
  }
  pedidoAcompanhado = id;
  mostrarEstado(elementos.estado.textContent);
  await reconciliar();
}

// Consulta condicional, sem consultas concorrentes: eventos que chegam durante uma
// consulta só marcam "revalidar depois", e uma única nova consulta é feita ao final.
async function reconciliar() {
  if (consultaEmAndamento) {
    revalidarDepois = true;
    return consultaEmAndamento;
  }
  consultaEmAndamento = (async () => {
    do {
      revalidarDepois = false;
      const cabecalhos = { Authorization: `Bearer ${token}` };
      if (etagAtual) cabecalhos["If-None-Match"] = etagAtual;
      const resposta = await fetch(`/api/v2/pedidos/${pedidoAcompanhado}`, { headers: cabecalhos });
      if (resposta.status === 304) {
        registrarEvento("GET condicional: 304 (representação local continua válida)");
      } else if (resposta.ok) {
        etagAtual = resposta.headers.get("ETag");
        elementos.representacao.textContent = JSON.stringify(await resposta.json(), null, 2);
        registrarEvento(`GET condicional: 200 (nova ETag ${etagAtual.slice(0, 20)}...)`);
      } else {
        elementos.mensagem.textContent = `Consulta falhou: HTTP ${resposta.status}`;
      }
    } while (revalidarDepois);
  })();
  try { await consultaEmAndamento; } finally { consultaEmAndamento = null; }
}

async function cancelar() {
  const resposta = await fetch(`/api/v2/pedidos/${pedidoAcompanhado}/cancelamento`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "If-Match": etagAtual ?? "*" },
  });
  elementos.mensagem.textContent = resposta.status === 412
    ? "O pedido mudou desde a sua leitura (412). Atualizado; confira antes de cancelar."
    : `Cancelamento: HTTP ${resposta.status}`;
  await reconciliar();
}

elementos.entrar.addEventListener("click", entrar);
elementos.reconectar.addEventListener("click", conectar);
elementos.acompanhar.addEventListener("click", () => acompanhar(elementos.pedido.value.trim()));
elementos.cancelar.addEventListener("click", cancelar);
mostrarEstado("desconectado");
