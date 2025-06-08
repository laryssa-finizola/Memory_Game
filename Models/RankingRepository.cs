using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace server.Models {
    public class RankingEntry {
        public string Nome { get; set; }
        public string Modo { get; set; }
        public string Nivel { get; set; }
        public int Pontuacao { get; set; }
        public string DataHora { get; set; }
    }

    public class RankingRepository {
        private readonly string _connectionString = "Data Source=ranking.db";

        // criação automática da tabela do banco SQLite
        public RankingRepository() {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Ranking (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nome TEXT NOT NULL,
                    Modo TEXT NOT NULL,
                    Nivel TEXT NOT NULL,
                    Pontuacao INTEGER NOT NULL,
                    DataHora TEXT NOT NULL
                );
            ";
            command.ExecuteNonQuery();
        }

        public void SalvarPontuacao(RankingEntry entry) {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Ranking (Nome, Modo, Nivel, Pontuacao, DataHora)
                VALUES ($nome, $modo, $nivel, $pontuacao, $dataHora);
            ";
            command.Parameters.AddWithValue("$nome", entry.Nome);
            command.Parameters.AddWithValue("$modo", entry.Modo);
            command.Parameters.AddWithValue("$nivel", entry.Nivel);
            command.Parameters.AddWithValue("$pontuacao", entry.Pontuacao);
            command.Parameters.AddWithValue("$dataHora", entry.DataHora);
            command.ExecuteNonQuery();
        }

        public List<RankingEntry> ObterTop5() {
            var top5 = new List<RankingEntry>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Nome, Modo, Nivel, Pontuacao, DataHora
                FROM Ranking
                ORDER BY Pontuacao DESC, DataHora DESC
                LIMIT 5;
            ";

            using var reader = command.ExecuteReader();
            while (reader.Read()) {
                top5.Add(new RankingEntry{
                    Nome = reader.GetString(0),
                    Modo = reader.GetString(1),
                    Nivel = reader.GetString(2),
                    Pontuacao = reader.GetInt32(3),
                    DataHora = reader.GetString(4)
                });
            }

            return top5;
        }
    }
}
