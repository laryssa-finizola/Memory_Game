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
        Console.WriteLine($"[BACKEND LOG] Embaralhar: Cartas embaralhadas."); // Adicionado log
    }

    public void CongelarCarta(int pos) {
        if (especiaisUsados >= MAX_ESPECIAIS) return;
        if (Deck[pos].Encontrada || Deck[pos].Visivel) return;
        especiaisUsados++;
        congeladas[pos] = true;
        Console.WriteLine($"[BACKEND LOG] Congelar: Carta na posição {pos} congelada."); // Adicionado log
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
            Console.WriteLine($"[BACKEND LOG] Jogo finalizado! Pontuação salva para {Humano.Nome}."); // Adicionado log
        }

        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;

        // Adiciona um log para o estado das cartas visíveis e não encontradas
        var visibleButNotFound = Deck.Select((c, i) => new { c, i })
                                     .Where(x => x.c.Visivel && !x.c.Encontrada)
                                     .Select(x => $"pos {x.i}: {x.c.Valor}")
                                     .ToList();
        Console.WriteLine($"[BACKEND LOG] ObterEstado: Cartas visíveis e não encontradas: [{string.Join(", ", visibleButNotFound)}]");

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
        Console.WriteLine($"[BACKEND LOG] AbrirCartaHumano: Tentativa de abrir carta na posição {pos}."); // Log inicial
        if (congeladas[pos]) {
            congeladas[pos] = false;
            Console.WriteLine($"[BACKEND LOG] AbrirCartaHumano: Carta {pos} estava congelada. Descongelando."); // Adicionado log
            return ObterEstado();
        }

        if (Deck[pos].Visivel || Deck[pos].Encontrada) {
            Console.WriteLine($"[BACKEND LOG] AbrirCartaHumano: Carta na posição {pos} já visível ou encontrada. Não virando."); // Adicionado log
            return ObterEstado();
        }

        if (_humanOpenedCards.Count == 0) {
            // Antes de limpar, loga as cartas que estavam abertas do turno anterior se houver
            if (Deck.Any(c => c.Visivel && !c.Encontrada)) {
                Console.WriteLine($"[BACKEND LOG] AbrirCartaHumano: Limpando cartas visíveis anteriores antes de novo turno humano.");
                foreach(var carta in Deck) {
                    if (carta.Visivel && !carta.Encontrada) {
                        carta.Visivel = false; // Garante que as cartas viradas do turno anterior sejam fechadas.
                    }
                }
            }
            _tempoInicioJogada = DateTime.UtcNow;
            Console.WriteLine($"[BACKEND LOG] AbrirCartaHumano: Novo turno humano iniciado. _humanOpenedCards.Count = 0.");
        }

        Deck[pos].Visivel = true;
        Humano.Pontos += 30; 
        _humanOpenedCards.Add(pos);
        Console.WriteLine($"[BACKEND LOG] AbrirCartaHumano: Carta {pos} virada para CIMA. Valor: {Deck[pos].Valor}. Pontos: {Humano.Pontos}."); // Adicionado log
        Console.WriteLine($"[BACKEND LOG] AbrirCartaHumano: _humanOpenedCards agora contém: [{string.Join(", ", _humanOpenedCards)}]"); // Adicionado log

        return ObterEstado();
    }

    public Estado ProcessarJogadaHumano()
    {
        Console.WriteLine($"[BACKEND LOG] ProcessarJogadaHumano: Iniciando processamento de jogada humana."); // Log inicial
        int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);

        if (_humanOpenedCards.Count != req)
        {
            Console.WriteLine($"[BACKEND LOG] ProcessarJogadaHumano: Número incorreto de cartas abertas ({_humanOpenedCards.Count} vs {req}). Retornando estado atual."); // Adicionado log
            return ObterEstado();
        }

        var openedCardsValues = new List<string>();
        foreach (int pos in _humanOpenedCards)
        {
            openedCardsValues.Add(Deck[pos].Valor);
        }
        
        bool allMatch = openedCardsValues.All(v => v == openedCardsValues[0]);
        Console.WriteLine($"[BACKEND LOG] ProcessarJogadaHumano: Cartas abertas para verificação: [{string.Join(", ", _humanOpenedCards.Select(p => $"pos {p}: {Deck[p].Valor}"))}]. Correspondem? {allMatch}"); // Adicionado log

        if (allMatch)
        {
            foreach (var pos in _humanOpenedCards)
            {
                Deck[pos].Encontrada = true;
            }

            RegistrarGrupoFormado();

            var tempoResposta = (DateTime.UtcNow - _tempoInicioJogada).TotalSeconds;
            int pontosGanhosPorAcerto = 500; 

            if (tempoResposta <= 2)
            {
                pontosGanhosPorAcerto += 1000; 
            }
            else if (tempoResposta <= 4)
            {
                pontosGanhosPorAcerto += 500; 
            }
            else if (tempoResposta <= 6)
            {
                pontosGanhosPorAcerto += 200; 
            }

            Humano.Pontos += pontosGanhosPorAcerto;
            Console.WriteLine($"[BACKEND LOG] Grupo formado! Pontos ganhos: {pontosGanhosPorAcerto}. Pontuação total: {Humano.Pontos}");
        }
        else
        {
            foreach (var pos in _humanOpenedCards)
            {
                Deck[pos].Visivel = false; // Fecha as cartas erradas
            }
            Humano.Pontos = Math.Max(0, Humano.Pontos - 10); 
            Console.WriteLine($"[BACKEND LOG] Combinação errada! Pontos perdidos: 10. Pontuação total: {Humano.Pontos}");
        }

        _humanOpenedCards.Clear(); // Limpa para o próximo turno
        Console.WriteLine($"[BACKEND LOG] ProcessarJogadaHumano: _humanOpenedCards limpas. Turno humano concluído."); // Adicionado log

        return ObterEstado();
    }


    private void AbrirCarta(Jogador j, int pos) { // Este método parece não estar sendo usado pelo fluxo principal do humano, mas é bom logar nele também.
        Console.WriteLine($"[BACKEND LOG] AbrirCarta (genérico): Tentativa de abrir carta na posição {pos} por {j.Nome}.");
        var carta = Deck[pos];
        if (carta.Visivel || carta.Encontrada) {
            Console.WriteLine($"[BACKEND LOG] AbrirCarta (genérico): Carta {pos} já visível ou encontrada. Não virando.");
            return;
        }
        carta.Visivel = true;
        j.Pontos += 30; //30 PONTOS A CADA CARTA QUE FOR ABERTA
        Console.WriteLine($"[BACKEND LOG] AbrirCarta (genérico): Carta {pos} virada para CIMA por {j.Nome}. Valor: {Deck[pos].Valor}.");


        if (j == Maquina)
            Maquina.Lembrar(pos, carta.Valor);
    }

    public void RegistrarGrupoFormado() {
        gruposFormados++;
        Console.WriteLine($"[BACKEND LOG] Grupo formado! Total de grupos: {gruposFormados}."); // Adicionado log
    }

    public Estado JogadaIA_AbrirCartas() {
        Console.WriteLine($"[BACKEND LOG] JogadaIA_AbrirCartas: Iniciando abertura de cartas pela IA."); // Log inicial
        int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);
        PosicoesIASelecionadas.Clear(); // Limpa as seleções anteriores da IA

        // Garante que as cartas visíveis de turnos anteriores (humanos ou IA) sejam fechadas antes da IA abrir novas
        Console.WriteLine($"[BACKEND LOG] JogadaIA_AbrirCartas: Fechando cartas visíveis não encontradas antes da IA jogar.");
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
                Console.WriteLine($"[BACKEND LOG] IA abriu posição {pos} -> {Deck[pos].Valor}");
            }
        }
        Console.WriteLine($"[BACKEND LOG] JogadaIA_AbrirCartas: IA abriu {PosicoesIASelecionadas.Count} cartas: [{string.Join(", ", PosicoesIASelecionadas)}]."); // Adicionado log
        return ObterEstado();
    }

    public Estado JogadaIA_Resolver() {
        Console.WriteLine($"[BACKEND LOG] JogadaIA_Resolver: Iniciando resolução de jogada pela IA."); // Log inicial
        int req = Nivel == "Facil" ? 2 : (Nivel == "Medio" ? 3 : 2);
        var valores = PosicoesIASelecionadas.Select(i => Deck[i].Valor).ToList();
        bool todasIguais = valores.Count == req && valores.All(v => v == valores[0]);

        if (todasIguais) {
            foreach (var i in PosicoesIASelecionadas)
                Deck[i].Encontrada = true;
            RegistrarGrupoFormado();
            Console.WriteLine("[BACKEND LOG] IA acertou o grupo!"); // Log no backend
        }
        else
        {
            foreach (var i in PosicoesIASelecionadas)
                Deck[i].Visivel = false;
            Console.WriteLine("[BACKEND LOG] IA errou o grupo."); // Log no backend
        }

        PosicoesIASelecionadas.Clear();
        Console.WriteLine($"[BACKEND LOG] JogadaIA_Resolver: Cartas da IA processadas. _PosicoesIASelecionadas limpas."); // Adicionado log
        return ObterEstado();
    }

    public Estado UsarDica() {
        Console.WriteLine($"[BACKEND LOG] UsarDica: Tentativa de usar dica."); // Log inicial
        var agora = DateTime.UtcNow;
        var segundosDesdeUltima = (agora - ultimaDica).TotalSeconds;
        if (dicasUsadas >= MAX_DICAS) {
            Console.WriteLine($"[BACKEND LOG] UsarDica: Limite de 3 dicas atingido ({dicasUsadas})."); // Adicionado log
            throw new InvalidOperationException("Limite de 3 dicas atingido.");
        }
        if (segundosDesdeUltima < DICA_COOLDOWN_SEC) {
            Console.WriteLine($"[BACKEND LOG] UsarDica: Cooldown ativo. Aguarde {DICA_COOLDOWN_SEC - (int)segundosDesdeUltima}s."); // Adicionado log
            throw new InvalidOperationException($"Aguarde {DICA_COOLDOWN_SEC - (int)segundosDesdeUltima}s antes de nova dica.");
        }

        dicasUsadas++;
        ultimaDica = agora;
        var rnd = new Random();
        var fechadas = Deck.Select((c, i) => (c, i))
            .Where(x => !x.c.Visivel && !x.c.Encontrada)
            .OrderBy(_ => rnd.Next())
            .Take(2).ToList();
        
        if (fechadas.Count == 0) {
            Console.WriteLine($"[BACKEND LOG] UsarDica: Não há cartas fechadas para dar dica.");
            // Você pode decidir como lidar com isso. Talvez não contar a dica, ou lançar outra exceção.
            // Por enquanto, vamos retornar o estado sem virar nada e ainda assim usar a dica (se passou pelos checks iniciais)
        } else {
            foreach (var (c, pos) in fechadas) { // Percorre a lista com a posição também para logar
                c.Visivel = true;
                Console.WriteLine($"[BACKEND LOG] UsarDica: Revelando carta na posição {pos} (Valor: {c.Valor})."); // Adicionado log
            }
        }
        Console.WriteLine($"[BACKEND LOG] UsarDica: Dica usada. Dicas restantes: {MAX_DICAS - dicasUsadas}."); // Adicionado log
        return ObterEstado();
    }
}