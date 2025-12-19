using server.Models;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Hosting in platforms like Render/Fly injects a PORT env var.
// When present, ensure Kestrel binds to 0.0.0.0:<PORT>.
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(portEnv, out var port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

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

// Simple health/page route (also helps Render health checks)
app.MapGet("/", () => Results.Redirect("/index.html"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();
app.UseAuthorization();

app.MapControllers(); 

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

// ================== PODERES ===================

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