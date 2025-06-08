const API_BASE = 'http://localhost:5091/api/jogo';
const canvas   = document.getElementById('gameCanvas');
const ctx      = canvas.getContext('2d');

const COLUNAS = 6, LINHAS = 4, TAM = 80;
canvas.width  = COLUNAS * TAM;
canvas.height = LINHAS  * TAM;

let jogoUI;

const imagens = {};
function preloadImagens(urls) {
  return Promise.all(
    urls.map(u => new Promise(res => {
      const img = new Image();
      img.src = u;
      img.onload = () => { imagens[u] = img; res(); };
      img.onerror = () => { console.warn(`Falha ao carregar ${u}`); res(); };
    }))
  );
}
const urls = Array.from({ length: 12 }, (_, i) => `img/c${i+1}.png`);

class InterfaceJogo {
  constructor(modo, nivel, nome) {
    this.modo   = modo;
    this.nivel  = nivel;
    this.nome   = nome;
    this.estado = null;
    this.turno  = 'humano';
    this.abertas = [];
    this.travado = false;
    canvas.addEventListener('click', e => this.fazerJogada(e));
    this.pontuacaoDisplay = document.getElementById('pontuacaoAtual');
  }

  async iniciar() {
    
    const params = new URLSearchParams({
      nome: this.nome,
      modo: this.modo,
      nivel: this.nivel,
      tamanho: COLUNAS * LINHAS
    });
    const resp = await fetch(`${API_BASE}/iniciar?${params.toString()}`);
    
    this.estado = await resp.json();
    this.turno = 'humano';
    this.abertas = [];
    this.travado = false;
    this.desenhar();
  }

async fazerJogada(evt) {
  if (this.turno !== 'humano' || this.estado.finalizado || this.travado) return;

  const x = Math.floor(evt.offsetX / TAM);
  const y = Math.floor(evt.offsetY / TAM);
  if (x < 0 || x >= COLUNAS || y < 0 || y >= LINHAS) return;

  const pos = y * COLUNAS + x;
  if (this.estado.cartas[pos].visivel) return;

  const resp = await fetch(`${API_BASE}/jogada`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ posicao: pos })
  });
  this.estado = await resp.json();
  this.desenhar(); // Renderiza a carta que acabou de ser virada 

  await new Promise(r => setTimeout(r, 50));
  
  this.abertas.push(pos);
  const req = this.nivel === 'Facil' ? 2 : 3;

  // Se o número de cartas abertas ainda não é suficiente para uma verificação, apenas retorna
  if (this.abertas.length < req) {
      return;
  }

  this.travado = true;

  // Aguarda 2s para o jogador ver as cartas
  await new Promise(r => setTimeout(r, 2000));

  // Consulta o estado real do backend após o tempo de exibição
  const atualiza = await fetch(`${API_BASE}/estado`);
  this.estado = await atualiza.json();
  this.abertas = [];
  this.desenhar();

  if (this.estado.finalizado){
    alert('Partida encerrada!');
    return
  }  

  this.turno = 'ia';
  await this.jogadaIA();
  this.travado = false;
}


  async jogadaIA() {
    await new Promise(r => setTimeout(r, 1000));

    // Parte 1: IA abre cartas
    const abrir = await fetch(`${API_BASE}/ia/abrir`, { method: 'POST' });
    this.estado = await abrir.json();
    this.desenhar();

    // Aguarda visualização
    await new Promise(r => setTimeout(r, 2000));

    // Parte 2: IA resolve
    const resolver = await fetch(`${API_BASE}/ia/resolver`, { method: 'POST' });
    this.estado = await resolver.json();
    this.desenhar();

    if (!this.estado.finalizado) {
      this.turno = 'humano';
      this.travado = false;
    } else {
      alert('Partida encerrada!');
    }
  }

  desenhar() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.font = '16px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';

    this.estado.cartas.forEach((c, i) => {
      const cx = (i % COLUNAS) * TAM;
      const cy = Math.floor(i / COLUNAS) * TAM;
      ctx.strokeStyle = '#333';
      ctx.strokeRect(cx, cy, TAM, TAM);
      if (!c.visivel) return;
      const img = imagens[c.valor];
      if (img) ctx.drawImage(img, cx, cy, TAM, TAM);
      else {
        ctx.fillStyle = '#000';
        ctx.fillText(c.valor, cx + TAM/2, cy + TAM/2);
      }
    });

    if (this.pontuacaoDisplay && this.estado) {
      this.pontuacaoDisplay.innerText = `Pontuação: ${this.estado.pontuacao}`;
    }
  }
}

