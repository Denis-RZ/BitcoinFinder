using BitcoinFinderWebServer.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace BitcoinFinderWebServer.Services
{
    public interface IDatabaseService
    {
        Task<DatabaseInstallResponse> InstallDatabaseAsync(DatabaseInstallRequest request);
        Task<bool> TestConnectionAsync(DatabaseConfig config);
        Task<DatabaseInstallResponse> CreateDatabaseAsync(DatabaseConfig config);
        Task<DatabaseInstallResponse> InstallSchemaAsync(DatabaseConfig config);
        Task<DatabaseInstallResponse> InstallSeedDataAsync(DatabaseConfig config);
        Task SaveFoundSeed(string bitcoinAddress, string seedPhrase, string foundAddress, long processed, TimeSpan time);
    }

    public class DatabaseService : IDatabaseService
    {
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(ILogger<DatabaseService> logger)
        {
            _logger = logger;
        }

        public async Task<DatabaseInstallResponse> InstallDatabaseAsync(DatabaseInstallRequest request)
        {
            var response = new DatabaseInstallResponse();
            
            try
            {
                _logger.LogInformation("Начинаем установку базы данных: {DatabaseName}", request.Config.DatabaseName);

                // Тестируем подключение
                if (!await TestConnectionAsync(request.Config))
                {
                    response.Success = false;
                    response.Message = "Не удалось подключиться к серверу базы данных";
                    response.Errors.Add("Проверьте настройки подключения и убедитесь, что сервер доступен");
                    return response;
                }

                // Создаем базу данных
                if (request.CreateDatabase)
                {
                    var createResult = await CreateDatabaseAsync(request.Config);
                    if (!createResult.Success)
                    {
                        response.Success = false;
                        response.Message = "Ошибка создания базы данных";
                        response.Errors.AddRange(createResult.Errors);
                        return response;
                    }
                    response.Warnings.AddRange(createResult.Warnings);
                }

                // Устанавливаем схему
                if (request.InstallSchema)
                {
                    var schemaResult = await InstallSchemaAsync(request.Config);
                    if (!schemaResult.Success)
                    {
                        response.Success = false;
                        response.Message = "Ошибка установки схемы базы данных";
                        response.Errors.AddRange(schemaResult.Errors);
                        return response;
                    }
                    response.Warnings.AddRange(schemaResult.Warnings);
                }

                // Устанавливаем начальные данные
                if (request.InstallSeedData)
                {
                    var seedResult = await InstallSeedDataAsync(request.Config);
                    if (!seedResult.Success)
                    {
                        response.Warnings.Add("Ошибка установки начальных данных: " + seedResult.Message);
                    }
                    else
                    {
                        response.Warnings.AddRange(seedResult.Warnings);
                    }
                }

                response.Success = true;
                response.Message = $"База данных '{request.Config.DatabaseName}' успешно установлена";
                _logger.LogInformation("Установка базы данных завершена успешно: {DatabaseName}", request.Config.DatabaseName);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Неожиданная ошибка при установке базы данных";
                response.Errors.Add(ex.Message);
                _logger.LogError(ex, "Ошибка установки базы данных: {DatabaseName}", request.Config.DatabaseName);
            }

            return response;
        }

        public async Task<bool> TestConnectionAsync(DatabaseConfig config)
        {
            try
            {
                using var connection = new SqlConnection(config.ConnectionString);
                await connection.OpenAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка подключения к базе данных: {DatabaseName}", config.DatabaseName);
                return false;
            }
        }

        public async Task<DatabaseInstallResponse> CreateDatabaseAsync(DatabaseConfig config)
        {
            var response = new DatabaseInstallResponse();
            
            try
            {
                // Подключаемся к master базе для создания новой базы
                var masterConnectionString = config.UseWindowsAuthentication
                    ? $"Server={config.Server},{config.Port};Database=master;Trusted_Connection=true;TrustServerCertificate=true;"
                    : $"Server={config.Server},{config.Port};Database=master;User Id={config.Username};Password={config.Password};TrustServerCertificate=true;";

                using var connection = new SqlConnection(masterConnectionString);
                await connection.OpenAsync();

                // Проверяем, существует ли база данных
                var checkCommand = new SqlCommand(
                    "SELECT COUNT(*) FROM sys.databases WHERE name = @DatabaseName", connection);
                checkCommand.Parameters.AddWithValue("@DatabaseName", config.DatabaseName);
                
                var result = await checkCommand.ExecuteScalarAsync();
                var exists = result != null && (int)result > 0;
                
                if (exists)
                {
                    response.Warnings.Add($"База данных '{config.DatabaseName}' уже существует");
                    response.Success = true;
                    return response;
                }

                // Создаем базу данных
                var createCommand = new SqlCommand(
                    $"CREATE DATABASE [{config.DatabaseName}]", connection);
                await createCommand.ExecuteNonQueryAsync();

                response.Success = true;
                response.Message = $"База данных '{config.DatabaseName}' создана успешно";
                _logger.LogInformation("База данных создана: {DatabaseName}", config.DatabaseName);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Ошибка создания базы данных";
                response.Errors.Add(ex.Message);
                _logger.LogError(ex, "Ошибка создания базы данных: {DatabaseName}", config.DatabaseName);
            }

            return response;
        }

        public async Task<DatabaseInstallResponse> InstallSchemaAsync(DatabaseConfig config)
        {
            var response = new DatabaseInstallResponse();
            
            try
            {
                using var connection = new SqlConnection(config.ConnectionString);
                await connection.OpenAsync();

                // Создаем таблицы
                var schemaScripts = GetSchemaScripts();
                
                foreach (var script in schemaScripts)
                {
                    try
                    {
                        using var command = new SqlCommand(script, connection);
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        response.Warnings.Add($"Ошибка выполнения скрипта: {ex.Message}");
                    }
                }

                response.Success = true;
                response.Message = "Схема базы данных установлена успешно";
                _logger.LogInformation("Схема базы данных установлена: {DatabaseName}", config.DatabaseName);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Ошибка установки схемы базы данных";
                response.Errors.Add(ex.Message);
                _logger.LogError(ex, "Ошибка установки схемы: {DatabaseName}", config.DatabaseName);
            }

            return response;
        }

        public async Task<DatabaseInstallResponse> InstallSeedDataAsync(DatabaseConfig config)
        {
            var response = new DatabaseInstallResponse();
            
            try
            {
                using var connection = new SqlConnection(config.ConnectionString);
                await connection.OpenAsync();

                // Добавляем начальные данные
                var seedScripts = GetSeedScripts();
                
                foreach (var script in seedScripts)
                {
                    try
                    {
                        using var command = new SqlCommand(script, connection);
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        response.Warnings.Add($"Ошибка добавления начальных данных: {ex.Message}");
                    }
                }

                response.Success = true;
                response.Message = "Начальные данные добавлены успешно";
                _logger.LogInformation("Начальные данные добавлены: {DatabaseName}", config.DatabaseName);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Ошибка добавления начальных данных";
                response.Errors.Add(ex.Message);
                _logger.LogError(ex, "Ошибка добавления начальных данных: {DatabaseName}", config.DatabaseName);
            }

            return response;
        }

        public async Task SaveFoundSeed(string bitcoinAddress, string seedPhrase, string foundAddress, long processed, TimeSpan time)
        {
            // Пример: сохраняем в таблицу TaskResults
            try
            {
                using var connection = new SqlConnection(/* строка подключения */);
                await connection.OpenAsync();
                var cmd = new SqlCommand("INSERT INTO TaskResults (BitcoinAddress, SeedPhrase, FoundAddress, ProcessedCount, ProcessingTime, FoundAt) VALUES (@address, @seed, @found, @count, @time, @at)", connection);
                cmd.Parameters.AddWithValue("@address", bitcoinAddress);
                cmd.Parameters.AddWithValue("@seed", seedPhrase);
                cmd.Parameters.AddWithValue("@found", foundAddress);
                cmd.Parameters.AddWithValue("@count", processed);
                cmd.Parameters.AddWithValue("@time", (long)time.TotalMilliseconds);
                cmd.Parameters.AddWithValue("@at", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения найденной seed phrase");
            }
        }

        private List<string> GetSchemaScripts()
        {
            return new List<string>
            {
                @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Agents')
                CREATE TABLE Agents (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    AgentId NVARCHAR(100) NOT NULL UNIQUE,
                    Name NVARCHAR(100) NOT NULL,
                    Status NVARCHAR(50) NOT NULL DEFAULT 'Offline',
                    LastSeen DATETIME2,
                    Threads INT NOT NULL DEFAULT 1,
                    ProcessingPower FLOAT NOT NULL DEFAULT 1.0,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                )",

                @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tasks')
                CREATE TABLE Tasks (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    TaskId NVARCHAR(100) NOT NULL UNIQUE,
                    BitcoinAddress NVARCHAR(100) NOT NULL,
                    WordCount INT NOT NULL DEFAULT 12,
                    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                    BlockId BIGINT NOT NULL,
                    StartIndex BIGINT NOT NULL,
                    EndIndex BIGINT NOT NULL,
                    AssignedAgentId NVARCHAR(100),
                    StartedAt DATETIME2,
                    CompletedAt DATETIME2,
                    Result NVARCHAR(MAX),
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                    FOREIGN KEY (AssignedAgentId) REFERENCES Agents(AgentId)
                )",

                @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TaskResults')
                CREATE TABLE TaskResults (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    TaskId NVARCHAR(100) NOT NULL,
                    AgentId NVARCHAR(100) NOT NULL,
                    SeedPhrase NVARCHAR(500),
                    BitcoinAddress NVARCHAR(100),
                    ProcessingTime BIGINT NOT NULL,
                    ProcessedCount BIGINT NOT NULL,
                    FoundAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                    FOREIGN KEY (TaskId) REFERENCES Tasks(TaskId),
                    FOREIGN KEY (AgentId) REFERENCES Agents(AgentId)
                )",

                @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SystemSettings')
                CREATE TABLE SystemSettings (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SettingKey NVARCHAR(100) NOT NULL UNIQUE,
                    SettingValue NVARCHAR(MAX),
                    Description NVARCHAR(500),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                )"
            };
        }

        private List<string> GetSeedScripts()
        {
            return new List<string>
            {
                @"
                IF NOT EXISTS (SELECT * FROM SystemSettings WHERE SettingKey = 'DefaultBitcoinAddress')
                INSERT INTO SystemSettings (SettingKey, SettingValue, Description) 
                VALUES ('DefaultBitcoinAddress', '1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ', 'Адрес по умолчанию для поиска')",

                @"
                IF NOT EXISTS (SELECT * FROM SystemSettings WHERE SettingKey = 'DefaultWordCount')
                INSERT INTO SystemSettings (SettingKey, SettingValue, Description) 
                VALUES ('DefaultWordCount', '12', 'Количество слов в seed-фразе по умолчанию')",

                @"
                IF NOT EXISTS (SELECT * FROM SystemSettings WHERE SettingKey = 'BlockSize')
                INSERT INTO SystemSettings (SettingKey, SettingValue, Description) 
                VALUES ('BlockSize', '100000', 'Размер блока для обработки')"
            };
        }
    }
} 