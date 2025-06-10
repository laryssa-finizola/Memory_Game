using System;
using System.Collections.Generic;
using System.Linq;

namespace server.Models;

public class AIPlayer : Jogador {
    private readonly int MemorySize;
    private readonly Queue<(int pos, string val)> memoria;
    private Jogo _jogoRef; 
    private List<Carta> _deckRef; // Adiciona uma referência ao deck completo

    public AIPlayer(int memorySize) {
        Nome = "Máquina";
        MemorySize = memorySize;
        memoria = new Queue<(int,string)>();
    }

    public void SetJogoReference(Jogo jogo)
    {
        _jogoRef = jogo;
        // Quando a referência do jogo é definida, também define a referência para o deck
        _deckRef = jogo.Deck; 
    }

    /// <summary>
    /// A IA escolhe uma posição para virar.
    /// Prioriza correspondências conhecidas, depois posições aleatórias não abertas.
    /// </summary>
    /// <param name="availablePositions">Lista de índices de posições disponíveis para escolha.</param>
    /// <returns>O índice da posição escolhida.</returns>
    public int EscolherPosicao(List<int> availablePositions) { // CORRIGIDO: Agora aceita List<int>
        // Se o deck não foi referenciado corretamente ou não há posições disponíveis,
        // retorna um erro ou uma posição padrão (isso não deveria acontecer com a lógica atual).
        if (_deckRef == null || availablePositions == null || !availablePositions.Any()) {
            // Em um ambiente real, você poderia lançar uma exceção ou logar um erro.
            // Para evitar travamento, retornaremos uma posição padrão ou aleatória se possível.
            // No entanto, a lógica de Jogo.cs já verifica availablePositions.Count.
            return -1; // Indica erro ou nenhuma posição válida
        }

        var knownCards = new Dictionary<string, List<int>>();
        // Popula knownCards apenas com cartas que estão nas availablePositions
        foreach (var (pos, val) in memoria) {
            // Verifica se a carta ainda está fechada e disponível (não encontrada, não visível, não congelada)
            // e se está entre as posições disponíveis para este turno.
            if (availablePositions.Contains(pos) && !_deckRef[pos].Encontrada && !_deckRef[pos].Visivel) {
                if (!knownCards.ContainsKey(val)) {
                    knownCards[val] = new List<int>();
                }
                knownCards[val].Add(pos);
            }
        }

        // Tenta encontrar uma correspondência para o número de cartas necessárias
        // no nível atual do jogo.
        foreach (var entry in knownCards) {
            // Verifica se _jogoRef é null antes de tentar acessá-lo
            if (_jogoRef != null && _jogoRef.RequisitoGrupoPorValor.TryGetValue(entry.Key, out int requiredCount))
            {
                // Se a IA conhece cartas suficientes de um valor para formar um grupo,
                // ela escolhe uma dessas posições.
                if (entry.Value.Count >= requiredCount) {
                    // Retorna a primeira posição conhecida que pode formar um grupo
                    return entry.Value.First(); 
                }
            }
        }

        // Se nenhuma correspondência perfeita for encontrada na memória,
        // escolhe uma posição aleatória entre as disponíveis.
        return EscolherPosicaoAleatoriaNaoAberta(availablePositions);
    }

    /// <summary>
    /// Escolhe uma posição aleatória de uma lista de posições disponíveis.
    /// </summary>
    /// <param name="availablePositions">Lista de índices de posições disponíveis.</param>
    /// <returns>Um índice de posição aleatório.</returns>
    private int EscolherPosicaoAleatoriaNaoAberta(List<int> availablePositions) {
        var rnd = new Random();
        if (availablePositions.Any()) {
            return availablePositions[rnd.Next(availablePositions.Count)];
        }
        // Isso não deveria ser alcançado se availablePositions.Any() foi verificado.
        // Se por algum motivo for, retorna -1 para indicar que nenhuma posição foi encontrada.
        return -1; 
    }

    /// <summary>
    /// Adiciona uma carta à memória da IA.
    /// </summary>
    /// <param name="pos">Posição da carta.</param>
    /// <param name="val">Valor da carta.</param>
    public void Lembrar(int pos, string val) {
        // Remove entradas antigas se a memória estiver cheia
        if (memoria.Count >= MemorySize) memoria.Dequeue();
        // Adiciona a nova carta à memória
        memoria.Enqueue((pos,val));
    }
}