window.onload = async () => {
  await preloadImagens(urls);
  document.getElementById('btnIniciar').addEventListener('click', () => {
    const nome  = document.getElementById('inputNome').value.trim();
    const modo  = document.getElementById('selModo').value;
    const nivel = document.getElementById('selNivel').value;

    if (!nome) {
        alert("Digite o nome do jogador antes de iniciar.");
        return;
    }

    jogoUI = new InterfaceJogo(modo, nivel, nome);
    jogoUI.iniciar();
  });
};

window.addEventListener('keydown', async e => {
  if (!jogoUI) return;

  if (e.key === 'E') {
    const resp = await fetch(`${API_BASE}/poder/embaralhar`, { method: 'POST' });
    jogoUI.estado = await resp.json();
    jogoUI.desenhar();
  }

  if (e.key === 'C') {
    const pos = parseInt(prompt('Qual posição (0–23) deseja congelar?'), 10);
    const resp = await fetch(`${API_BASE}/poder/congelar`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ posicao: pos })
    });
    jogoUI.estado = await resp.json();
    jogoUI.desenhar();
  }

  if (e.key === 'D') {
    const resp = await fetch(`${API_BASE}/poder/dica`, { method: 'POST' });
    if (!resp.ok) {
      const { erro } = await resp.json();
      alert("Não foi possível usar dica: " + erro);
    } else {
      jogoUI.estado = await resp.json();
      jogoUI.desenhar();
      alert(`Dica usada! Restam ${jogoUI.estado.dicasRestantes} dicas.` +
        (jogoUI.estado.cooldownDicaSec
          ? ` Aguarde ${jogoUI.estado.cooldownDicaSec}s.` : ''));
    }
  }
});

function carregarTop5() {
    fetch('/api/ranking/top5')
        .then(response => {
            if (!response.ok) {
                throw new Error('Erro ao buscar ranking');
            }
            return response.json();
        })
        .then(ranking => {
            const container = document.getElementById('top5-container');
            if (!container) return;

            container.innerHTML = `
                <h2>Top 5 Jogadores</h2>
                <table border="1" cellpadding="5">
                    <thead>
                        <tr>
                            <th>Nome</th>
                            <th>Modo</th>
                            <th>Nível</th>
                            <th>Pontuação</th>
                            <th>Data/Hora</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${ranking.map(j => `
                            <tr>
                                <td>${j.nome}</td>
                                <td>${j.modo}</td>
                                <td>${j.nivel}</td>
                                <td>${j.pontuacao}</td>
                                <td>${j.dataHora}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            `;
        })
        .catch(error => {
            console.error('Erro ao carregar ranking:', error);
        });
}

document.addEventListener("DOMContentLoaded", () => {
    const btnTop5 = document.getElementById("btn-top5");
    const modal = document.getElementById("modal-top5");
    const fecharModal = document.getElementById("fechar-modal");

    btnTop5.onclick = () => {
        modal.style.display = "block";
        carregarTop5(); // busca o ranking ao abrir o modal
    };

    fecharModal.onclick = () => {
        modal.style.display = "none";
    };

    window.onclick = (event) => {
        if (event.target == modal) {
            modal.style.display = "none";
        }
    };
});

function carregarTop5() {
    fetch('/api/ranking/top5')
        .then(response => {
            if (!response.ok) throw new Error('Erro ao buscar ranking');
            return response.json();
        })
        .then(ranking => {
            const container = document.getElementById('top5-container');
            container.innerHTML = `
                <h2>Top 5 Jogadores</h2>
                <table>
                    <thead>
                        <tr>
                            <th>Nome</th>
                            <th>Modo</th>
                            <th>Nível</th>
                            <th>Pontuação</th>
                            <th>Data/Hora</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${ranking.map(j => `
                            <tr>
                                <td>${j.nome}</td>
                                <td>${j.modo}</td>
                                <td>${j.nivel}</td>
                                <td>${j.pontuacao}</td>
                                <td>${j.dataHora}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            `;
        })
        .catch(error => {
            const container = document.getElementById('top5-container');
            container.innerHTML = `<p>Erro ao carregar ranking.</p>`;
            console.error(error);
        });
}


