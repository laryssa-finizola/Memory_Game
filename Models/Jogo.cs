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

    private int jogadasIA = 0;

    private DateTime _tempoInicioJogada;

    public List<int> PosicoesIASelecionadas = new();

    private bool pontuacaoSalva = false;

    private List<int> _humanOpenedCards = new List<int>();

    public Jogo(string nome, string modo, string nivel, int tamanho)
    {
        Modo = modo;
        Nivel = nivel;
        Humano = new Jogador { Nome = nome };
        int mem = Nivel == "Facil" ? 1 : (Nivel == "Medio" ? 3 : 1);
        Maquina = new AIPlayer(mem);
        Deck = GerarDeck(tamanho);
        congeladas = new bool[tamanho];
        cronometro = Stopwatch.StartNew();
        _tempoInicioJogada = DateTime.UtcNow;

        if (Modo == "Coop") {
            TempoLimiteSegundos = 180;
            tempoRestanteCronometro = Stopwatch.StartNew();
        }
    }

    private List<Carta> GerarDeck(int tam) {
        var lista = new List<Carta>();
        int grupo = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);
        int pares = tam / grupo;

        for (int i = 0; i < pares; i++) {
            for (int j = 0; j < grupo; j++) {
                string url = $"img/c{i + 1}.png";
                lista.Add(new Carta { Valor = url });
            }
        }

        var rnd = new Random();
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
        bool todasCartasEncontradas = gruposFormados == Deck.Count / (Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2));
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

        if (_humanOpenedCards.Count == 0) {
            foreach(var carta in Deck) {
                if (carta.Visivel && !carta.Encontrada) {
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

    public Estado ProcessarJogadaHumano() {
        int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);

        if (_humanOpenedCards.Count != req) {
            return ObterEstado();
        }

        var openedCardsValues = new List<string>();
        foreach (int pos in _humanOpenedCards) {
            openedCardsValues.Add(Deck[pos].Valor);
        }
        
        bool allMatch = openedCardsValues.All(v => v == openedCardsValues[0]);

        if (allMatch) {
            foreach (var pos in _humanOpenedCards) {
                Deck[pos].Encontrada = true;
            }

            RegistrarGrupoFormado();

            var tempoResposta = (DateTime.UtcNow - _tempoInicioJogada).TotalSeconds;
            int pontosGanhosPorAcerto = 500; 

            if (tempoResposta <= 2) {
                pontosGanhosPorAcerto += 1000; 
            } else if (tempoResposta <= 4) {
                pontosGanhosPorAcerto += 500; 
            } else if (tempoResposta <= 6) {
                pontosGanhosPorAcerto += 200; 
            }

            Humano.Pontos += pontosGanhosPorAcerto;
        } else {
            foreach (var pos in _humanOpenedCards) {
                Deck[pos].Visivel = false; 
            }
            Humano.Pontos = Math.Max(0, Humano.Pontos - 10); 
        }

        _humanOpenedCards.Clear(); 

        return ObterEstado();
    }

    private void AbrirCarta(Jogador j, int pos) {
        var carta = Deck[pos];
        if (carta.Visivel || carta.Encontrada) {
            return;
        }
        carta.Visivel = true;
        j.Pontos += 30; 

        if (j == Maquina)
            Maquina.Lembrar(pos, carta.Valor);
    }

    public void RegistrarGrupoFormado() {
        gruposFormados++;
    }

    public Estado JogadaIA_AbrirCartas() {
        int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);
        PosicoesIASelecionadas.Clear(); 

        foreach (var carta in Deck)
            if (carta.Visivel && !carta.Encontrada)
                carta.Visivel = false;

        int tentativas = 0, maxTentativas = 200;
        while (PosicoesIASelecionadas.Count < req && tentativas < maxTentativas) {
            int pos = Maquina.EscolherPosicao(Deck);
            tentativas++;

            if (!Deck[pos].Visivel && !Deck[pos].Encontrada && !PosicoesIASelecionadas.Contains(pos) && !congeladas[pos]) {
                Deck[pos].Visivel = true;
                Maquina.Lembrar(pos, Deck[pos].Valor);
                PosicoesIASelecionadas.Add(pos);
            }
        }
        return ObterEstado();
    }

    public Estado JogadaIA_Resolver() {
        int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);
        var valores = PosicoesIASelecionadas.Select(i => Deck[i].Valor).ToList();
        bool todasIguais = valores.Count == req && valores.All(v => v == valores[0]);

        if (todasIguais) {
            foreach (var i in PosicoesIASelecionadas)
                Deck[i].Encontrada = true;
            RegistrarGrupoFormado();
            Humano.Pontos += 300; 
        } else {
            foreach (var i in PosicoesIASelecionadas)
                Deck[i].Visivel = false;
            Humano.Pontos = Math.Max(0, Humano.Pontos - 5);
        }

        PosicoesIASelecionadas.Clear();
        DescongelarCartasAntigas();

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
            .Take(2).ToList();
        
        if (fechadas.Count > 0) {
            foreach (var (c, _) in fechadas) {
                c.Visivel = true;
            }
        }
        return ObterEstado();
    }
}
