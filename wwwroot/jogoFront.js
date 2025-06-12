const API_BASE = 'http://localhost:5091/api/jogo';
const canvas = document.getElementById('gameCanvas');
const ctx = canvas.getContext('2d');

const COLUNAS = 8, LINHAS = 6, TAM = 80;
canvas.width = COLUNAS * TAM;
canvas.height = LINHAS * TAM;

let jogoUI;

const imagens = {};
function preloadImagens(urls) {
  return Promise.all(
    urls.map(u => new Promise(res => {
      const img = new Image();
      img.src = u;
      img.onload = () => { imagens[u] = img; res(); };
      img.onerror = () => { res(); };
    }))
  );
}
const urls = Array.from({ length: 24 }, (_, i) => `img/c${i+1}.png`);

class InterfaceJogo {
  constructor(modo, nivel, nome) {
    this.modo = modo;
    this.nivel = nivel;
    this.nome = nome;
    this.estado = null;
    this.turno = 'humano';
    this.travado = false;
    this.modoCongelamentoAtivo = false;
    this.intervaloTempo = null;
    this.cartasViradasNoTurno = 0;

    canvas.addEventListener('click', e => this.lidarComClique(e));
    this.pontuacaoDisplay = document.getElementById('pontuacaoAtual');
    this.tempoRestanteDisplay = document.getElementById('tempoRestante');

    this.especiaisRestantesDisplay = document.getElementById('especiaisRestantes');
    this.dicasRestantesDisplay = document.getElementById('dicasRestantes');
    this.dicaCooldownDisplay = document.getElementById('dicaCooldown');
    this.cooldownSecDisplay = document.getElementById('cooldownSec');

    document.getElementById('btnEmbaralhar').addEventListener('click', () => this.usarPoder('embaralhar'));
    document.getElementById('btnCongelar').addEventListener('click', () => this.ativarModoCongelamento());
    document.getElementById('btnDica').addEventListener('click', () => this.usarPoder('dica'));
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
    this.travado = false;
    this.modoCongelamentoAtivo = false;
    this.cartasViradasNoTurno = 0;
    this.desenhar();
    this.atualizarPoderesUI();

    if (this.modo === 'Coop') {
      this.intervaloTempo = setInterval(() => this.sicronizarEstadoLoop(), 1000);
      if (this.intervaloTempo) {
        clearInterval(this.intervaloTempo);
      }
      this.atualizarTempoUI();
      this.intervaloTempo = setInterval(() => this.atualizarTempoUI(), 1000);
    } else {
      if (this.intervaloTempo) {
        clearInterval(this.intervaloTempo);
        this.intervaloTempo = null;
      }
      this.tempoRestanteDisplay.innerText = '';
    }
  }

  atualizarTempoUI() {
    if (this.estado && this.modo === 'Coop') {
      const segundos = this.estado.tempoRestanteCoop;
      const minutos = Math.floor(segundos / 60);
      const segundosFormatados = (segundos % 60).toString().padStart(2, '0');
      this.tempoRestanteDisplay.innerText = `Tempo: ${minutos}:${segundosFormatados}`;

      if (this.estado.finalizado) {
        if (this.intervaloTempo) {
          clearInterval(this.intervaloTempo);
          this.intervaloTempo = null;
        }
        if (!this.estado.tempoEsgotado && this.estado.todasCartasEncontradas) {
          alert('Parabéns, equipe! Vocês revelaram todas as cartas a tempo!');
        } else if (this.estado.tempoEsgotado) {
          alert('O tempo esgotou! A equipe não conseguiu revelar todas as cartas. Tente novamente!');
        }
      }
    }
  }

  async lidarComClique(evt) {
    if (this.modoCongelamentoAtivo) {
      this.processarCliqueCongelamento(evt);
    } else {
      this.fazerJogada(evt);
    }
  }

  ativarModoCongelamento() {
    if (this.travado || this.estado.especiaisRestantes <= 0) {
      alert("Não é possível congelar agora ou você não tem especiais restantes.");
      return;
    }
    this.modoCongelamentoAtivo = true;
    alert("Modo de congelamento ativado. Clique na carta que deseja congelar.");
  }

  async processarCliqueCongelamento(evt) {
    if (this.travado) return;

    const x = Math.floor(evt.offsetX / TAM);
    const y = Math.floor(evt.offsetY / TAM);
    const pos = y * COLUNAS + x;

    if (x < 0 || x >= COLUNAS || y < 0 || y >= LINHAS || this.estado.cartas[pos].encontrada) {
      alert("Não é possível congelar uma posição inválida ou uma carta já encontrada.");
      this.modoCongelamentoAtivo = false;
      return;
    }

    await this.usarPoder('congelar', pos);
    this.modoCongelamentoAtivo = false;
  }

