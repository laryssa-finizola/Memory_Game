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
        var dicionario = new Dictionary<string,int>();
        foreach (var (p, v) in memoria)
            if (!deck[p].Encontrada && !deck[p].Visivel)  // Adicione !deck[p].Visivel aqui
                 dicionario[v] = p;

        
        if (dicionario.Count < 2)
            return Aleatorio(deck);

        foreach (var (p, v) in memoria) {
            if (dicionario.ContainsKey(v) && dicionario[v] != p)
                return new Random().NextDouble() < .8 ? p : Aleatorio(deck);
        }
        return Aleatorio(deck);
    }

    private int Aleatorio(List<Carta> deck) {
        var rnd = new Random();
    int pos;
    do {
        pos = rnd.Next(deck.Count);
    } while (deck[pos].Visivel || deck[pos].Encontrada);
    return pos;
    }

    public void Lembrar(int pos, string val) {
        memoria.Enqueue((pos,val));
        if (memoria.Count > MemorySize) memoria.Dequeue();
    }
}