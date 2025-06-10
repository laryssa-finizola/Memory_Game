using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace server.Models;

public class Jogo
{
    public List<Carta> Deck { get; }
    public Jogador Humano { get; }
    public AIPlayer Maquina { get; }
    public string Modo { get; }
    public string Nivel { get; }

    private Stopwatch cronometro;
    private int gruposFormados = 0;
    private int especiaisUsados = 0;
    private readonly bool[] congeladas;
    private int dicasUsadas = 0;
    private DateTime ultimaDica = DateTime.MinValue;

    private int? posicaoCongeladaNaRodadaAnterior = null;

    private const int MAX_ESPECIAIS = 3;
    private const int MAX_DICAS = 3;
    private const int DICA_COOLDOWN_SEC = 10;

    private int TempoLimiteSegundos;
    private Stopwatch tempoRestanteCronometro;

    private DateTime _tempoInicioJogada;

    public List<int> PosicoesIASelecionadas = new();

    private bool pontuacaoSalva = false;

    private List<int> _humanOpenedCards = new List<int>();

    public Dictionary<string, int> RequisitoGrupoPorValor { get; private set; }


    public Jogo(string nome, string modo, string nivel, int tamanho)
    {
        Modo = modo;
        Nivel = nivel;
        Humano = new Jogador { Nome = nome };
        int mem = Nivel == "Facil" ? 1 : (Nivel == "Medio" ? 3 : (Nivel == "Dificil" ? 4 : (Nivel == "Extremo" ? 4 : 1)));
        Maquina = new AIPlayer(mem)
        {
            Pontos = 0
        };
        // Define a referência do jogo na IA, o que também define o deck.
        Maquina.SetJogoReference(this); 
        Deck = GerarDeck(tamanho);
        congeladas = new bool[tamanho];
        cronometro = Stopwatch.StartNew();
        _tempoInicioJogada = DateTime.UtcNow;
        tempoRestanteCronometro = Stopwatch.StartNew();

        if (Modo == "Coop")
        {
            TempoLimiteSegundos = 180;
        }
        else
        {
            tempoRestanteCronometro.Stop();
        }
    }

    private List<Carta> GerarDeck(int tam)
    {
        var lista = new List<Carta>();
        var rnd = new Random();
        RequisitoGrupoPorValor = new Dictionary<string, int>();

        if (Nivel == "Extremo")
        {
            int numPares = 4;
            int numTrios = 4;
            int numQuadras = 7;


            if (tam < (numPares * 2 + numTrios * 3 + numQuadras * 4))
            {
                tam = 48;
            }

            // Gerar pares
            for (int i = 0; i < numPares; i++)
            {
                string url = $"img/c{i + 1}.png";
                for (int j = 0; j < 2; j++)
                {
                    lista.Add(new Carta { Valor = url });
                }
                RequisitoGrupoPorValor[url] = 2;
            }

            // Gerar trincas
            for (int i = 0; i < numTrios; i++)
            {
                string url = $"img/c{numPares + i + 1}.png";
                for (int j = 0; j < 3; j++)
                {
                    lista.Add(new Carta { Valor = url });
                }
                RequisitoGrupoPorValor[url] = 3;
            }

            // Gerar quadras
            for (int i = 0; i < numQuadras; i++)
            {
                string url = $"img/c{numPares + numTrios + i + 1}.png";
                for (int j = 0; j < 4; j++)
                {
                    lista.Add(new Carta { Valor = url });
                }
                RequisitoGrupoPorValor[url] = 4;
            }
        }
        else
        {
            // Lógica para níveis Fácil, Médio, Difícil 
            int grupo = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : (Nivel == "Dificil" ? 4 : 2));
            int pares = tam / grupo; // representa o número de valores únicos de cartas

            for (int i = 0; i < pares; i++)
            {
                for (int j = 0; j < grupo; j++)
                {
                    string url = $"img/c{i + 1}.png";
                    lista.Add(new Carta { Valor = url });
                }
                RequisitoGrupoPorValor[$"img/c{i + 1}.png"] = grupo;
            }
        }

