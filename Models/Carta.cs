namespace server.Models;
public class Carta
{
    public string Valor { get; set; }
    public bool Visivel { get; set; } = false;
    public bool Encontrada { get; set; } = false;
    public bool Especial { get; set; } = false;
}