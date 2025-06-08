using server.Models;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(); // necessário para APIs


// Evita erros de CORS no navegador
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
app.MapControllers(); // ativa os endpoints dos controllers
app.UseCors();
app.UseDefaultFiles();    // permite index.html como padrão
app.UseStaticFiles();     // serve arquivos de wwwroot/

// ================== ROTAS PRINCIPAIS ==================


app.MapGet("/api/jogo/iniciar", (string nome, string modo, string nivel, int tamanho, Repositorio repo) => {
    var jogo = repo.CriarJogo(nome, modo, nivel, tamanho);
    return Results.Json(jogo.ObterEstado());
});


app.MapGet("/api/jogo/estado", (Repositorio repo) => {
    return Results.Json(repo.JogoAtual.ObterEstado());
});

app.MapPost("/api/jogo/jogada", (Jogada jogada, Repositorio repo) =>
    Results.Json(repo.ProcessarHumano(jogada.Posicao))
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