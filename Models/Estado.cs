using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace server.Models;
public class Estado {
    [JsonPropertyName("cartas")]
    public List<Carta> Cartas { get; init; }
    [JsonPropertyName("finalizado")]
    public bool Finalizado { get; init; }
    [JsonPropertyName("modo")]
    public string Modo { get; init; }
    [JsonPropertyName("nivel")]
    public string Nivel { get; init; }
    [JsonPropertyName("pontuacao")]
    public int Pontuacao { get; init; }
    [JsonPropertyName("duracaoSec")]
    public int DuracaoSec { get; init; }
    [JsonPropertyName("especiaisRestantes")]
    public int EspeciaisRestantes { get; init; }
    [JsonPropertyName("dicasRestantes")]
    public int DicasRestantes { get; init; }
    [JsonPropertyName("cooldownDicaSec")]
    public int CooldownDicaSec { get; init; }
    [JsonPropertyName("cartasCongeladas")]
    public bool[] CartasCongeladas { get; init; }
}