        while (lista.Count > tam)
        {
            lista.RemoveAt(lista.Count - 1);
        }
        while (lista.Count < tam)
        {

            var randomUrl = RequisitoGrupoPorValor.Keys.OrderBy(x => rnd.Next()).FirstOrDefault();
            if (randomUrl != null)
            {
                lista.Add(new Carta { Valor = randomUrl });
            }
            else
            {
                string url = $"img/c{1}.png";
                lista.Add(new Carta { Valor = url });
                RequisitoGrupoPorValor[url] = 2;
            }
        }

        for (int i = lista.Count - 1; i >= 1; i--)
        {
            int j = rnd.Next(i + 1);
            (lista[i], lista[j]) = (lista[j], lista[i]);
        }

        return lista;
    }

    public void EmbaralharBaixo()
    {
        if (especiaisUsados >= MAX_ESPECIAIS) return;
        especiaisUsados++;
        var rnd = new Random();
        var indices = Deck.Select((c, i) => new { c, i })
            .Where(x => !x.c.Visivel && !x.c.Encontrada)
            .Select(x => x.i).ToList();
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            var tmp = Deck[indices[i]];
            Deck[indices[i]] = Deck[indices[j]];
            Deck[indices[j]] = tmp;
        }
    }

    public void CongelarCarta(int pos)
    {
        if (especiaisUsados >= MAX_ESPECIAIS)
        {
            throw new InvalidOperationException("Limite de poderes especiais atingido.");
        }
        if (Deck[pos].Encontrada || Deck[pos].Visivel)
        {
            throw new InvalidOperationException("Não é possível congelar uma carta já encontrada ou visível.");
        }
        if (congeladas[pos])
        {
            throw new InvalidOperationException("Esta carta já está congelada.");
        }

        especiaisUsados++;
        congeladas[pos] = true;
        posicaoCongeladaNaRodadaAnterior = null; // Garante que apenas uma carta por vez está congelada
    }

    private void DescongelarCartasAntigas()
    {
        if (posicaoCongeladaNaRodadaAnterior.HasValue)
        {
            congeladas[posicaoCongeladaNaRodadaAnterior.Value] = false;
            posicaoCongeladaNaRodadaAnterior = null;
        }
    }

    public Estado ObterEstado()
    {
        bool todasCartasEncontradas = Deck.All(c => c.Encontrada);
        bool tempoEsgotado = false;
        int tempoRestanteCoop = 0;

        if (Modo == "Coop")
        {
            var tempoPassado = (int)tempoRestanteCronometro.Elapsed.TotalSeconds;
            tempoRestanteCoop = Math.Max(0, TempoLimiteSegundos - tempoPassado);
            if (tempoRestanteCoop <= 0 && !todasCartasEncontradas)
            {
                tempoEsgotado = true;
            }
        }

        bool finalizado = todasCartasEncontradas || tempoEsgotado;

        if (finalizado && !pontuacaoSalva)
        {
            var repo = new RankingRepository();
            repo.SalvarPontuacao(new RankingEntry
            {
                Nome = Humano.Nome,
                Modo = Modo,
                Nivel = Nivel,
                // No modo competitivo (PvAI), o ranking deve salvar a pontuação do Humano.
                // No modo cooperativo (Coop), ele salva a pontuação combinada (Humano + IA).
                Pontuacao = (Modo == "PvAI") ? Humano.Pontos : Humano.Pontos,
                DataHora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            pontuacaoSalva = true;
            if (Modo == "Coop" && tempoRestanteCronometro != null)
            {
                tempoRestanteCronometro.Stop();
            }
        }

        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;

        return new Estado
        {
            Cartas = Deck,
            Finalizado = finalizado,
            Modo = Modo,
            Nivel = Nivel,
            Pontuacao = Humano.Pontos,
            DuracaoSec = (int)cronometro.Elapsed.TotalSeconds,
            EspeciaisRestantes = MAX_ESPECIAIS - especiaisUsados, // Propriedade corrigida em Estado.cs
            DicasRestantes = MAX_DICAS - dicasUsadas,
            CooldownDicaSec = dicasUsadas >= MAX_DICAS ? 0 :
                Math.Max(0, DICA_COOLDOWN_SEC - (int)segundosDesdeUltima),
            CartasCongeladas = congeladas,
            TempoRestanteCoop = tempoRestanteCoop,
            TempoEsgotado = tempoEsgotado,
            TodasCartasEncontradas = todasCartasEncontradas,
            PontuacaoHumano = Humano.Pontos,
            PontuacaoMaquina = Maquina.Pontos
        };
    }

    public Estado AbrirCartaHumano(int pos)
    {
        if (congeladas[pos])
        {
            throw new InvalidOperationException("Não é possível virar uma carta congelada.");
        }

        if (Deck[pos].Visivel || Deck[pos].Encontrada)
        {
            return ObterEstado();
        }

        // Antes de virar a primeira carta do turno, esconde as cartas visíveis não encontradas
        if (_humanOpenedCards.Count == 0)
        {
            foreach (var carta in Deck)
            {
                if (carta.Visivel && !carta.Encontrada)
                {
                    carta.Visivel = false;
                }
            }
            _tempoInicioJogada = DateTime.UtcNow;
        }

        Deck[pos].Visivel = true;
        Humano.Pontos += 30;
        _humanOpenedCards.Add(pos);

        return ObterEstado();
    }

    public Estado ProcessarJogadaHumano()
    {
        int cartasParaVirar = 4; // Para o nível Extremo, o jogador sempre vira 4 cartas por turno
        if (Nivel != "Extremo")
        {
            cartasParaVirar = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 4);
        }

        if (_humanOpenedCards.Count != cartasParaVirar)
        {
            // Se o número de cartas viradas não for o esperado para o turno,
            // algo inesperado aconteceu (ex: UI não enviou todas as cartas).
            // Retorna o estado atual sem processar a jogada como completa.
            return ObterEstado();
        }

        var groupedCards = _humanOpenedCards
            .Select(pos => new { Pos = pos, Card = Deck[pos] })
            .GroupBy(x => x.Card.Valor)
            .ToList();

        int pontosGanhosPorAcerto = 0;
        bool algumaCorrespondencia = false;

        foreach (var group in groupedCards)
        {
            if (RequisitoGrupoPorValor.TryGetValue(group.Key, out int requiredCount))
            {
                if (group.Count() >= requiredCount)
                {
                    pontosGanhosPorAcerto += 50; // Pontos base por grupo formado
                    algumaCorrespondencia = true;
                    foreach (var item in group.Take(requiredCount))
                    {
                        Deck[item.Pos].Encontrada = true;
                    }
                    RegistrarGrupoFormado();
                }
            }
        }

        if (algumaCorrespondencia)
        {
            var tempoResposta = (DateTime.UtcNow - _tempoInicioJogada).TotalSeconds;
            if (tempoResposta <= 1)
            {
                pontosGanhosPorAcerto += 50; // Resposta muito rápida
            }
            else if (tempoResposta <= 3)
            {
                pontosGanhosPorAcerto += 20;  // Resposta rápida
            }
            else if (tempoResposta <= 5)
            {
                pontosGanhosPorAcerto += 10;  // Resposta moderada
            }
            Humano.Pontos += pontosGanhosPorAcerto;
        }
        else
        {
            foreach (var pos in _humanOpenedCards)
            {
                Deck[pos].Visivel = false;
            }
            Humano.Pontos = Math.Max(0, Humano.Pontos - 50); //perda de 50 pontos se não acertar
        }

        _humanOpenedCards.Clear(); // Limpa as cartas viradas pelo humano para o próximo turno
        DescongelarCartasAntigas(); // Descongela cartas da rodada anterior, se houver

        return ObterEstado();
    }

    private void AbrirCarta(Jogador j, int pos)
    {
        var carta = Deck[pos];
        if (carta.Visivel || carta.Encontrada)
        {
            return;
        }
        carta.Visivel = true;
        j.Pontos += 30; // Pontos por virar uma carta

        if (j == Maquina)
            Maquina.Lembrar(pos, carta.Valor); // IA lembra a carta virada
    }


    public void RegistrarGrupoFormado()
    {
        gruposFormados++;
    }

    /// <summary>
    /// Gerencia a ação da IA de abrir cartas. A IA tenta virar o número de cartas necessárias para o turno,
    /// escolhendo entre as cartas não visíveis, não encontradas e não congeladas.
    /// </summary>
    /// <returns>O estado atualizado do jogo.</returns>
    public Estado JogadaIA_AbrirCartas()
    {
        int cartasParaVirar = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 4);
        if (Nivel == "Extremo")
        {
            cartasParaVirar = 4; // Garante que Extremo sempre vira 4
        }
        
        PosicoesIASelecionadas.Clear(); // Limpa as posições selecionadas pela IA no turno anterior

        // Esconde cartas que foram viradas pelo humano mas não formaram par
        // antes do turno da IA começar, garantindo um tabuleiro limpo.
        foreach (var carta in Deck)
            if (carta.Visivel && !carta.Encontrada)
                carta.Visivel = false;

        // Coleta todas as posições de cartas que a IA pode virar:
        // - Não visíveis (fechadas)
        // - Não encontradas (ainda não formaram par)
        // - Não congeladas (não estão temporariamente bloqueadas)
        var availablePositions = Deck.Select((c, i) => i)
                                     .Where(i => !Deck[i].Visivel && !Deck[i].Encontrada && !congeladas[i])
                                     .ToList();
        var rnd = new Random();

        // A IA tenta virar o número de cartas necessárias para o turno,
        // escolhendo-as de forma inteligente (via Maquina.EscolherPosicao)
        // ou aleatoriamente se não houver correspondências conhecidas.
        for (int i = 0; i < cartasParaVirar; i++)
        {
            // Se não houver mais cartas disponíveis para virar, a IA para.
            if (availablePositions.Count == 0) break; 

            // A IA escolhe uma posição. Maquina.EscolherPosicao usa a memória da IA
            // para tentar encontrar correspondências ou escolhe aleatoriamente.
            // AQUI ESTÁ A CHAMA CORRIGIDA PARA Maquina.EscolherPosicao
            int pos = Maquina.EscolherPosicao(availablePositions); 

            // Garante que a posição escolhida é realmente válida antes de virar
            // (evita virar cartas já viradas, encontradas ou congeladas,
            // embora 'availablePositions' já devesse ter filtrado isso).
            if (pos != -1 && !Deck[pos].Visivel && !Deck[pos].Encontrada && !congeladas[pos] && PosicoesIASelecionadas.All(p => p != pos))
            {
                Deck[pos].Visivel = true; // Vira a carta
                Maquina.Lembrar(pos, Deck[pos].Valor); // IA adiciona a carta à sua memória
                PosicoesIASelecionadas.Add(pos); // Adiciona à lista de cartas viradas pela IA neste turno

                // Remove a posição da lista de disponíveis para evitar que a IA tente virar a mesma carta novamente no mesmo turno.
                // É importante remover de availablePositions para que Maquina.EscolherPosicao não a escolha novamente
                availablePositions.Remove(pos); 
            } else {
                // Se, por alguma razão, a carta escolhida não for válida (e.g. já foi virada por um bug),
                // tenta escolher outra, mas dentro do limite do loop for.
                // É mais robusto que o loop 'while' anterior, pois o 'availablePositions' já é pré-filtrado.
                i--; // Decrementa 'i' para tentar encontrar outra carta, pois esta não foi válida
            }
        }
        return ObterEstado(); // Retorna o estado atualizado do jogo
    }

    /// <summary>
    /// Processa o resultado da jogada da IA, verificando se há correspondências
    /// e atualizando pontuações e estado do tabuleiro.
    /// </summary>
    /// <returns>O estado atualizado do jogo.</returns>
    public Estado JogadaIA_Resolver()
    {
        // Define o número de cartas para virar com base no nível (para referência, não para lógica de virar)
        int cartasParaVirar = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 4);
        if (Nivel == "Extremo")
        {
            cartasParaVirar = 4;
        }

        // Agrupa as cartas viradas pela IA por valor para verificar correspondências
        var groupedCardsIA = PosicoesIASelecionadas
            .Select(pos => new { Pos = pos, Card = Deck[pos] })
            .GroupBy(x => x.Card.Valor)
            .ToList();

        bool algumaCorrespondenciaIA = false;
        int pontosGanhosIA = 0;

        foreach (var group in groupedCardsIA)
        {
            if (RequisitoGrupoPorValor.TryGetValue(group.Key, out int requiredCount))
            {
                // Se a quantidade de cartas viradas for suficiente para um grupo
                if (group.Count() >= requiredCount)
                {
                    algumaCorrespondenciaIA = true;
                    pontosGanhosIA += 300; // Pontos por acerto da IA
                    foreach (var item in group.Take(requiredCount))
                    {
                        Deck[item.Pos].Encontrada = true; // Marca as cartas como encontradas
                    }
                    RegistrarGrupoFormado(); // Incrementa o contador de grupos formados
                }
            }
        }

        // Lógica de atribuição de pontos com base no modo de jogo (PvAI ou Coop)
        if (Modo == "PvAI") // Modo Competitivo (Player vs AI)
        {
            if (algumaCorrespondenciaIA)
            {
                Maquina.Pontos += pontosGanhosIA; // IA ganha seus próprios pontos
            }
            else
            {
                // Se a IA não acertou, vira as cartas para baixo e perde pontos
                foreach (var i in PosicoesIASelecionadas)
                    Deck[i].Visivel = false;
                Maquina.Pontos = Math.Max(0, Maquina.Pontos - 20); // Garante que a pontuação não seja negativa
            }
        }
        else // Modo Cooperativo (Coop)
        {
            if (algumaCorrespondenciaIA)
            {
                Humano.Pontos += pontosGanhosIA; // No coop, os pontos da IA são somados ao jogador humano
            }
            else
            {
                // No coop, se a IA errou, as cartas viram para baixo e o jogador humano perde pontos
                foreach (var i in PosicoesIASelecionadas)
                    Deck[i].Visivel = false;
                Humano.Pontos = Math.Max(0, Humano.Pontos - 20);
            }
        }

        PosicoesIASelecionadas.Clear(); // Limpa as posições da IA para o próximo turno dela
        DescongelarCartasAntigas(); // Descongela cartas da rodada anterior, se houver

        return ObterEstado(); // Retorna o estado atualizado do jogo
    }

    /// <summary>
    /// Permite ao jogador usar uma "dica" para revelar duas cartas aleatórias não encontradas.
    /// </summary>
    /// <returns>O estado atualizado do jogo.</returns>
    /// <exception cref="InvalidOperationException">Lançada se o limite de dicas for atingido ou o cooldown estiver ativo.</exception>
    public Estado UsarDica()
    {
        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;
        if (dicasUsadas >= MAX_DICAS)
        {
            throw new InvalidOperationException("Limite de 3 dicas atingido.");
        }
        if (segundosDesdeUltima < DICA_COOLDOWN_SEC)
        {
            throw new InvalidOperationException($"Aguarde {DICA_COOLDOWN_SEC - (int)segundosDesdeUltima}s antes de nova dica.");
        }

        dicasUsadas++;
        ultimaDica = agora;
        var rnd = new Random();
        // Seleciona 2 cartas aleatórias que não estão visíveis e não foram encontradas
        var fechadas = Deck.Select((c, i) => (c, i))
            .Where(x => !x.c.Visivel && !x.c.Encontrada)
            .OrderBy(_ => rnd.Next()) // Embaralha para pegar aleatoriamente
            .Take(2).ToList(); // Pega as duas primeiras

        if (fechadas.Count > 0)
        {
            foreach (var (c, _) in fechadas)
            {
                c.Visivel = true; // Vira as cartas selecionadas
            }
        }
        return ObterEstado();
    }
}