  async fazerJogada(evt) {
    try {
      if (this.turno !== 'humano' || this.estado.finalizado || this.travado) return;

      const x = Math.floor(evt.offsetX / TAM);
      const y = Math.floor(evt.offsetY / TAM);
      const pos = y * COLUNAS + x;

      if (x < 0 || x >= COLUNAS || y < 0 || y >= LINHAS || this.estado.cartas[pos].visivel || this.estado.cartas[pos].encontrada || this.estado.cartasCongeladas[pos]) {
        if (this.estado.cartasCongeladas[pos]) {
          alert('Esta carta está congelada e não pode ser virada nesta rodada.');
        }
        return;
      }

      this.travado = true;

      const openResp = await fetch(`${API_BASE}/jogada/abrir`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ posicao: pos })
      });

      if (!openResp.ok) {
        const errorText = await openResp.text();
        try {
          const { erro } = JSON.parse(errorText);
          if (erro.includes("carta congelada")) {
            alert('Não é possível virar esta carta, ela está congelada.');
          } else {
            alert(`Erro ao virar carta: ${erro}. Tente novamente.`);
          }
        } catch {
          alert('Erro ao virar carta. Tente novamente.');
        }
        this.travado = false;
        return;
      }

      this.estado = await openResp.json();
      this.desenhar();
      this.atualizarPoderesUI();

      this.cartasViradasNoTurno++;

      let cartasNecessariasPorTurno = 0;
      if (this.nivel === 'Extremo') {
          cartasNecessariasPorTurno = 4;
      } else {
          cartasNecessariasPorTurno = this.nivel === 'Facil' ? 2 : (this.nivel === 'Medio' ? 3 : 4);
      }

      if (this.cartasViradasNoTurno < cartasNecessariasPorTurno) {
        this.travado = false;
        return;
      }

      await new Promise(r => setTimeout(r, 2000));

      const verifyResp = await fetch(`${API_BASE}/jogada/verificar`, { method: 'POST' });

      if (!verifyResp.ok) {
        alert('Erro ao verificar jogada. Tente novamente.');
        return;
      }

      this.estado = await verifyResp.json();
      this.desenhar();
      this.atualizarPoderesUI();

      if (this.estado.finalizado){
        if (this.estado.todasCartasEncontradas && !this.estado.tempoEsgotado){
          alert('Parabéns, equipe! Vocês revelaram todas as cartas a tempo!');
        } else if (this.estado.tempoEsgotado){
          alert('O tempo esgotou! A equipe não conseguiu revelar todas as cartas. Tente novamente!');
        } else {
          alert('Partida encerrada!');
        }
        return;
      }

      if (this.turno === 'humano') {
            this.cartasViradasNoTurno = 0; 
            this.travado = true;

      if (this.modo === 'PvAI') {
          this.turno = 'ia';
          await this.jogadaIA();
      } else if (this.modo === 'Coop') { 
                this.turno = 'ia'; 
                await this.jogadaIA(); 
        } else {
          this.travado = false;
          this.turno = 'humano';
      }

    }
 } catch (error) {
      console.error("Erro na jogada humana:", error);
      alert('Ocorreu um erro no jogo. Por favor, reinicie.');
    } 
}
  async jogadaIA() {
    try {
      await new Promise(r => setTimeout(r, 1000));
      this.travado = true;

      const abrir = await fetch(`${API_BASE}/ia/abrir`, { method: 'POST' });

      if (!abrir.ok) {
        alert('Erro na jogada da IA (abrir).');
        this.travado = false;
        return;
      }

      this.estado = await abrir.json();
      this.desenhar();
      this.atualizarPoderesUI();

      await new Promise(r => setTimeout(r, 2000));

      const resolver = await fetch(`${API_BASE}/ia/resolver`, { method: 'POST' });

      if (!resolver.ok) {
        alert('Erro na jogada da IA (resolver).');
        this.travado = false;
        return;
      }

      this.estado = await resolver.json();
      this.desenhar();
      this.atualizarPoderesUI();

      if (this.estado.finalizado) {
        if (this.estado.todasCartasEncontradas && !this.estado.tempoEsgotado){
          alert('Parabéns, equipe! Vocês revelaram todas as cartas a tempo!');
        } else if (this.estado.tempoEsgotado){
          alert('O tempo esgotou! A equipe não conseguiu revelar todas as cartas. Tente novamente!');
        } else {
          alert('Partida encerrada!');
        }
        this.travado = false;
        return;
      }
      this.turno = 'humano';
      this.travado = false;

    } catch (error) {
      console.error("Erro no turno da IA:", error);
      alert('Ocorreu um erro no turno da IA.');
      this.travado = false;
    }
  }

  async usarPoder(poder, pos = -1) {
    if (!jogoUI || this.turno !== 'humano' || this.travado) return;

    this.travado = true;

    try {
      let resp;
      if (poder === 'embaralhar') {
        resp = await fetch(`${API_BASE}/poder/embaralhar`, { method: 'POST' });
      } else if (poder === 'congelar') {
        if (pos === -1) {
          alert("Erro: Posição para congelar não fornecida.");
          return;
        }
        resp = await fetch(`${API_BASE}/poder/congelar`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ posicao: pos })
        });
      } else if (poder === 'dica') {
        resp = await fetch(`${API_BASE}/poder/dica`, { method: 'POST' });
      } else {
        return;
      }

      if (!resp.ok) {
        const errorText = await resp.text();
        try {
          const { erro } = JSON.parse(errorText);
          alert(`Não foi possível usar ${poder}: ${erro}`);
        } catch {
          alert(`Não foi possível usar ${poder}: Erro desconhecido ou resposta inválida do servidor.`);
        }
      } else {
        this.estado = await resp.json();
        this.desenhar();
        this.atualizarPoderesUI();
        if (poder === 'dica') {
          alert(`Dica usada! Restam ${this.estado.dicasRestantes} dicas.` +
            (this.estado.cooldownDicaSec ? ` Aguarde ${this.estado.cooldownDicaSec}s para a próxima.` : ''));
        } else if (poder === 'congelar') {
          alert(`Carta na posição ${pos} congelada!`);
        }
      }
    } catch (error) {
      console.error(`Erro ao usar ${poder}:`, error);
      alert(`Ocorreu um erro ao usar ${poder}.`);
    } finally {
      this.travado = false;
    }
  }

  
