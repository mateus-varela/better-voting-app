using System;
using System.Data.Common;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using Npgsql;
using Confluent.Kafka;

namespace Worker
{
    public class Program
    {
        private static readonly string KafkaTopic = "voting-app-topic";
        private static IConsumer<string, string> _consumer;
        private static NpgsqlConnection _pgsqlConnection;

        public static int Main(string[] args)
        {
            try
            {
                _pgsqlConnection = OpenDbConnection();
                var kafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVER");

                var keepAliveCommand = _pgsqlConnection.CreateCommand();
                keepAliveCommand.CommandText = "SELECT 1";

                SetupKafkaConsumer(kafkaBootstrapServers);

                Console.WriteLine("Worker started. Press Ctrl+C to exit.");

                while (true)
                {
                    Thread.Sleep(100);

                    ConsumeMessage();

                    keepAliveCommand.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Exception in main loop: {ex}");
                return 1;
            }
            finally
            {
                _consumer?.Close();
                _pgsqlConnection?.Close();
            }
        }

        private static void SetupKafkaConsumer(string bootstrapServers)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = "worker-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                SecurityProtocol = SecurityProtocol.SaslPlaintext,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = Environment.GetEnvironmentVariable("KAFKA_USERNAME"),
                SaslPassword = Environment.GetEnvironmentVariable("KAFKA_PASSWORD"),
            };

            Console.WriteLine($"Kafka Bootstrap Servers: {bootstrapServers}");
            Console.WriteLine($"Kafka Username: {Environment.GetEnvironmentVariable("KAFKA_USERNAME")}");

            _consumer = new ConsumerBuilder<string, string>(config).Build();
            _consumer.Subscribe(KafkaTopic);

            Console.WriteLine("Kafka consumer configured and subscribed to topic.");
        }

        private static void ConsumeMessage()
        {
            try
            {
                var consumeResult = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (consumeResult != null)
                {
                    Console.WriteLine($"Consumed message: {consumeResult.Message.Value}");
                    var vote = JsonConvert.DeserializeObject<Vote>(consumeResult.Message.Value);
                    UpdateVote(_pgsqlConnection, vote.voter_id, vote.vote);
                }
            }
            catch (JsonReaderException ex)
            {
                Console.Error.WriteLine($"Error parsing JSON message: {ex}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error consuming message: {ex}");
            }
        }

        private static void UpdateVote(NpgsqlConnection connection, string voterId, string vote)
        {
            var command = connection.CreateCommand();
            try
            {
                command.CommandText = "INSERT INTO votes (id, vote) VALUES (@id, @vote)";
                command.Parameters.AddWithValue("@id", voterId);
                command.Parameters.AddWithValue("@vote", vote);
                command.ExecuteNonQuery();
                Console.WriteLine($"Vote updated for ID: {voterId}");
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                // Handle duplicate key violation
                command.CommandText = "UPDATE votes SET vote = @vote WHERE id = @id";
                command.ExecuteNonQuery();
                Console.WriteLine($"Vote updated (duplicate) for ID: {voterId}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error updating vote: {ex}");
            }
            finally
            {
                command.Dispose();
            }
        }

        private static NpgsqlConnection OpenDbConnection()
        {
            var host = Environment.GetEnvironmentVariable("DB_HOST");
            var username = Environment.GetEnvironmentVariable("DB_USERNAME");
            var password = Environment.GetEnvironmentVariable("DB_PASSWORD");
            var connectionString = $"Server={host};Username={username};Password={password};";

            NpgsqlConnection connection = null;

            while (true)
            {
                try
                {
                    connection = new NpgsqlConnection(connectionString);
                    connection.Open();
                    Console.WriteLine("Connected to PostgreSQL database");
                    break;
                }
                catch (SocketException)
                {
                    Console.Error.WriteLine("Socket exception: Waiting for db");
                    Thread.Sleep(1000);
                }
                catch (NpgsqlException ex)
                {
                    Console.Error.WriteLine($"Npgsql exception: Waiting for db: {ex}");
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"General exception: Waiting for db: {ex}");
                    Thread.Sleep(1000);
                }
            }

            var command = connection.CreateCommand();
            command.CommandText = @"CREATE TABLE IF NOT EXISTS votes (
                                        id VARCHAR(255) PRIMARY KEY,
                                        vote VARCHAR(255) NOT NULL
                                    )";
            command.ExecuteNonQuery();

            return connection;
        }
    }

    public class Vote
    {
        public string vote { get; set; }
        public string voter_id { get; set; }
    }
}
