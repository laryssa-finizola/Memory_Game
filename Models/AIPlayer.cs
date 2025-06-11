using System;
using System.Collections.Generic;
using System.Linq;

namespace server.Models;
public class AIPlayer : Jogador {
    private readonly int MemorySize;
    private readonly Queue<(int pos, string val)> memoria;
    private Jogo _jogoRef; // Referência ao objeto Jogo para acessar RequisitoGrupoPorValor

    public AIPlayer(int memorySize) {
        Nome = "Máquina";
        MemorySize = memorySize;
        memoria = new Queue<(int,string)>();
    }

    // Método para definir a referência ao objeto Jogo após a instanciação
    public void SetJogoReference(Jogo jogo)
    {
        _jogoRef = jogo;
    }

    public int EscolherPosicao(List<Carta> deck) {
        // Passo 1: Tentar encontrar cartas que, combinadas com as da memória, formam um grupo completo
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

        // Tenta formar um grupo com base nas cartas já "conhecidas" na memória
        foreach (var entry in knownCards) {
            // Verifica se a referência ao jogo existe e se o requisito de grupo para o valor da carta é conhecido
            if (_jogoRef != null && _jogoRef.RequisitoGrupoPorValor.TryGetValue(entry.Key, out int requiredCount))
            {
                // Se a IA tem posições suficientes na memória para um par/trio/quadra completo
                if (entry.Value.Count >= requiredCount) {
                    // Retorna a primeira posição para virar
                    return entry.Value.First();
                }
            }
        }

        // Passo 2: Se não encontrou um grupo completo na memória, virar uma carta aleatória que ainda não está visível/encontrada.
        return EscolherPosicaoAleatoriaNaoAberta(deck);
    }

    private int EscolherPosicaoAleatoriaNaoAberta(List<Carta> deck) {
        var rnd = new Random();
        int pos;
        // Percorre aleatoriamente até encontrar uma posição que não está visível e não foi encontrada
        do {
            pos = rnd.Next(deck.Count);
        } while (deck[pos].Visivel || deck[pos].Encontrada); 
        return pos;
    }

    public void Lembrar(int pos, string val) {
        // Remove entradas antigas da memória se o tamanho for excedido
        if (memoria.Count >= MemorySize) memoria.Dequeue(); 
        // Adiciona a nova carta à memória
        memoria.Enqueue((pos,val));
    }
}