using System;
using System.Collections.Generic;
using System.Linq;

namespace server.Models;
public class AIPlayer : Jogador {
    private readonly int MemorySize;
    private readonly Queue<(int pos, string val)> memoria;
    private Jogo _jogoRef; 
    public AIPlayer(int memorySize) {
        Nome = "Máquina";
        MemorySize = memorySize;
        memoria = new Queue<(int,string)>();
    }
    public void SetJogoReference(Jogo jogo)
    {
        _jogoRef = jogo;
    }

    public int EscolherPosicao(List<Carta> deck) {
        var knownCards = new Dictionary<string, List<int>>();
        foreach (var (pos, val) in memoria) {
            if (!deck[pos].Visivel && !deck[pos].Encontrada) {
                if (!knownCards.ContainsKey(val)) {
                    knownCards[val] = new List<int>();
                }
                knownCards[val].Add(pos);
            }
        }

        foreach (var entry in knownCards) {
            if (_jogoRef != null && _jogoRef.RequisitoGrupoPorValor.TryGetValue(entry.Key, out int requiredCount))
            {
                if (entry.Value.Count >= requiredCount) {
                    return entry.Value.First();
                }
            }
        }

        return EscolherPosicaoAleatoriaNaoAberta(deck);
    }

    private int EscolherPosicaoAleatoriaNaoAberta(List<Carta> deck) {
        var rnd = new Random();
        int pos;
        do {
            pos = rnd.Next(deck.Count);
        } while (deck[pos].Visivel || deck[pos].Encontrada);
        return pos;
    }

    public void Lembrar(int pos, string val) {
        if (memoria.Count >= MemorySize) memoria.Dequeue();
        memoria.Enqueue((pos,val));
    }
}