async sicronizarEstadoLoop() {
    // Se o jogo acabou, para de sincronizar
    if (!this.estado || this.estado.finalizado) {
        if(this.intervaloTempo) {
            clearInterval(this.intervaloTempo);
            this.intervaloTempo = null;
        }
        return;
    }

    try {
        // Busca o estado mais recente do jogo no servidor
        const resp = await fetch(`${API_BASE}/estado`);
        if(resp.ok) {
            this.estado = await resp.json();
            // Atualiza a interface com os novos dados
            this.atualizarTempoUI();
            this.atualizarPoderesUI();
            this.desenhar();
        }
    } catch (error) {
        console.error("Erro ao sincronizar estado:", error);
        if(this.intervaloTempo) {
            clearInterval(this.intervaloTempo);
            this.intervaloTempo = null;
        }
    }
  }

  atualizarPoderesUI() {
    if (this.estado) {
      this.especiaisRestantesDisplay.innerText = this.estado.especiaisRestantes;
      this.dicasRestantesDisplay.innerText = this.estado.dicasRestantes;

      if (this.estado.cooldownDicaSec > 0) {
        this.dicaCooldownDisplay.style.display = 'block';
        this.cooldownSecDisplay.innerText = this.estado.cooldownDicaSec;
      } else {
        this.dicaCooldownDisplay.style.display = 'none';
      }

      if (this.pontuacaoDisplay && this.estado) {
        const prefixo = this.modo === 'Coop' ? 'Pontuação da Equipe' : 'Pontuação';
        this.pontuacaoDisplay.innerText = `${prefixo}: ${this.estado.pontuacao}`;
      }

      if (this.modo === 'Coop') {
        this.atualizarTempoUI();
      }
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
      if (this.estado.cartasCongeladas[i]) {
        ctx.fillStyle = 'rgba(0, 0, 255, 0.3)';
        ctx.fillRect(cx, cy, TAM, TAM);
      }
      if (!c.visivel && !c.encontrada) return;

      const img = imagens[c.valor];
      if (img) ctx.drawImage(img, cx, cy, TAM, TAM);
      else {
        ctx.fillStyle = '#000';
        ctx.fillText(c.valor, cx + TAM/2, cy + TAM/2);
      }
    });
  }
}

window.onload = async () => {
  await preloadImagens(urls);
  document.getElementById('btnIniciar').addEventListener('click', () => {
    const nome = document.getElementById('inputNome').value.trim();
    const modo = document.getElementById('selModo').value;
    const nivel = document.getElementById('selNivel').value;

    if (!nome) {
      alert("Digite o nome do jogador antes de iniciar.");
      return;
    }

    jogoUI = new InterfaceJogo(modo, nivel, nome);
    jogoUI.iniciar();
  });
};

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
    carregarTop5();
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

