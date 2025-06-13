using System;
using System.Collections.Generic;
using System.Linq;

namespace server.Models;

public class AIPlayer : Jogador
{
    private readonly int MemorySize = 4;
    private readonly Queue<(int pos, string val)> memoria;
    private Jogo? _jogoRef;

    public AIPlayer()
    {
        Nome = "Máquina";
        memoria = new Queue<(int, string)>();
    }

    public void SetJogoReference(Jogo jogo)
    {
        _jogoRef = jogo;
    }

    public int EscolherPosicao(List<Carta> deck)
    {
        var knownCards = new Dictionary<string, List<int>>();
        foreach (var (pos, val) in memoria)
        {
            if (pos >= 0 && pos < deck.Count && !deck[pos].Visivel && !deck[pos].Encontrada && !_jogoRef.congeladas[pos])
            {
                if (!knownCards.ContainsKey(val))
                {
                    knownCards[val] = new List<int>();
                }
                knownCards[val].Add(pos);
            }
        }

        foreach (var entry in knownCards)
        {
            if (_jogoRef != null && _jogoRef.RequisitoGrupoPorValor.TryGetValue(entry.Key, out int requiredCount))
            {
                var unrevealedPositions = entry.Value.Where(p => !deck[p].Visivel && !deck[p].Encontrada && !_jogoRef.congeladas[p]).ToList();

                if (unrevealedPositions.Count >= requiredCount)
                {
                    return unrevealedPositions.OrderBy(p => p).First();
                }
            }
        }
        if (_jogoRef == null) return -1;
        return EscolherPosicaoAleatoriaNaoAberta(deck, _jogoRef.congeladas);
    }

    private int EscolherPosicaoAleatoriaNaoAberta(List<Carta> deck, bool[] congeladas)
    {
        var rnd = new Random();
        var posicoesDisponiveis = new List<int>();
        for (int i = 0; i < deck.Count; i++)
        {
            if (!deck[i].Visivel && !deck[i].Encontrada && !congeladas[i])
            {
                posicoesDisponiveis.Add(i);
            }
        }

        if (posicoesDisponiveis.Any())
        {
            return posicoesDisponiveis[rnd.Next(posicoesDisponiveis.Count)];
        }

        return -1;
    }

    public void Lembrar(int pos, string val)
    {
        memoria.Enqueue((pos, val));
        if (memoria.Count > MemorySize)
        {
            memoria.Dequeue();
        }
    }
}