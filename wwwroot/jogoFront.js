const API_BASE = 'http://localhost:5091/api/jogo';
const canvas = document.getElementById('gameCanvas');
const ctx = canvas.getContext('2d');

const COLUNAS = 8, LINHAS = 6, TAM = 80;
canvas.width = COLUNAS * TAM;
canvas.height = LINHAS * TAM;

let jogoUI;

const imagens = {};

/**
 * Pre-carrega as imagens do jogo.
 * @param {string[]} urls - Um array de URLs de imagens a serem carregadas.
 * @returns {Promise<void[]>} Uma promessa que resolve quando todas as imagens forem carregadas.
 */
function preloadImagens(urls) {
    return Promise.all(
        urls.map(u => new Promise(res => {
            const img = new Image();
            img.src = u;
            img.onload = () => {
                imagens[u] = img;
                res();
            };
            img.onerror = () => {
                // Em caso de erro ao carregar a imagem, ainda resolve para não travar o preload.
                // Pode-se adicionar uma imagem de fallback ou log de erro aqui.
                console.warn(`Falha ao carregar imagem: ${u}`);
                res();
            };
        }))
    );
}

// Este array deve conter todas as URLs de imagens que serão usadas no jogo
const urls = Array.from({
    length: 24
}, (_, i) => `img/c${i+1}.png`);

/**
 * Gerencia a interface do jogo, interações com o usuário e comunicação com o backend.
 */
class InterfaceJogo {
    /**
     * Construtor da classe InterfaceJogo.
     * @param {string} modo - O modo de jogo ('PvAI' ou 'Coop').
     * @param {string} nivel - O nível de dificuldade ('Facil', 'Medio', 'Dificil', 'Extremo').
     * @param {string} nome - O nome do jogador humano.
     */
    constructor(modo, nivel, nome) {
        this.modo = modo;
        this.nivel = nivel;
        this.nome = nome;
        this.estado = null; // O estado atual do jogo, recebido do backend
        this.turno = 'humano'; // Controla de quem é a vez ('humano' ou 'ia')
        this.travado = false; // Flag para impedir cliques enquanto o jogo processa
        this.modoCongelamentoAtivo = false; // Flag para o modo de uso do poder 'congelar'
        this.intervaloTempo = null; // ID do intervalo para o cronômetro do modo cooperativo
        this.humanCardsOpenedThisTurn = []; // Rastreia as posições das cartas viradas pelo humano no turno atual

        // Adiciona o listener de clique ao canvas do jogo
        canvas.addEventListener('click', e => this.lidarComClique(e));

        // Referências aos elementos da UI para exibição de pontuações e tempo
        this.pontuacaoDisplay = document.getElementById('pontuacaoAtual');
        this.pontuacaoHumanoPvAIDisplay = document.getElementById('pontuacaoHumano');
        this.pontuacaoOponentePvAIDisplay = document.getElementById('pontuacaoOponente');
        this.tempoRestanteDisplay = document.getElementById('tempoRestante');

        // Referências aos elementos da UI para exibição de poderes
        this.especiaisRestantesDisplay = document.getElementById('especiaisRestantes');
        this.dicasRestantesDisplay = document.getElementById('dicasRestantes');
        this.dicaCooldownDisplay = document.getElementById('dicaCooldown');
        this.cooldownSecDisplay = document.getElementById('cooldownSec');

        // Adiciona listeners aos botões de poderes
        document.getElementById('btnEmbaralhar').addEventListener('click', () => this.usarPoder('embaralhar'));
        document.getElementById('btnCongelar').addEventListener('click', () => this.ativarModoCongelamento());
        document.getElementById('btnDica').addEventListener('click', () => this.usarPoder('dica'));
    }

    /**
     * Inicia um novo jogo, comunicando-se com o backend.
     */
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
        this.humanCardsOpenedThisTurn = []; // Garante que esteja vazio ao iniciar um novo jogo
        this.desenhar(); // Desenha o estado inicial do tabuleiro
        this.atualizarPoderesUI(); // Atualiza a UI dos poderes e pontuações

