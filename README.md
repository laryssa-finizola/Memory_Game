# Memory Game - Web Application

This repository contains a full-stack web application for a classic Memory Game. The project is built with a C# and ASP.NET Core backend that serves a RESTful API, and a vanilla JavaScript frontend that consumes this API to provide an interactive user experience.

The game features a card-matching challenge where players flip cards to find pairs. The backend manages the game state, validates moves, and maintains a persistent player ranking using a SQLite database.

## Core Technologies

### Backend
* **Framework:** C# with ASP.NET Core Web API (.NET 9).
* **Database:** SQLite for persistent storage of the player ranking.
* **API Architecture:** RESTful endpoints to manage game state and rankings.
* **State Management:** A Singleton service (`Repositorio.cs`) holds the game state in memory.
* **Object-Oriented Design:** The core logic is encapsulated in models for `Jogo` (Game), `Jogador` (Player), and `Carta` (Card).

### Frontend
* **Languages:** HTML, CSS, and Vanilla JavaScript (no frameworks).
* **API Communication:** Uses the `fetch` API to communicate with the C# backend to get game data and post scores.
* **Dynamic UI:** The game board and state are dynamically rendered and updated in the DOM using JavaScript.

## Features

* **Full-Stack Application:** A C# server hosts both the API and the static frontend files.
* **AI Opponent:** Play against an AI that remembers previously revealed cards to make intelligent moves.
* **Persistent Ranking:** Game results (wins/losses/draws) are saved to a local SQLite database and displayed in a Top 10 leaderboard.
* **Dynamic UI:** The frontend uses pure JavaScript to dynamically build the game board, handle card flip animations, and update scores by fetching data from the backend API.

## Project Architecture

This project follows a simple and effective client-server model:

1.  **Backend (Server):** The ASP.NET Core application acts as the web server.
    * It serves the static files (HTML, JS, images) from the `wwwroot` directory.
    * It exposes a JSON API (defined in `RankingController.cs`) to manage the game state.
    * All core game logic (shuffling, checking for matches, AI moves) is handled securely on the server in `Jogo.cs`.

2.  **Frontend (Client):** The `index.html` and `jogoFront.js` files run entirely in the user's browser.
    * The `jogoFront.js` script initiates the game by calling `fetch('/iniciar')`.
    * When a player clicks a card, the script sends the move to the server using `fetch('/jogar')`.
    * After each move, the script fetches the latest game state from `fetch('/estado')` and re-renders the board.
    * The ranking is loaded via `fetch('/ranking')`.

### API Endpoints

The core API is defined in `RankingController.cs`:

* `POST /iniciar`: Creates a new game instance with a shuffled deck. Takes a player's name as input.
* `POST /jogar`: Processes a player's move. Takes the selected card index as input.
* `GET /estado`: Returns the current state of the game board (which cards are flipped, matched, etc.).
* `GET /ranking`: Returns a JSON array of the top 10 player scores from the database.

## How to Run

### Prerequisites

* [.NET 9 SDK (or newer)](https://dotnet.microsoft.com/download)
* A web browser

### Running the Application

1.  **Clone the repository:**
    ```bash
    git clone [https://github.com/laryssa-finizola/memory_game.git](https://github.com/laryssa-finizola/memory_game.git)
    cd memory_game
    ```

2.  **Restore .NET dependencies:**
    ```bash
    dotnet restore
    ```

3.  **Run the server:**
    ```bash
    dotnet run
    ```

4.  **Open the game:**
    The console will display the URL where the application is running (e.g., `http://localhost:5123`). Open this URL in your web browser to play the game.

## Deploy grátis no Render (passo a passo)

Você vai criar um **Web Service** no Render apontando para este repositório. O Render vai fazer o build e iniciar o servidor ASP.NET que já entrega o front (`wwwroot`) e a API.

### 1) Pré-requisitos

* Repositório no GitHub com este código (branch `main`)
* Conta no Render (plano Free)

### 2) Criar o serviço

1. No Render, clique em **New +** → **Web Service**.
2. Conecte o GitHub e selecione o seu repositório.
3. Em **Name**, escolha um nome (vai virar parte da URL).
4. Em **Region**, pode deixar a padrão.
5. Em **Branch**, selecione `main`.
6. Em **Runtime**, selecione **Native** (não precisa Docker).

### 3) Configurar build e start

Como este projeto tem uma solution (`MemoriaGameServer.sln`) e um `.csproj`, o mais seguro é publicar o **.csproj**.

* **Build Command**
    * `dotnet publish MemoriaGameServer.csproj -c Release -o out`
* **Start Command**
    * `dotnet out/MemoriaGameServer.dll`

### 4) Variáveis de ambiente

Em **Environment → Add Environment Variable**:

* `ASPNETCORE_ENVIRONMENT` = `Production`

> Obs.: o Render injeta automaticamente a variável `PORT`. Este projeto já lê `PORT` e faz bind em `0.0.0.0:<PORT>`.

### 5) Deploy e validação

Depois do deploy, teste estas URLs:

* **Front:** `https://SEU-SERVICO.onrender.com/`
* **Health:** `https://SEU-SERVICO.onrender.com/health`
* **API (ranking):** `https://SEU-SERVICO.onrender.com/api/Ranking/top5`

Se o front carregar, mas a API falhar, abra **Logs** no Render e me mande as últimas linhas que eu te ajudo a ajustar.

### Nota sobre o free tier

No plano grátis, o serviço pode **“dormir”** após um tempo sem acessos e demorar alguns segundos pra voltar quando alguém abrir (isso é normal). Pra recruiters, geralmente é ok.



## Troubleshooting (Render)

Se o serviço ficar **OFF** / crashando:

* Confira em **Logs** se aparece algo como `Now listening on: http://0.0.0.0:<PORT>`.
* Garanta que o **Start Command** está usando o publish: `dotnet out/MemoriaGameServer.dll`.
* Se o erro for de SQLite/permissão (pasta read-only), como você não precisa persistência, a solução mais simples é aceitar que o `ranking.db` pode ser recriado no runtime (ephemeral). Se precisar, eu ajusto o caminho do SQLite pra uma pasta temporária.

