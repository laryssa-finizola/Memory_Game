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
    this.travado = false; // Controla se o canvas está travado para cliques
    canvas.addEventListener('click', e => this.fazerJogada(e));
    this.pontuacaoDisplay = document.getElementById('pontuacaoAtual');

    // Referências aos elementos da UI dos poderes
    this.especiaisRestantesDisplay = document.getElementById('especiaisRestantes');
    this.dicasRestantesDisplay     = document.getElementById('dicasRestantes');
    this.dicaCooldownDisplay       = document.getElementById('dicaCooldown');
    this.cooldownSecDisplay        = document.getElementById('cooldownSec');

    // Adicionar event listeners para os novos botões de poder
    document.getElementById('btnEmbaralhar').addEventListener('click', () => this.usarPoder('embaralhar'));
    document.getElementById('btnCongelar').addEventListener('click', () => this.usarPoder('congelar'));
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
    this.desenhar();
    this.atualizarPoderesUI(); // Adicionar chamada para atualizar a UI dos poderes ao iniciar
  }

async fazerJogada(evt) {
  try {
    console.log('--- Início fazerJogada ---');
    // Condição de saída:
    // Se não for turno do humano, ou jogo finalizado, ou JÁ ESTIVER TRAVADO (processando clique anterior).
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
    const pos = y * COLUNAS + x;

    // Verificações de clique inválido (fora do canvas ou carta já visível/encontrada)
    if (x < 0 || x >= COLUNAS || y < 0 || y >= LINHAS || this.estado.cartas[pos].visivel || this.estado.cartas[pos].encontrada) {
      console.log(`Clique inválido na posição ${pos}. Carta já visível/encontrada ou fora do canvas.`);
      return;
    }

    // *** PONTO CHAVE DA CORREÇÃO: Trava a interface aqui ***
    this.travado = true; 
    console.log('Interface travada. Carta clicada na posição:', pos);
    console.log(`Estado da carta ${pos} ANTES de abrir (frontend): visivel=${this.estado.cartas[pos].visivel}, encontrada=${this.estado.cartas[pos].encontrada}`);


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
        this.travado = false; // Destrava em caso de erro na abertura
        return;
    }
    
    this.estado = await openResp.json();
    this.desenhar(); // Redesenha para mostrar a carta virada
    this.atualizarPoderesUI(); 
    console.log('Carta virada na UI. Estado atual:', this.estado.pontuacao);
    // LOGS DE DEBUG ADICIONADOS
    console.log(`Estado da carta ${pos} DEPOIS de abrir (backend response): visivel=${this.estado.cartas[pos].visivel}, encontrada=${this.estado.cartas[pos].encontrada}`);
    const allVisible = this.estado.cartas.map((c, i) => c.visivel && !c.encontrada ? `pos ${i}: ${c.valor}` : null).filter(Boolean);
    console.log("Cartas atualmente visíveis e não encontradas (do backend):", allVisible);


    const req = this.nivel === 'Facil' ? 2 : (this.nivel === 'Medio' ? 3 : 2); // Quantidade de cartas para o turno
    const currentlyVisibleOnBackend = this.estado.cartas.filter(c => c.visivel && !c.encontrada).length;

    console.log(`Cartas visíveis no backend: ${currentlyVisibleOnBackend}, Requisito: ${req}`);

    // *** PONTO CHAVE DA CORREÇÃO: AQUI DESTRAMOS SE AINDA PRECISAMOS DE MAIS CLIQUES ***
    // Se o número de cartas abertas ainda é menor que o necessário para o turno,
    // destravamos a interface para permitir o próximo clique do jogador e retornamos.
    if (currentlyVisibleOnBackend < req) {
        console.log('Ainda não há cartas suficientes para verificação. Aguardando próximo clique.');
        this.travado = false; // <<< Destrava a interface para permitir o próximo clique
        return; // Retorna para esperar o próximo clique do usuário
    }

    // Se chegamos aqui, significa que o número 'req' de cartas foi virado.
    // A interface permanece travada (pois `this.travado` ainda é `true`).
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
    this.desenhar(); // Redesenha após a verificação (cartas podem ter virado novamente ou sido encontradas)
    this.atualizarPoderesUI();
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
    // *** PONTO CHAVE DA CORREÇÃO: Remoção da lógica complexa do finally ***
    // O `this.travado` é gerenciado explicitamente nos `if`s acima.
    // Aqui no finally, apenas garantimos que, se o jogo não finalizou e o turno ainda é humano
    // (o que significa que houve um erro e não um fluxo normal de espera de cliques),
    // a interface seja destravada para não ficar presa.
    if (!this.estado.finalizado && this.turno === 'humano') {
        this.travado = false;
        console.log('Interface destravada no finally (erro/interrupção). Turno atual:', this.turno);
    }
    // Se o turno for 'ia' ou 'finalizado', o destravamento será tratado por `jogadaIA` ou o jogo permanecerá finalizado.
    console.log('--- Fim fazerJogada ---');
  }
}