        // Configura o cronômetro para o modo cooperativo
        if (this.modo === 'Coop') {
            if (this.intervaloTempo) {
                clearInterval(this.intervaloTempo);
            }
            this.atualizarTempoUI(); // Atualiza imediatamente
            this.intervaloTempo = setInterval(() => this.atualizarTempoUI(), 1000); // Atualiza a cada segundo
        } else {
            // Limpa o cronômetro se não for modo cooperativo
            if (this.intervaloTempo) {
                clearInterval(this.intervaloTempo);
                this.intervaloTempo = null;
            }
            this.tempoRestanteDisplay.innerText = ''; // Limpa o display de tempo
        }
    }

    /**
     * Atualiza a exibição do tempo restante no modo cooperativo.
     */
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
                // Exibe mensagens de fim de jogo baseadas no tempo e cartas encontradas
                if (!this.estado.tempoEsgotado && this.estado.todasCartasEncontradas) {
                    alert('Parabéns, equipe! Vocês revelaram todas as cartas a tempo!');
                } else if (this.estado.tempoEsgotado) {
                    alert('O tempo esgotou! A equipe não conseguiu revelar todas as cartas. Tente novamente!');
                }
            }
        }
    }

    /**
     * Lida com o clique do usuário no canvas, direcionando para a função correta
     * (jogada normal ou congelamento).
     * @param {MouseEvent} evt - O evento de clique.
     */
    async lidarComClique(evt) {
        if (this.modoCongelamentoAtivo) {
            this.processarCliqueCongelamento(evt);
        } else {
            this.fazerJogada(evt);
        }
    }

    /**
     * Ativa o modo de congelamento, permitindo que o jogador selecione uma carta para congelar.
     */
    ativarModoCongelamento() {
        if (this.travado || this.estado.especiaisRestantes <= 0) {
            alert("Não é possível congelar agora ou você não tem especiais restantes.");
            return;
        }
        this.modoCongelamentoAtivo = true;
        alert("Modo de congelamento ativado. Clique na carta que deseja congelar.");
    }

    /**
     * Processa o clique do usuário quando o modo de congelamento está ativo.
     * @param {MouseEvent} evt - O evento de clique.
     */
    async processarCliqueCongelamento(evt) {
        if (this.travado) return; // Não permite ações se a interface estiver travada

        const x = Math.floor(evt.offsetX / TAM);
        const y = Math.floor(evt.offsetY / TAM);
        const pos = y * COLUNAS + x;

        // Valida a posição da carta para congelar
        if (x < 0 || x >= COLUNAS || y < 0 || y >= LINHAS || this.estado.cartas[pos].encontrada) {
            alert("Não é possível congelar uma posição inválida ou uma carta já encontrada.");
            this.modoCongelamentoAtivo = false; // Sai do modo de congelamento
            return;
        }

        // Usa o poder de congelamento na posição selecionada
        await this.usarPoder('congelar', pos);
        this.modoCongelamentoAtivo = false; // Desativa o modo de congelamento após o uso
    }

    /**
     * Processa a jogada do jogador humano.
     * @param {MouseEvent} evt - O evento de clique na carta.
     */
    async fazerJogada(evt) {
        // Sai se não for o turno do humano, o jogo estiver finalizado ou a interface estiver travada
        if (this.turno !== 'humano' || this.estado.finalizado || this.travado) return;

        const x = Math.floor(evt.offsetX / TAM);
        const y = Math.floor(evt.offsetY / TAM);
        const pos = y * COLUNAS + x;

        // Determina quantas cartas são necessárias para o nível de dificuldade atual
        let requiredCardsForTurn = this.nivel === 'Facil' ? 2 : (this.nivel === 'Medio' ? 3 : 4);
        if (this.nivel === 'Extremo') {
            requiredCardsForTurn = 4;
        }

        // Impede o clique em cartas já visíveis, encontradas ou congeladas
        if (this.estado.cartas[pos].visivel || this.estado.cartas[pos].encontrada || this.estado.cartasCongeladas[pos]) {
            if (this.estado.cartasCongeladas[pos]) {
                alert('Esta carta está congelada e não pode ser virada nesta rodada.');
            }
            return;
        }

        // Impede a abertura de mais cartas do que o permitido em um único turno antes da verificação
        if (this.humanCardsOpenedThisTurn.length >= requiredCardsForTurn) {
            alert(`Você já virou ${requiredCardsForTurn} cartas. Vire-as para completar o turno.`);
            return;
        }

        // Otimisticamente atualiza a UI: vira a carta e redesenha
        this.estado.cartas[pos].visivel = true;
        this.humanCardsOpenedThisTurn.push(pos); // Adiciona ao rastreador local
        this.desenhar();

        try {
            // Envia o clique para o servidor para abrir a carta
            const openResp = await fetch(`${API_BASE}/jogada/abrir`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    posicao: pos
                })
            });

            if (!openResp.ok) {
                // Em caso de erro no servidor, reverte a UI e exibe mensagem
                const errorData = await openResp.json().catch(() => ({ erro: "Erro desconhecido." }));
                alert(`Erro ao virar carta: ${errorData.erro}. Tente novamente.`);
                this.estado.cartas[pos].visivel = false;
                this.humanCardsOpenedThisTurn = this.humanCardsOpenedThisTurn.filter(p => p !== pos);
                this.desenhar();
                return;
            }

            // Atualiza o estado com a resposta do servidor e redesenha
            this.estado = await openResp.json();
            this.desenhar();
            this.atualizarPoderesUI();

            // Verifica se cartas suficientes foram abertas para este turno
            // O número de cartas visíveis não encontradas no backend é a fonte de verdade
            const currentlyVisibleOnBackend = this.estado.cartas.filter(c => c.visivel && !c.encontrada).length;

            if (currentlyVisibleOnBackend === requiredCardsForTurn) {
                this.travado = true; // Trava a UI durante a verificação e o turno da IA
                await new Promise(r => setTimeout(r, 2000)); // Pequena pausa para o jogador ver as cartas

                // Envia a requisição para o servidor verificar a jogada
                const verifyResp = await fetch(`${API_BASE}/jogada/verificar`, {
                    method: 'POST'
                });

                if (!verifyResp.ok) {
                    alert('Erro ao verificar jogada. Tente novamente.');
                    return;
                }

                // Atualiza o estado com o resultado da verificação e redesenha
                this.estado = await verifyResp.json();
                this.desenhar();
                this.atualizarPoderesUI();

                this.humanCardsOpenedThisTurn = []; // Reinicia o rastreador de cartas para o próximo turno

                if (this.estado.finalizado) {
                    // Lida com o fim do jogo se o humano encontrou todas as cartas
                    this.handleGameEnd();
                    return;
                }

                // A IA joga em ambos os modos, PvAI e Coop, se o jogo não estiver finalizado
                this.turno = 'ia';
                await this.jogadaIA();
                
            } else {
                // Se o humano ainda precisa abrir mais cartas, a interface permanece destravada
                this.travado = false;
            }

        } catch (error) {
            console.error('Erro em fazerJogada:', error);
            alert('Ocorreu um erro no jogo. Por favor, reinicie.');
        }
    }

    /**
     * Executa a jogada da Inteligência Artificial.
     */
    async jogadaIA() {
        try {
            this.travado = true; // Trava a interface durante o turno da IA
            await new Promise(r => setTimeout(r, 1000)); // Pequena pausa para simular pensamento da IA

            // Requisição para a IA abrir suas cartas
            const abrir = await fetch(`${API_BASE}/ia/abrir`, {
                method: 'POST'
            });

            if (!abrir.ok) {
                alert('Erro na jogada da IA (abrir).');
                return;
            }

            // Atualiza o estado com as cartas viradas pela IA e redesenha
            this.estado = await abrir.json();
            this.desenhar();
            this.atualizarPoderesUI();

            await new Promise(r => setTimeout(r, 2000)); // Espera para o jogador ver as cartas da IA

            // Requisição para a IA resolver (verificar correspondências)
            const resolver = await fetch(`${API_BASE}/ia/resolver`, {
                method: 'POST'
            });

            if (!resolver.ok) {
                alert('Erro na jogada da IA (resolver).');
                return;
            }

            // Atualiza o estado com o resultado da jogada da IA e redesenha
            this.estado = await resolver.json();
            this.desenhar();
            this.atualizarPoderesUI();

            // Verifica se o jogo terminou após a jogada da IA
            if (this.estado.finalizado) {
                this.handleGameEnd();
                return;
            }
            this.turno = 'humano'; // Passa o turno de volta para o humano

        } catch (error) {
            console.error("Erro no turno da IA:", error);
            alert('Ocorreu um erro no turno da IA.');
        } finally {
            // Garante que a interface seja desbloqueada se o jogo não terminou
            if (!this.estado.finalizado) {
                this.travado = false;
            }
        }
    }

    /**
     * Permite que o jogador use um poder especial (embaralhar, congelar, dica).
     * @param {string} poder - O tipo de poder a ser usado.
     * @param {number} [pos=-1] - A posição da carta, se o poder for 'congelar'.
     */
    async usarPoder(poder, pos = -1) {
        // Não permite usar poderes se não for o turno do humano, se a UI estiver travada, ou se jogoUI não estiver inicializado
        if (!jogoUI || this.turno !== 'humano' || this.travado) return;

        this.travado = true; // Trava a interface ao usar um poder

        try {
            let resp;
            if (poder === 'embaralhar') {
                resp = await fetch(`${API_BASE}/poder/embaralhar`, { method: 'POST' });
            } else if (poder === 'congelar') {
                if (pos === -1) { // Validação de posição para congelar
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
                return; // Sai se o poder não for reconhecido
            }

            if (!resp.ok) {
                // Trata erros de API e exibe mensagens ao usuário
                const errorText = await resp.text();
                try {
                    const { erro } = JSON.parse(errorText);
                    alert(`Não foi possível usar ${poder}: ${erro}`);
                } catch {
                    alert(`Não foi possível usar ${poder}: Erro desconhecido ou resposta inválida do servidor.`);
                }
            } else {
                // Atualiza o estado do jogo e a UI após o uso do poder
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
            this.travado = false; // Destrava a interface
        }
    }

    /**
     * Atualiza os displays de poderes e pontuações na interface do usuário.
     */
    atualizarPoderesUI() {
        if (this.estado) {
            this.especiaisRestantesDisplay.innerText = this.estado.especiaisRestantes;
            this.dicasRestantesDisplay.innerText = this.estado.dicasRestantes;

            // Exibe ou oculta o cooldown da dica
            if (this.estado.cooldownDicaSec > 0) {
                this.dicaCooldownDisplay.style.display = 'block';
                this.cooldownSecDisplay.innerText = this.estado.cooldownDicaSec;
            } else {
                this.dicaCooldownDisplay.style.display = 'none';
            }

            // Lógica para exibir pontuações diferentes com base no modo de jogo
            if (this.modo === 'PvAI') {
                // Modo Competitivo: Mostra pontuações individuais, oculta a geral e o tempo
                if (this.pontuacaoHumanoPvAIDisplay) this.pontuacaoHumanoPvAIDisplay.style.display = 'block';
                if (this.pontuacaoOponentePvAIDisplay) this.pontuacaoOponentePvAIDisplay.style.display = 'block';
                if (this.pontuacaoDisplay) this.pontuacaoDisplay.style.display = 'none'; // Oculta pontuação geral
                if (this.tempoRestanteDisplay) this.tempoRestanteDisplay.style.display = 'none'; // Oculta o tempo

                // Atualiza os textos das pontuações individuais
                if (this.pontuacaoHumanoPvAIDisplay && this.estado.pontuacaoHumano !== undefined) {
                    this.pontuacaoHumanoPvAIDisplay.innerText = `Seus Pontos: ${this.estado.pontuacaoHumano}`;
                }
                if (this.pontuacaoOponentePvAIDisplay && this.estado.pontuacaoMaquina !== undefined) {
                    this.pontuacaoOponentePvAIDisplay.innerText = `Pontos do Oponente: ${this.estado.pontuacaoMaquina}`;
                }
            } else { // Modo 'Coop'
                // Modo Cooperativo: Mostra pontuação geral e o tempo, oculta as individuais
                if (this.pontuacaoHumanoPvAIDisplay) this.pontuacaoHumanoPvAIDisplay.style.display = 'none';
                if (this.pontuacaoOponentePvAIDisplay) this.pontuacaoOponentePvAIDisplay.style.display = 'none';
                if (this.pontuacaoDisplay) this.pontuacaoDisplay.style.display = 'block'; // Mostra pontuação geral
                if (this.tempoRestanteDisplay) this.tempoRestanteDisplay.style.display = 'block'; // Mostra o tempo

                // Atualiza o texto da pontuação da equipe
                if (this.pontuacaoDisplay && this.estado) {
                    this.pontuacaoDisplay.innerText = `Pontuação da Equipe: ${this.estado.pontuacao}`;
                }
                this.atualizarTempoUI(); // Chama para garantir que o tempo esteja atualizado
            }
        }
    }

    /**
     * Lida com a lógica de fim de jogo e exibe a mensagem final.
     */
    handleGameEnd() {
        let message = "Partida encerrada!";
        if (this.modo === 'Coop') { // Modo Cooperativo
            if (this.estado.todasCartasEncontradas && !this.estado.tempoEsgotado) {
                message = `Parabéns! Você revelou todas as cartas!\nPontuação Final: ${this.estado.pontuacao}`;
            } else if (this.estado.tempoEsgotado) {
                message = `O tempo esgotou! A equipe não conseguiu revelar todas as cartas.\nPontuação Final: ${this.estado.pontuacao}. Tente novamente!`;
            }
        } else if (this.modo === 'PvAI') { // Modo Competitivo
            if (this.estado.pontuacaoHumano > this.estado.pontuacaoMaquina) {
                message = `Parabéns! Você ganhou!\nSua Pontuação: ${this.estado.pontuacaoHumano}\nPontuação da Máquina: ${this.estado.pontuacaoMaquina}`;
            } else if (this.estado.pontuacaoHumano < this.estado.pontuacaoMaquina) {
                message = `Opss. Você perdeu!\nSua Pontuação: ${this.estado.pontuacaoHumano}\nPontuação da Máquina: ${this.estado.pontuacaoMaquina}`;
            } else {
                message = `Empate!\nSua Pontuação: ${this.estado.pontuacaoHumano}\nPontuação da Máquina: ${this.estado.pontuacaoMaquina}`;
            }
        }
        alert(message); // Exibe a mensagem final do jogo
    }

    /**
     * Desenha o estado atual do tabuleiro de jogo no canvas.
     */
    desenhar() {
        ctx.clearRect(0, 0, canvas.width, canvas.height); // Limpa o canvas
        ctx.font = '16px sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        this.estado.cartas.forEach((c, i) => {
            const cx = (i % COLUNAS) * TAM;
            const cy = Math.floor(i / COLUNAS) * TAM;
            ctx.strokeStyle = '#333';
            ctx.strokeRect(cx, cy, TAM, TAM); // Desenha o contorno da carta

            // Desenha o efeito de congelamento se a carta estiver congelada
            if (this.estado.cartasCongeladas[i]) {
                ctx.fillStyle = 'rgba(0, 0, 255, 0.3)'; // Azul transparente
                ctx.fillRect(cx, cy, TAM, TAM);
            }

            // Desenha a carta apenas se estiver visível ou já encontrada
            if (!c.visivel && !c.encontrada) return;
            const img = imagens[c.valor];
            if (img) ctx.drawImage(img, cx, cy, TAM, TAM); // Desenha a imagem da carta
            else {
                ctx.fillStyle = '#000';
                ctx.fillText(c.valor, cx + TAM / 2, cy + TAM / 2); // Fallback para texto se a imagem não carregar
            }
        });
    }
}

