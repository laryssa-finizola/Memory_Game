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
