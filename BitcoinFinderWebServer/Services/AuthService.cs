using BitcoinFinderWebServer.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BitcoinFinderWebServer.Services
{
    public interface IAuthService
    {
        Task<bool> ValidateCredentialsAsync(string username, string password);
        Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
        Task<AdminConfig> GetConfigAsync();
        Task SaveConfigAsync(AdminConfig config);
        string HashPassword(string password);
        bool VerifyPassword(string password, string hash);
    }

    public class AuthService : IAuthService
    {
        private readonly ILogger<AuthService> _logger;
        private readonly string _configFile = "admin_config.json";
        private AdminConfig _config;

        public AuthService(ILogger<AuthService> logger)
        {
            _logger = logger;
            _config = LoadConfig();
        }

        public async Task<bool> ValidateCredentialsAsync(string username, string password)
        {
            if (!_config.RequireAuth)
                return true;

            var hashedPassword = HashPassword(password);
            return username == _config.Username && hashedPassword == _config.Password;
        }

        public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            var currentHash = HashPassword(currentPassword);
            if (currentHash != _config.Password)
            {
                return false;
            }

            _config.Password = HashPassword(newPassword);
            await SaveConfigAsync(_config);
            return true;
        }

        public async Task<AdminConfig> GetConfigAsync()
        {
            return await Task.FromResult(_config);
        }

        public async Task SaveConfigAsync(AdminConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_configFile, json);
                _config = config;
                _logger.LogInformation("Конфигурация администратора сохранена");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения конфигурации администратора");
                throw;
            }
        }

        public string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        public bool VerifyPassword(string password, string hash)
        {
            var passwordHash = HashPassword(password);
            return passwordHash == hash;
        }

        private AdminConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    var config = JsonSerializer.Deserialize<AdminConfig>(json);
                    if (config != null)
                    {
                        _logger.LogInformation("Конфигурация администратора загружена");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки конфигурации администратора");
            }

            // Создаем конфигурацию по умолчанию
            var defaultConfig = new AdminConfig();
            _ = Task.Run(async () => await SaveConfigAsync(defaultConfig));
            return defaultConfig;
        }
    }
} 