async jogadaIA() {
  try { 
    console.log('--- Iniciando jogada da IA ---');
    await new Promise(r => setTimeout(r, 1000)); 

    this.travado = true; // Trava a interface durante toda a jogada da IA

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
    this.atualizarPoderesUI();
    console.log('IA abriu cartas. Estado atual:', this.estado.pontuacao);

    console.log('IA aguardando 2s para visualização...');
    await new Promise(r => setTimeout(r, 2000));
    console.log('IA concluiu visualização.');

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
    this.atualizarPoderesUI();
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
      this.travado = false; // Destrava a interface para o turno humano
      console.log('Interface destravada no finally de jogadaIA. Turno atual:', this.turno);
    } else {
        console.log('JOGO FINALIZADO NO FINALLY DA jogadaIA. Interface permanece travada.');
    }
    console.log('--- Fim jogadaIA ---');
  }
}

  async usarPoder(poder) {
    if (!jogoUI || this.turno !== 'humano' || this.travado) {
      console.warn('Não é possível usar poder neste momento.');
      return;
    }

    this.travado = true; // Trava a interface enquanto o poder é processado

    try {
      let resp;
      if (poder === 'embaralhar') {
        resp = await fetch(`${API_BASE}/poder/embaralhar`, { method: 'POST' });
      } else if (poder === 'congelar') {
        const pos = parseInt(prompt('Qual posição (0–23) deseja congelar?'), 10);
        if (isNaN(pos) || pos < 0 || pos >= COLUNAS * LINHAS) {
          alert('Posição inválida.');
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
        console.warn('Poder desconhecido:', poder);
        return;
      }

      if (!resp.ok) {
        const errorText = await resp.text();
        console.error(`Erro ao usar ${poder}: ${resp.status} - ${errorText}`);
        try {
            const { erro } = JSON.parse(errorText); 
            alert(`Não foi possível usar ${poder}: ${erro}`);
        } catch (parseError) {
            alert(`Não foi possível usar ${poder}: Erro desconhecido ou resposta inválida do servidor.`);
        }
      } else {
        this.estado = await resp.json();
        this.desenhar();
        this.atualizarPoderesUI();
        if (poder === 'dica') {
          alert(`Dica usada! Restam ${this.estado.dicasRestantes} dicas.` +
            (this.estado.cooldownDicaSec
              ? ` Aguarde ${this.estado.cooldownDicaSec}s para a próxima.` : ''));
        }
      }
    } catch (error) {
      console.error(`Erro inesperado ao usar poder ${poder}:`, error);
      alert(`Ocorreu um erro ao usar ${poder}.`);
    } finally {
      this.travado = false; // Destrava a interface após o uso do poder
    }
  }

  atualizarPoderesUI() {
    if (this.estado) {
      this.especiaisRestantesDisplay.innerText = this.estado.especiaisRestantes;
      this.dicasRestantesDisplay.innerText     = this.estado.dicasRestantes;

      if (this.estado.cooldownDicaSec > 0) {
        this.dicaCooldownDisplay.style.display = 'block';
        this.cooldownSecDisplay.innerText = this.estado.cooldownDicaSec;
      } else {
        this.dicaCooldownDisplay.style.display = 'none';
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
      // Destaca cartas congeladas
      if (this.estado.cartasCongeladas[i]) {
          ctx.fillStyle = 'rgba(0, 0, 255, 0.3)'; // Azul transparente
          ctx.fillRect(cx, cy, TAM, TAM);
      }
      if (!c.visivel) return; // Se a carta não está visível, não desenha a imagem/texto
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
