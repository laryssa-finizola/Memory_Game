using server.Models;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(); 


builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
    )
);
builder.Services.AddSingleton<Repositorio>();

var app = builder.Build();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.UseCors();
app.UseDefaultFiles();   
app.UseStaticFiles();   

// ================== ROTAS PRINCIPAIS ==================


app.MapGet("/api/jogo/iniciar", (string nome, string modo, string nivel, int tamanho, Repositorio repo) => {
    var jogo = repo.CriarJogo(nome, modo, nivel, tamanho);
    jogo.Maquina.SetJogoReference(jogo); 
    return Results.Json(jogo.ObterEstado());
});


app.MapGet("/api/jogo/estado", (Repositorio repo) => {
    return Results.Json(repo.JogoAtual.ObterEstado());
});

app.MapPost("/api/jogo/jogada/abrir", (Jogada jogada, Repositorio repo) =>
    Results.Json(repo.ProcessarHumano(jogada.Posicao))
);

app.MapPost("/api/jogo/jogada/verificar", (Repositorio repo) =>
    Results.Json(repo.VerificarJogadaHumano())
);


app.MapPost("/api/jogo/ia/abrir", (Repositorio repo) => {
    var estado = repo.JogoAtual.JogadaIA_AbrirCartas();
    return Results.Json(estado);
});

app.MapPost("/api/jogo/ia/resolver", (Repositorio repo) => {
    var estado = repo.JogoAtual.JogadaIA_Resolver();
    return Results.Json(estado);
});

// ================== PODERES ==================

app.MapPost("/api/jogo/poder/embaralhar", (Repositorio repo) => {
    repo.JogoAtual.EmbaralharBaixo();
    return Results.Json(repo.JogoAtual.ObterEstado());
});

app.MapPost("/api/jogo/poder/congelar", (Jogada jogada, Repositorio repo) => {
    repo.JogoAtual.CongelarCarta(jogada.Posicao);
    return Results.Json(repo.JogoAtual.ObterEstado());
});

app.MapPost("/api/jogo/poder/dica", (Repositorio repo) => {
    try {
        var estado = repo.JogoAtual.UsarDica();
        return Results.Json(estado);
    }
    catch (InvalidOperationException ex) {
        return Results.BadRequest(new { erro = ex.Message });
    }
});

app.Run();