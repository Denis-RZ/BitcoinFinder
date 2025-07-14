using BitcoinFinderWebServer.Models;

namespace BitcoinFinderWebServer.Services
{
    public interface IAuthService
    {
        Task<bool> ValidateCredentialsAsync(string username, string password);
        Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
        Task<AdminConfig> GetConfigAsync();
        Task SaveConfigAsync(AdminConfig config);
    }

    public class AuthService : IAuthService
    {
        private AdminConfig _config = new AdminConfig { Username = "admin", Password = "admin123", RequireAuth = true };

        public async Task<bool> ValidateCredentialsAsync(string username, string password)
        {
            if (!_config.RequireAuth)
                return true;
            return username == _config.Username && password == _config.Password;
        }

        public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            if (currentPassword != _config.Password)
                return false;
            _config.Password = newPassword;
            return true;
        }

        public async Task<AdminConfig> GetConfigAsync()
        {
            return await Task.FromResult(_config);
        }

        public async Task SaveConfigAsync(AdminConfig config)
        {
            _config = config;
        }
    }
} 