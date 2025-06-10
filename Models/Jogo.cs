using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace server.Models;

public class Jogo {
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
        Maquina = new AIPlayer(mem);
        Deck = GerarDeck(tamanho);
        congeladas = new bool[tamanho];
        cronometro = Stopwatch.StartNew();
        _tempoInicioJogada = DateTime.UtcNow;
        tempoRestanteCronometro = Stopwatch.StartNew(); 

        if (Modo == "Coop") {
            TempoLimiteSegundos = 180;
        } else {
             tempoRestanteCronometro.Stop(); 
        }
    }

    private List<Carta> GerarDeck(int tam) {
        var lista = new List<Carta>();
        var rnd = new Random();
        RequisitoGrupoPorValor = new Dictionary<string, int>();

        if (Nivel == "Extremo")
        {
            int numPares = 4;
            int numTrios = 4;
            int numQuadras = 7;

            // Garantir que o tamanho do deck seja suficiente para os grupos definidos
            // Se o tamanho fornecido for menor, ajusta para um mínimo que possa acomodar os grupos
            if (tam < (numPares * 2 + numTrios * 3 + numQuadras * 4)) {
                 tam = 48; // Um tamanho razoável para o exemplo de Extremo
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
            // Lógica para níveis Fácil, Médio, Difícil (grupo fixo)
            int grupo = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : (Nivel == "Dificil" ? 4 : 2));
            int pares = tam / grupo; // 'pares' aqui representa o número de valores únicos de cartas

            for (int i = 0; i < pares; i++) {
                for (int j = 0; j < grupo; j++) {
                    string url = $"img/c{i + 1}.png";
                    lista.Add(new Carta { Valor = url });
                }
                RequisitoGrupoPorValor[$"img/c{i + 1}.png"] = grupo;
            }
        }
        
        // Ajustar o tamanho da lista para o tamanho desejado, se houver excesso ou falta
        while (lista.Count > tam)
        {
            lista.RemoveAt(lista.Count - 1);
        }
        while (lista.Count < tam)
        {
            // Adicionar cartas de forma a tentar completar o tamanho, se necessário
            // Para simplificar, adiciona mais de um tipo aleatório já existente no deck
            var randomUrl = RequisitoGrupoPorValor.Keys.OrderBy(x => rnd.Next()).FirstOrDefault();
            if (randomUrl != null)
            {
                lista.Add(new Carta { Valor = randomUrl });
            }
            else // Caso o deck esteja vazio por algum motivo (improvável com a lógica acima), adicione um par
            {
                string url = $"img/c{1}.png";
                lista.Add(new Carta { Valor = url });
                RequisitoGrupoPorValor[url] = 2; // Assume que img/c1.png é um par
            }
        }


        // Embaralhar a lista de cartas
        for (int i = lista.Count - 1; i >= 1; i--) {
            int j = rnd.Next(i + 1);
            (lista[i], lista[j]) = (lista[j], lista[i]);
        }

        return lista;
    }

    public void EmbaralharBaixo() {
        if (especiaisUsados >= MAX_ESPECIAIS) return;
        especiaisUsados++;
        var rnd = new Random();
        var indices = Deck.Select((c, i) => new { c, i })
            .Where(x => !x.c.Visivel && !x.c.Encontrada)
            .Select(x => x.i).ToList();
        for (int i = indices.Count - 1; i > 0; i--) {
            int j = rnd.Next(i + 1);
            var tmp = Deck[indices[i]];
            Deck[indices[i]] = Deck[indices[j]];
            Deck[indices[j]] = tmp;
        }
    }

    public void CongelarCarta(int pos) {
        if (especiaisUsados >= MAX_ESPECIAIS) {
            throw new InvalidOperationException("Limite de poderes especiais atingido.");
        }
        if (Deck[pos].Encontrada || Deck[pos].Visivel) {
            throw new InvalidOperationException("Não é possível congelar uma carta já encontrada ou visível.");
        }
        if (congeladas[pos]) {
            throw new InvalidOperationException("Esta carta já está congelada.");
        }

        especiaisUsados++;
        congeladas[pos] = true;
        posicaoCongeladaNaRodadaAnterior = pos;
    }

    private void DescongelarCartasAntigas() {
        if (posicaoCongeladaNaRodadaAnterior.HasValue) {
            congeladas[posicaoCongeladaNaRodadaAnterior.Value] = false;
            posicaoCongeladaNaRodadaAnterior = null;
        }
    }

    public Estado ObterEstado() {
        // Verifica se todas as cartas foram encontradas (independente do tamanho do grupo)
        bool todasCartasEncontradas = Deck.All(c => c.Encontrada);
        bool tempoEsgotado = false;
        int tempoRestanteCoop = 0;

        if (Modo == "Coop") {
            var tempoPassado = (int)tempoRestanteCronometro.Elapsed.TotalSeconds;
            tempoRestanteCoop = Math.Max(0, TempoLimiteSegundos - tempoPassado);
            if (tempoRestanteCoop <= 0 && !todasCartasEncontradas) {
                tempoEsgotado = true;
            }
        }

        bool finalizado = todasCartasEncontradas || tempoEsgotado;

        if (finalizado && !pontuacaoSalva) {
            var repo = new RankingRepository();
            repo.SalvarPontuacao(new RankingEntry {
                Nome = Humano.Nome,
                Modo = Modo,
                Nivel = Nivel,
                Pontuacao = Humano.Pontos,
                DataHora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            pontuacaoSalva = true;
            if (Modo == "Coop" && tempoRestanteCronometro != null) {
                tempoRestanteCronometro.Stop();
            }
        }

        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;

        return new Estado {
            Cartas = Deck,
            Finalizado = finalizado,
            Modo = Modo,
            Nivel = Nivel,
            Pontuacao = Humano.Pontos,
            DuracaoSec = (int)cronometro.Elapsed.TotalSeconds,
            EspeciaisRestantes = MAX_ESPECIAIS - especiaisUsados,
            DicasRestantes = MAX_DICAS - dicasUsadas,
            CooldownDicaSec = dicasUsadas >= MAX_DICAS ? 0 :
                Math.Max(0, DICA_COOLDOWN_SEC - (int)segundosDesdeUltima),
            CartasCongeladas = congeladas,
            TempoRestanteCoop = tempoRestanteCoop,
            TempoEsgotado = tempoEsgotado,
            TodasCartasEncontradas = todasCartasEncontradas
        };
    }

    public Estado AbrirCartaHumano(int pos) {
        if (congeladas[pos]) {
            throw new InvalidOperationException("Não é possível virar uma carta congelada.");
        }

        if (Deck[pos].Visivel || Deck[pos].Encontrada) {
            return ObterEstado(); 
        }

        // Antes de virar a primeira carta do turno, esconde as cartas visíveis não encontradas
        if (_humanOpenedCards.Count == 0) {
            foreach(var carta in Deck) {
                if (carta.Visivel && !carta.Encontrada) {
                    carta.Visivel = false; 
                }
            }
            _tempoInicioJogada = DateTime.UtcNow;
        }

        Deck[pos].Visivel = true;
        Humano.Pontos += 30; // Pontos por virar uma carta
        _humanOpenedCards.Add(pos);

        return ObterEstado();
    }

    public Estado ProcessarJogadaHumano() {
        int cartasParaVirar = 4; // Para o nível Extremo, o jogador sempre vira 4 cartas por turno
        if (Nivel != "Extremo")
        {
            cartasParaVirar = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 4);
        }

        // Se o número de cartas viradas não corresponde ao esperado para o turno, retorna o estado atual.
        if (_humanOpenedCards.Count != cartasParaVirar) {
            return ObterEstado();
        }
        
        // Agrupar as cartas viradas por valor para verificar correspondências
        var groupedCards = _humanOpenedCards
            .Select(pos => new { Pos = pos, Card = Deck[pos] })
            .GroupBy(x => x.Card.Valor)
            .ToList();

        int pontosGanhosPorAcerto = 0;
        bool algumaCorrespondencia = false;

        foreach (var group in groupedCards)
        {
            // Verifica o requisito de grupo para o valor da carta
            if (RequisitoGrupoPorValor.TryGetValue(group.Key, out int requiredCount))
            {
                // Se a quantidade de cartas viradas com aquele valor é suficiente para formar um grupo
                if (group.Count() >= requiredCount)
                {
                    pontosGanhosPorAcerto += 500; // Pontos base por grupo formado
                    algumaCorrespondencia = true;
                    foreach (var item in group.Take(requiredCount)) // Marca apenas o número de cartas necessárias como encontradas
                    {
                        Deck[item.Pos].Encontrada = true;
                        // NÃO VIRA Deck[item.Pos].Visivel = false; aqui para que permaneçam visíveis
                    }
                    RegistrarGrupoFormado();
                }
            }
        }
        
        if (algumaCorrespondencia) {
            // Calcula pontos extras com base no tempo de resposta
            var tempoResposta = (DateTime.UtcNow - _tempoInicioJogada).TotalSeconds;
            if (tempoResposta <= 2) {
                pontosGanhosPorAcerto += 1000; // Resposta muito rápida
            } else if (tempoResposta <= 4) {
                pontosGanhosPorAcerto += 500;  // Resposta rápida
            } else if (tempoResposta <= 6) {
                pontosGanhosPorAcerto += 200;  // Resposta moderada
            }
            Humano.Pontos += pontosGanhosPorAcerto;
        } else {
            // Se não houve nenhuma correspondência, vira as cartas para baixo e perde pontos
            foreach (var pos in _humanOpenedCards) {
                Deck[pos].Visivel = false; 
            }
            Humano.Pontos = Math.Max(0, Humano.Pontos - 10); // Garante que a pontuação não seja negativa
        }

        _humanOpenedCards.Clear(); // Limpa as cartas abertas para o próximo turno
        DescongelarCartasAntigas(); // Descongela qualquer carta congelada da rodada anterior

        return ObterEstado();
    }

    private void AbrirCarta(Jogador j, int pos) {
        var carta = Deck[pos];
        if (carta.Visivel || carta.Encontrada) {
            return;
        }
        carta.Visivel = true;
        j.Pontos += 30; // Pontos por virar uma carta

        if (j == Maquina)
            Maquina.Lembrar(pos, carta.Valor); // IA lembra a carta virada
    }

    public void RegistrarGrupoFormado() {
        gruposFormados++;
    }

    public Estado JogadaIA_AbrirCartas() {
        int cartasParaVirar = 4; // Para o nível Extremo, IA sempre vira 4 cartas
        if (Nivel != "Extremo")
        {
            cartasParaVirar = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 4);
        }

        PosicoesIASelecionadas.Clear(); // Limpa as posições selecionadas pela IA

        // Esconde cartas visíveis que não foram encontradas antes do turno da IA
        foreach (var carta in Deck)
            if (carta.Visivel && !carta.Encontrada)
                carta.Visivel = false;

        int tentativas = 0, maxTentativas = 200;
        // A IA tenta virar o número de cartas necessárias para o turno
        while (PosicoesIASelecionadas.Count < cartasParaVirar && tentativas < maxTentativas) {
            int pos = Maquina.EscolherPosicao(Deck); // IA escolhe uma posição
            tentativas++;

            // Se a carta não está visível, não foi encontrada, não foi selecionada ainda e não está congelada
            if (!Deck[pos].Visivel && !Deck[pos].Encontrada && !PosicoesIASelecionadas.Contains(pos) && !congeladas[pos]) {
                Deck[pos].Visivel = true; // Vira a carta
                Maquina.Lembrar(pos, Deck[pos].Valor); // IA lembra a carta
                PosicoesIASelecionadas.Add(pos); // Adiciona à lista de cartas viradas pela IA no turno
            }
        }
        return ObterEstado();
    }

    public Estado JogadaIA_Resolver() {
        int cartasParaVirar = 4; // Para o nível Extremo, IA sempre vira 4 cartas
        if (Nivel != "Extremo")
        {
            cartasParaVirar = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 4);
        }

        // Agrupar as cartas viradas pela IA por valor para verificar correspondências
        var groupedCardsIA = PosicoesIASelecionadas
            .Select(pos => new { Pos = pos, Card = Deck[pos] })
            .GroupBy(x => x.Card.Valor)
            .ToList();

        bool algumaCorrespondenciaIA = false;
        int pontosGanhosIA = 0;

        foreach (var group in groupedCardsIA)
        {
            // Verifica o requisito de grupo para o valor da carta
            if (RequisitoGrupoPorValor.TryGetValue(group.Key, out int requiredCount))
            {
                // Se a quantidade de cartas viradas com aquele valor é suficiente para formar um grupo
                if (group.Count() >= requiredCount)
                {
                    algumaCorrespondenciaIA = true;
                    pontosGanhosIA += 300; // Pontos base da IA por grupo
                    foreach (var item in group.Take(requiredCount)) // Marca apenas o número de cartas necessárias como encontradas
                    {
                        Deck[item.Pos].Encontrada = true;
                        // NÃO VIRA Deck[item.Pos].Visivel = false; aqui para que permaneçam visíveis
                    }
                    RegistrarGrupoFormado();
                }
            }
        }

        if (algumaCorrespondenciaIA)
        {
            Humano.Pontos += pontosGanhosIA; // IA ganha pontos, que são adicionados à pontuação do humano em modo coop ou competitivo
        }
        else
        {
            // Se não houve nenhuma correspondência, vira as cartas para baixo e perde pontos
            foreach (var i in PosicoesIASelecionadas)
                Deck[i].Visivel = false;
            Humano.Pontos = Math.Max(0, Humano.Pontos - 5); // Garante que a pontuação não seja negativa
        }

        PosicoesIASelecionadas.Clear(); // Limpa as posições selecionadas pela IA
        DescongelarCartasAntigas(); // Descongela qualquer carta congelada da rodada anterior

        return ObterEstado();
    }

    public Estado UsarDica() {
        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;
        if (dicasUsadas >= MAX_DICAS) {
            throw new InvalidOperationException("Limite de 3 dicas atingido.");
        }
        if (segundosDesdeUltima < DICA_COOLDOWN_SEC) {
            throw new InvalidOperationException($"Aguarde {DICA_COOLDOWN_SEC - (int)segundosDesdeUltima}s antes de nova dica.");
        }

        dicasUsadas++;
        ultimaDica = agora;
        var rnd = new Random();
        var fechadas = Deck.Select((c, i) => (c, i))
            .Where(x => !x.c.Visivel && !x.c.Encontrada)
            .OrderBy(_ => rnd.Next())
            .Take(2).ToList(); // Pega 2 cartas aleatórias que não estão visíveis ou encontradas
        
        if (fechadas.Count > 0) {
            foreach (var (c, _) in fechadas) {
                c.Visivel = true; // Torna as cartas da dica visíveis
            }
        }
        return ObterEstado();
    }
}