using System;
using System.Collections.Generic;
using System.Linq;

namespace server.Models;
public class AIPlayer : Jogador {
    private readonly int MemorySize;
    private readonly Queue<(int pos, string val)> memoria;

    public AIPlayer(int memorySize) {
        Nome = "Máquina";
        MemorySize = memorySize;
        memoria = new Queue<(int,string)>();
    }

    public int EscolherPosicao(List<Carta> deck) {
        // Passo 1: Tentar encontrar um par/trio completo na memória que ainda não está visível/encontrado
        var knownCards = new Dictionary<string, List<int>>();
        foreach (var (pos, val) in memoria) {
            // Apenas considera cartas que NÃO ESTÃO VISÍVEIS e NÃO FORAM ENCONTRADAS
            if (!deck[pos].Visivel && !deck[pos].Encontrada) {
                if (!knownCards.ContainsKey(val)) {
                    knownCards[val] = new List<int>();
                }
                knownCards[val].Add(pos);
            }
        }

        // Tenta formar um par/trio com base nas cartas já "conhecidas" na memória
        foreach (var entry in knownCards) {
            int requiredCount = (MemorySize == 1) ? 2 : 3; // Adapta para Fácil (2) ou Difícil (3)
            if (entry.Value.Count >= requiredCount) {
                // Se a IA tem posições suficientes na memória para um par/trio, ela vira a primeira delas.
                return entry.Value.First();
            }
        }

        // Passo 2: Se não encontrou um par/trio completo na memória, virar uma carta aleatória que ainda não está visível/encontrada.
        return EscolherPosicaoAleatoriaNaoAberta(deck);
    }

    private int EscolherPosicaoAleatoriaNaoAberta(List<Carta> deck) {
        var rnd = new Random();
        int pos;
        // Percorre aleatoriamente até encontrar uma posição que não está visível e não foi encontrada
        do {
            pos = rnd.Next(deck.Count);
        } while (deck[pos].Visivel || deck[pos].Encontrada); //
        return pos;
    }


    public void Lembrar(int pos, string val) {
        // Remove entradas antigas da memória se o tamanho for excedido
        if (memoria.Count >= MemorySize) memoria.Dequeue(); // Mudado para >= para evitar exceder o limite
        // Adiciona a nova carta à memória
        memoria.Enqueue((pos,val));
    }
}