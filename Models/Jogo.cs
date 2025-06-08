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

    private const int MAX_ESPECIAIS = 3;
    private const int MAX_DICAS = 3;
    private const int DICA_COOLDOWN_SEC = 10;

    private int jogadasIA = 0; // contador para controlar jogadas da IA

    private DateTime _tempoInicioJogada;

    public List<int> PosicoesIASelecionadas = new(); // usado nos novos endpoints da IA

    private bool pontuacaoSalva = false;


    public Jogo(string nome, string modo, string nivel, int tamanho)
    {
        Modo = modo;
        Nivel = nivel;
        Humano = new Jogador { Nome = nome };
        int mem = nivel == "Facil" ? 1 : 3;
        Maquina = new AIPlayer(mem);
        Deck = GerarDeck(tamanho);
        congeladas = new bool[tamanho];
        cronometro = Stopwatch.StartNew();
        _tempoInicioJogada = DateTime.UtcNow;
    }

    private List<Carta> GerarDeck(int tam) {
        var lista = new List<Carta>();
        int grupo = Nivel == "Facil" ? 2 : 3;
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
        if (especiaisUsados >= MAX_ESPECIAIS) return;
        if (Deck[pos].Encontrada || Deck[pos].Visivel) return;
        especiaisUsados++;
        congeladas[pos] = true;
    }

    public Estado ObterEstado() {
        bool finalizado = gruposFormados == Deck.Count / (Nivel == "Facil" ? 2 : 3);

        // salvar a pontuação apenas uma vez ao final do jogo
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
        }

        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;
        return new Estado {
            Cartas = Deck,
            Finalizado = gruposFormados == Deck.Count / (Nivel == "Facil" ? 2 : 3),
            Modo = Modo,
            Nivel = Nivel,
            Pontuacao = Humano.Pontos,
            DuracaoSec = (int)cronometro.Elapsed.TotalSeconds,
            EspeciaisRestantes = MAX_ESPECIAIS - especiaisUsados,
            DicasRestantes = MAX_DICAS - dicasUsadas,
            CooldownDicaSec = dicasUsadas >= MAX_DICAS ? 0 :
                Math.Max(0, DICA_COOLDOWN_SEC - (int)segundosDesdeUltima),
            CartasCongeladas = congeladas
        };
    }

    public Estado AbrirCartaHumano(int pos) {
        if (congeladas[pos]) {
            congeladas[pos] = false;
            return ObterEstado();
        }

        var cartasAbertasAtualmente = Deck
            .Where(c => c.Visivel && !c.Encontrada)
            .ToList();

        if (cartasAbertasAtualmente.Count == 0) {
            _tempoInicioJogada = DateTime.UtcNow; // Inicia a contagem do tempo para a jogada
        }

         AbrirCarta(Humano, pos); // Adiciona 200 pontos por carta virada aqui

        int req = Nivel == "Facil" ? 2 : 3;

        var abertas = Deck
            .Select((c, i) => new { c, i })
            .Where(x => x.c.Visivel && !x.c.Encontrada)
            .Select(x => x.i)
            .ToList();

        if (abertas.Count == req) {
            var valores = abertas.Select(i => Deck[i].Valor).ToList();
            if (valores.All(v => v == valores[0])) {
                foreach (var i in abertas)
                    Deck[i].Encontrada = true;

                RegistrarGrupoFormado();
                
                // Calcular pontuação baseada no tempo
                var tempoResposta = (DateTime.UtcNow - _tempoInicioJogada).TotalSeconds;
                int pontosGanhosPorAcerto = 500; // Pontuação base por acerto

                if (tempoResposta <= 2) {
                    pontosGanhosPorAcerto += 1000; // Bônus por tempo <= 2 segundos
                } else if (tempoResposta <= 4) {
                    pontosGanhosPorAcerto += 500; // Bônus por tempo <= 4 segundos
                } else if (tempoResposta <= 6) {
                    pontosGanhosPorAcerto += 200; // Bônus por tempo <= 6 segundos
                }
                // Se for mais de 6 segundos, nenhum bônus extra é adicionado (pontosGanhosPorAcerto permanece 500)

                Humano.Pontos += pontosGanhosPorAcerto;
                Console.WriteLine($"Grupo formado! Pontos ganhos: {pontosGanhosPorAcerto}. Pontuação total: {Humano.Pontos}");
            } else {
                foreach (var i in abertas)
                    Deck[i].Visivel = false; // Fecha as cartas erradas
                Humano.Pontos = Math.Max(0, Humano.Pontos - 10); // Penalidade por errar DE -10 PONTOS
                Console.WriteLine($"Combinação errada! Pontos perdidos: 10. Pontuação total: {Humano.Pontos}"); 
            }
        }

        return ObterEstado();
}

    private void AbrirCarta(Jogador j, int pos) {
        var carta = Deck[pos];
        if (carta.Visivel || carta.Encontrada) return;
        carta.Visivel = true;
        j.Pontos += 30; //30 PONTOS A CADA CARTA QUE FOR ABERTA

        if (j == Maquina)
            Maquina.Lembrar(pos, carta.Valor);
    }

    public void RegistrarGrupoFormado() => gruposFormados++;

    public Estado JogadaIA_AbrirCartas() {
        int req = Nivel == "Facil" ? 2 : 3;
        PosicoesIASelecionadas.Clear();

        foreach (var carta in Deck)
            if (carta.Visivel && !carta.Encontrada)
                carta.Visivel = false;

        int tentativas = 0, maxTentativas = 200;
        while (PosicoesIASelecionadas.Count < req && tentativas < maxTentativas) {
            int pos = Maquina.EscolherPosicao(Deck);
            tentativas++;

            if (!Deck[pos].Visivel && !Deck[pos].Encontrada && !PosicoesIASelecionadas.Contains(pos)) {
                Deck[pos].Visivel = true;
                Maquina.Lembrar(pos, Deck[pos].Valor);
                PosicoesIASelecionadas.Add(pos);
                Console.WriteLine($"IA abriu posição {pos} -> {Deck[pos].Valor}");
            }
        }

        return ObterEstado();
    }

    public Estado JogadaIA_Resolver() {
        int req = Nivel == "Facil" ? 2 : 3;
        var valores = PosicoesIASelecionadas.Select(i => Deck[i].Valor).ToList();
        bool todasIguais = valores.Count == req && valores.All(v => v == valores[0]);

        if (todasIguais) {
            foreach (var i in PosicoesIASelecionadas)
                Deck[i].Encontrada = true;
            RegistrarGrupoFormado();
            Console.WriteLine("IA acertou o grupo!");
        }
        else
        {
            foreach (var i in PosicoesIASelecionadas)
                Deck[i].Visivel = false;
            Console.WriteLine("IA errou o grupo.");
        }

        PosicoesIASelecionadas.Clear();
        return ObterEstado();
    }

    public Estado UsarDica() {
        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;
        if (dicasUsadas >= MAX_DICAS)
            throw new InvalidOperationException("Limite de 3 dicas atingido.");
        if (segundosDesdeUltima < DICA_COOLDOWN_SEC)
            throw new InvalidOperationException($"Aguarde {DICA_COOLDOWN_SEC - (int)segundosDesdeUltima}s antes de nova dica.");

        dicasUsadas++;
        ultimaDica = agora;
        var rnd = new Random();
        var fechadas = Deck.Select((c, i) => (c, i))
            .Where(x => !x.c.Visivel && !x.c.Encontrada)
            .OrderBy(_ => rnd.Next())
            .Take(2).ToList();
        foreach (var (c, _) in fechadas)
            c.Visivel = true;

        return ObterEstado();
    }
}
