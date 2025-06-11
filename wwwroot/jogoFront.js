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
// Ajustar as URLs das imagens se mais imagens forem adicionadas para o nível Extremo
// Este array deve conter todas as URLs de imagens que serão usadas no jogo
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
    this.cartasViradasNoTurno = 0; // Adiciona contador de cartas viradas no turno atual

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
    this.cartasViradasNoTurno = 0; // Reinicia o contador no início do jogo
    this.desenhar();
    this.atualizarPoderesUI();

    if (this.modo === 'Coop') {
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

      // Impede virar cartas já visíveis, encontradas ou congeladas
      if (x < 0 || x >= COLUNAS || y < 0 || y >= LINHAS || this.estado.cartas[pos].visivel || this.estado.cartas[pos].encontrada || this.estado.cartasCongeladas[pos]) {
        if (this.estado.cartasCongeladas[pos]) {
          alert('Esta carta está congelada e não pode ser virada nesta rodada.');
        }
        return;
      }

      this.travado = true; // Trava a interface enquanto a requisição é processada

      // Envia a requisição para abrir a carta
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
        this.travado = false; // Desbloqueia se houve erro na abertura
        return;
      }
      
      this.estado = await openResp.json(); // Atualiza o estado do jogo com a carta virada
      this.desenhar(); // Redesenha o tabuleiro
      this.atualizarPoderesUI(); // Atualiza a UI dos poderes

      // Incrementa o contador de cartas viradas no turno
      this.cartasViradasNoTurno++; 
      
      // Define a quantidade de cartas que devem ser viradas no turno
      let cartasNecessariasPorTurno = 0;
      if (this.nivel === 'Extremo') {
          cartasNecessariasPorTurno = 4; // No nível Extremo, sempre 4 cartas por turno
      } else {
          cartasNecessariasPorTurno = this.nivel === 'Facil' ? 2 : (this.nivel === 'Medio' ? 3 : 4);
      }

      // Se o jogador ainda não virou todas as cartas necessárias para o turno, desbloqueia e espera mais cliques
      if (this.cartasViradasNoTurno < cartasNecessariasPorTurno) {
        this.travado = false; 
        return; // Permite cliques adicionais para virar as 4 cartas
      }

      // Se todas as cartas foram viradas para o turno, aguarda e processa a jogada
      await new Promise(r => setTimeout(r, 2000)); // Espera 2 segundos para o jogador ver as cartas

      const verifyResp = await fetch(`${API_BASE}/jogada/verificar`, { method: 'POST' });

      if (!verifyResp.ok) {
        alert('Erro ao verificar jogada. Tente novamente.');
        return; // Sai da função se houver erro
      }

      this.estado = await verifyResp.json(); // Atualiza o estado com o resultado da verificação
      this.desenhar(); // Redesenha o tabuleiro (cartas acertadas permanecem viradas, erradas viram para baixo)
      this.atualizarPoderesUI(); // Atualiza a UI dos poderes

      // Verifica se o jogo terminou
      if (this.estado.finalizado){
        if (this.estado.todasCartasEncontradas && !this.estado.tempoEsgotado){
          alert('Parabéns, equipe! Vocês revelaram todas as cartas a tempo!');
        } else if (this.estado.tempoEsgotado){
          alert('O tempo esgotou! A equipe não conseguiu revelar todas as cartas. Tente novamente!');
        } else {
          alert('Partida encerrada!');
        }
        return; // Sai da função se o jogo terminou
      }  

      this.turno = 'ia'; // Passa o turno para a IA
      this.cartasViradasNoTurno = 0; // Reinicia o contador para o próximo turno do humano
      await this.jogadaIA(); // Inicia o turno da IA
      
    } catch (error) { // Captura erros inesperados
      console.error("Erro na jogada humana:", error);
      alert('Ocorreu um erro no jogo. Por favor, reinicie.');
    } finally {
      // Garante que a interface seja desbloqueada se o jogo não terminou e o turno é do humano
      if (!this.estado.finalizado && this.turno === 'humano') {
        this.travado = false;
      }
    }
  }

  async jogadaIA() {
    try { 
      await new Promise(r => setTimeout(r, 1000)); // Pequena pausa antes da IA jogar
      this.travado = true; // Trava a interface durante o turno da IA

      // Requisição para a IA abrir cartas
      const abrir = await fetch(`${API_BASE}/ia/abrir`, { method: 'POST' });

      if (!abrir.ok) {
        alert('Erro na jogada da IA (abrir).');
        return;
      }

      this.estado = await abrir.json(); // Atualiza o estado com as cartas viradas pela IA
      this.desenhar(); // Redesenha o tabuleiro
      this.atualizarPoderesUI(); // Atualiza a UI dos poderes

      await new Promise(r => setTimeout(r, 2000)); // Espera 2 segundos para o jogador ver as cartas da IA

      // Requisição para a IA resolver (verificar correspondências)
      const resolver = await fetch(`${API_BASE}/ia/resolver`, { method: 'POST' });

      if (!resolver.ok) {
        alert('Erro na jogada da IA (resolver).');
        return; 
      }

      this.estado = await resolver.json(); // Atualiza o estado com o resultado da jogada da IA
      this.desenhar(); // Redesenha o tabuleiro
      this.atualizarPoderesUI(); // Atualiza a UI dos poderes

      // Verifica se o jogo terminou após a jogada da IA
      if (this.estado.finalizado) {
        if (this.estado.todasCartasEncontradas && !this.estado.tempoEsgotado){
          alert('Parabéns, equipe! Vocês revelaram todas as cartas a tempo!');
        } else if (this.estado.tempoEsgotado){
          alert('O tempo esgotou! A equipe não conseguiu revelar todas as cartas. Tente novamente!');
        } else {
          alert('Partida encerrada!');
        }
        return;
      }
      this.turno = 'humano'; // Passa o turno de volta para o humano

    } catch (error) { // Captura erros inesperados
      console.error("Erro no turno da IA:", error);
      alert('Ocorreu um erro no turno da IA.');
    } finally {
      // Garante que a interface seja desbloqueada se o jogo não terminou
      if (!this.estado.finalizado) {
        this.travado = false;
      }
    }
  }

  async usarPoder(poder, pos = -1) { 
    if (!jogoUI || this.turno !== 'humano' || this.travado) return; // Não permite usar poderes se não for o turno do humano ou estiver travado

    this.travado = true; // Trava a interface ao usar um poder

    try {
      let resp;
      if (poder === 'embaralhar') {
        resp = await fetch(`${API_BASE}/poder/embaralhar`, { method: 'POST' });
      } else if (poder === 'congelar') {
        if (pos === -1) { // Verifica se a posição foi fornecida para congelar
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
        return; // Se o poder não é reconhecido, sai
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
        this.estado = await resp.json(); // Atualiza o estado após usar o poder
        this.desenhar(); // Redesenha o tabuleiro
        this.atualizarPoderesUI(); // Atualiza a UI dos poderes
        if (poder === 'dica') {
          alert(`Dica usada! Restam ${this.estado.dicasRestantes} dicas.` +
            (this.estado.cooldownDicaSec ? ` Aguarde ${this.estado.cooldownDicaSec}s para a próxima.` : ''));
        } else if (poder === 'congelar') { 
          alert(`Carta na posição ${pos} congelada!`);
        }
      }
    } catch (error) { // Captura erros inesperados
      console.error(`Erro ao usar ${poder}:`, error);
      alert(`Ocorreu um erro ao usar ${poder}.`);
    } finally {
      this.travado = false; // Garante que a interface seja desbloqueada
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
    ctx.clearRect(0, 0, canvas.width, canvas.height); // Limpa o canvas
    ctx.font = '16px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';

    this.estado.cartas.forEach((c, i) => {
      const cx = (i % COLUNAS) * TAM;
      const cy = Math.floor(i / COLUNAS) * TAM;
      ctx.strokeStyle = '#333';
      ctx.strokeRect(cx, cy, TAM, TAM); // Desenha a borda da carta
      if (this.estado.cartasCongeladas[i]) {
        ctx.fillStyle = 'rgba(0, 0, 255, 0.3)'; // Desenha um overlay azul para cartas congeladas
        ctx.fillRect(cx, cy, TAM, TAM);
      }
      // Condição para desenhar a imagem da carta: se ela está visível OU já foi encontrada
      if (!c.visivel && !c.encontrada) return; // Se não está visível e não foi encontrada, não desenha a imagem (mantém o verso)
      
      const img = imagens[c.valor];
      if (img) ctx.drawImage(img, cx, cy, TAM, TAM); // Desenha a imagem da carta
      else {
        ctx.fillStyle = '#000'; // Fallback para texto se a imagem não carregar
        ctx.fillText(c.valor, cx + TAM/2, cy + TAM/2);
      }
    });
  }
}

// Quando a janela carrega, pré-carrega as imagens e configura o botão Iniciar
window.onload = async () => {
  await preloadImagens(urls); // Espera todas as imagens serem carregadas
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

// Função para carregar e exibir o Top 5 do ranking
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

// Configura o modal do Top 5
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