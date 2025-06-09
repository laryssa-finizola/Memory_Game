const API_BASE = 'http://localhost:5091/api/jogo';
const canvas   = document.getElementById('gameCanvas');
const ctx      = canvas.getContext('2d');

const COLUNAS = 8, LINHAS = 6, TAM = 80;
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
const urls = Array.from({ length: 24 }, (_, i) => `img/c${i+1}.png`);

class InterfaceJogo {
  constructor(modo, nivel, nome) {
    this.modo   = modo;
    this.nivel  = nivel;
    this.nome   = nome;
    this.estado = null;
    this.turno  = 'humano';
    this.abertas = []; // Este array agora é usado para rastrear as cartas abertas no CLIENTE para controle de fluxo
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
  try {
    console.log('--- Início fazerJogada ---');
    if (this.turno !== 'humano' || this.estado.finalizado || this.travado) {
      console.log('Condição de saída inicial em fazerJogada:', {
        turno: this.turno,
        finalizado: this.estado.finalizado,
        travado: this.travado
      });
      return;
    }

    const x = Math.floor(evt.offsetX / TAM);
    const y = Math.floor(evt.offsetY / TAM);
    if (x < 0 || x >= COLUNAS || y < 0 || y >= LINHAS) {
      console.log('Clique fora do canvas.');
      return;
    }

    const pos = y * COLUNAS + x;
    if (this.estado.cartas[pos].visivel || this.estado.cartas[pos].encontrada) {
      console.log(`Carta na posição ${pos} já visível ou encontrada.`);
      return;
    }

    this.travado = true; // Bloqueia cliques adicionais enquanto a jogada é processada
    console.log('Interface travada. Carta clicada na posição:', pos);

    // 1. Envia a solicitação para o backend APENAS virar a carta.
    console.log('Chamando API: jogada/abrir para posição', pos);
    const openResp = await fetch(`${API_BASE}/jogada/abrir`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ posicao: pos })
    });

    if (!openResp.ok) {
        const errorText = await openResp.text();
        console.error(`Erro na API jogada/abrir: ${openResp.status} - ${errorText}`);
        alert('Erro ao virar carta. Tente novamente.');
        return;
    }
    
    this.estado = await openResp.json();
    this.desenhar();
    console.log('Carta virada na UI. Estado atual:', this.estado.pontuacao);

    // ALTERADO AQUI: "Dificil" para "Medio"
    const req = this.nivel === 'Facil' ? 2 : (this.nivel === 'Medio' ? 3 : 2); // Adicionado 'Medio'

    const currentlyVisibleOnFrontend = this.estado.cartas.filter(c => c.visivel && !c.encontrada).length;

    console.log(`Cartas visíveis no frontend: ${currentlyVisibleOnFrontend}, Requisito: ${req}`);

    if (currentlyVisibleOnFrontend < req) {
        console.log('Ainda não há cartas suficientes para verificação. Aguardando próximo clique.');
        return; 
    }

    console.log(`Número de cartas (${req}) atingido. Aguardando 2s para visualização...`);
    await new Promise(r => setTimeout(r, 2000));
    console.log('Tempo de visualização de 2s concluído.');

    // 2. Após o atraso de visualização, solicita ao backend para VERIFICAR a jogada.
    console.log('Chamando API: jogada/verificar para processar a jogada.');
    const verifyResp = await fetch(`${API_BASE}/jogada/verificar`, { method: 'POST' });

    if (!verifyResp.ok) {
        const errorText = await verifyResp.text();
        console.error(`Erro na API jogada/verificar: ${verifyResp.status} - ${errorText}`);
        alert('Erro ao verificar jogada. Tente novamente.');
        return; 
    }

    this.estado = await verifyResp.json();
    this.desenhar();
    console.log('Jogada verificada na UI. Pontuação atual:', this.estado.pontuacao);


    if (this.estado.finalizado){
      alert('Partida encerrada!');
      console.log('JOGO FINALIZADO APÓS JOGADA HUMANA. IA NÃO JOGARÁ.');
      return; 
    }  

    console.log('Turno Humano Concluído. Mudando para IA.');
    this.turno = 'ia';
    await this.jogadaIA();
    
  } catch (error) {
    console.error('Erro inesperado em fazerJogada:', error);
    alert('Ocorreu um erro no jogo. Por favor, reinicie.');
  } finally {
    if (!this.estado.finalizado) { 
      this.travado = false;
      console.log('Interface destravada no finally de fazerJogada. Turno atual:', this.turno);
    } else {
        console.log('JOGO FINALIZADO NO FINALLY DE fazerJogada. Interface permanece travada.');
    }
    console.log('--- Fim fazerJogada ---');
  }
}

async jogadaIA() {
  try { 
    console.log('--- Iniciando jogada da IA ---');
    await new Promise(r => setTimeout(r, 1000));

    // Parte 1: IA abre cartas
    console.log('Chamando API: ia/abrir');
    const abrir = await fetch(`${API_BASE}/ia/abrir`, { method: 'POST' });

    if (!abrir.ok) {
        const errorText = await abrir.text();
        console.error(`Erro na API ia/abrir: ${abrir.status} - ${errorText}`);
        alert('Erro na jogada da IA (abrir).');
        return;
    }

    this.estado = await abrir.json();
    this.desenhar();
    console.log('IA abriu cartas. Estado atual:', this.estado.pontuacao);

    // Aguarda visualização
    console.log('IA aguardando 2s para visualização...');
    await new Promise(r => setTimeout(r, 2000));
    console.log('IA concluiu visualização.');

    // Parte 2: IA resolve
    console.log('Chamando API: ia/resolver');
    const resolver = await fetch(`${API_BASE}/ia/resolver`, { method: 'POST' });

    if (!resolver.ok) {
        const errorText = await resolver.text();
        console.error(`Erro na API ia/resolver: ${resolver.status} - ${errorText}`);
        alert('Erro na jogada da IA (resolver).');
        return;
    }

    this.estado = await resolver.json();
    this.desenhar();
    console.log('IA resolveu a jogada. Estado atual:', this.estado.pontuacao);

    if (this.estado.finalizado) {
      alert('Partida encerrada!');
      console.log('JOGO FINALIZADO APÓS JOGADA DA IA.');
      return;
    }
    this.turno = 'humano';
    console.log('Jogada da IA concluída. Mudando para Humano.');

  } catch (error) {
    console.error('Erro inesperado em jogadaIA:', error);
    alert('Ocorreu um erro no turno da IA.');
  } finally {
    if (!this.estado.finalizado) {
      this.travado = false;
      console.log('Interface destravada no finally de jogadaIA. Turno atual:', this.turno);
    } else {
        console.log('JOGO FINALIZADO NO FINALLY DA jogadaIA. Interface permanece travada.');
    }
    console.log('--- Fim jogadaIA ---');
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