// Quando a janela carrega, pré-carrega as imagens e configura o botão Iniciar
window.onload = async () => {
    await preloadImagens(urls); // Espera todas as imagens serem carregadas antes de habilitar o jogo
    document.getElementById('btnIniciar').addEventListener('click', () => {
        const nome = document.getElementById('inputNome').value.trim();
        const modo = document.getElementById('selModo').value;
        const nivel = document.getElementById('selNivel').value;

        if (!nome) {
            alert("Digite o nome do jogador antes de iniciar.");
            return;
        }

        // Cria uma nova instância da interface do jogo e a inicia
        jogoUI = new InterfaceJogo(modo, nivel, nome);
        jogoUI.iniciar();
    });
};

/**
 * Função para carregar e exibir o Top 5 do ranking global.
 */
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

            // Constrói a tabela do ranking dinamicamente
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

// Configura o modal do Top 5 ao carregar o DOM
document.addEventListener("DOMContentLoaded", () => {
    const btnTop5 = document.getElementById("btn-top5");
    const modal = document.getElementById("modal-top5");
    const fecharModal = document.getElementById("fechar-modal");

    // Abre o modal e carrega o ranking quando o botão "Top 5 Global" é clicado
    btnTop5.onclick = () => {
        modal.style.display = "block";
        carregarTop5();
    };

    // Fecha o modal quando o botão de fechar é clicado
    fecharModal.onclick = () => {
        modal.style.display = "none";
    };

    // Fecha o modal se o usuário clicar fora dele
    window.onclick = (event) => {
        if (event.target == modal) {
            modal.style.display = "none";
        }
    };
});