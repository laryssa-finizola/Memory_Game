// laryssa-finizola/pp/PP-b120e24693914edde64dbdb9581263ca7a04411b/Models/Jogo.cs
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

    // NOVO: Lista para rastrear as cartas abertas pelo humano no turno atual
    private List<int> _humanOpenedCards = new List<int>();


    public Jogo(string nome, string modo, string nivel, int tamanho)
    {
        Modo = modo;
        Nivel = nivel;
        Humano = new Jogador { Nome = nome };
        int mem = Nivel == "Facil" ? 1 : (Nivel == "Medio" ? 3 : 1); // Garante 1 para Facil, 3 para Medio, default 1
        Maquina = new AIPlayer(mem);
        Deck = GerarDeck(tamanho);
        congeladas = new bool[tamanho];
        cronometro = Stopwatch.StartNew();
        _tempoInicioJogada = DateTime.UtcNow;
    }

    private List<Carta> GerarDeck(int tam) {
        var lista = new List<Carta>();
        int grupo = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2); // Garante 2 para Facil, 3 para Medio, default 2
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
        bool finalizado = gruposFormados == Deck.Count / (Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2));

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

    // MODIFICADO: Este método agora SÓ abre a carta e adiciona à lista interna
    public Estado AbrirCartaHumano(int pos) {
        if (congeladas[pos]) {
            congeladas[pos] = false;
            return ObterEstado();
        }

        // Verifica se a carta já está visível ou encontrada antes de abrir
        if (Deck[pos].Visivel || Deck[pos].Encontrada) {
            return ObterEstado(); // Retorna o estado atual sem alterações se a carta já estiver aberta
        }

        // Limpa as cartas abertas anteriormente se este for o início de um novo turno
        // Isso garante que apenas as cartas do turno atual sejam rastreadas
        if (_humanOpenedCards.Count == 0) {
            _tempoInicioJogada = DateTime.UtcNow;
        }

        // Abre a carta
        Deck[pos].Visivel = true;
        Humano.Pontos += 30; // 30 PONTOS A CADA CARTA QUE FOR ABERTA

        _humanOpenedCards.Add(pos);

        // Este método apenas abre uma carta e a adiciona à lista de cartas abertas para o turno atual.
        // A verificação de correspondência e o ajuste de pontuação ocorrerão em uma chamada separada
        // depois que o frontend exibir as cartas por um breve período.
        return ObterEstado();
    }

    // NOVO MÉTODO: Responsável por verificar a jogada humana após as cartas serem viradas
    public Estado ProcessarJogadaHumano()
    {
         int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);

        // Garante que exatamente 'req' cartas sejam abertas pelo humano para uma verificação de correspondência
        if (_humanOpenedCards.Count != req)
        {
            // Isso não deve acontecer se a lógica do frontend estiver correta, mas é bom para robustez.
            // Se menos de 'req' cartas foram abertas, simplesmente retorna o estado atual.
            return ObterEstado();
        }

        var openedCardsValues = new List<string>();
        foreach (int pos in _humanOpenedCards)
        {
            openedCardsValues.Add(Deck[pos].Valor);
        }

        bool allMatch = openedCardsValues.All(v => v == openedCardsValues[0]);

        if (allMatch)
        {
            foreach (var pos in _humanOpenedCards)
            {
                Deck[pos].Encontrada = true;
            }

            RegistrarGrupoFormado();

            // Calcula a pontuação com base no tempo
            var tempoResposta = (DateTime.UtcNow - _tempoInicioJogada).TotalSeconds;
            int pontosGanhosPorAcerto = 500; // Pontuação base para um acerto correto

            if (tempoResposta <= 2)
            {
                pontosGanhosPorAcerto += 1000; // Bônus para <= 2 segundos
            }
            else if (tempoResposta <= 4)
            {
                pontosGanhosPorAcerto += 500; // Bônus para <= 4 segundos
            }
            else if (tempoResposta <= 6)
            {
                pontosGanhosPorAcerto += 200; // Bônus para <= 6 segundos
            }

            Humano.Pontos += pontosGanhosPorAcerto;
            Console.WriteLine($"Grupo formado! Pontos ganhos: {pontosGanhosPorAcerto}. Pontuação total: {Humano.Pontos}");
        }
        else
        {
            foreach (var pos in _humanOpenedCards)
            {
                Deck[pos].Visivel = false; // Fecha as cartas erradas
            }
            Humano.Pontos = Math.Max(0, Humano.Pontos - 10); // Penalidade por erro -10 pontos
            Console.WriteLine($"Combinação errada! Pontos perdidos: 10. Pontuação total: {Humano.Pontos}");
        }

        _humanOpenedCards.Clear(); // Limpa para o próximo turno

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
         int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);
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
        int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);
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