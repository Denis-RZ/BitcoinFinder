using System.ComponentModel.DataAnnotations;

namespace BitcoinFinderWebServer.Models
{
    public class DatabaseConfig
    {
        [Required]
        public string DatabaseName { get; set; } = "";
        
        [Required]
        public string Server { get; set; } = "localhost";
        
        [Required]
        public int Port { get; set; } = 1433; // SQL Server по умолчанию
        
        [Required]
        public string Username { get; set; } = "";
        
        [Required]
        public string Password { get; set; } = "";
        
        public bool UseWindowsAuthentication { get; set; } = false;
        
        public string ConnectionString => UseWindowsAuthentication 
            ? $"Server={Server},{Port};Database={DatabaseName};Trusted_Connection=true;TrustServerCertificate=true;"
            : $"Server={Server},{Port};Database={DatabaseName};User Id={Username};Password={Password};TrustServerCertificate=true;";
    }

    public class DatabaseInstallRequest
    {
        public DatabaseConfig Config { get; set; } = new();
        public bool CreateDatabase { get; set; } = true;
        public bool InstallSchema { get; set; } = true;
        public bool InstallSeedData { get; set; } = false;
    }

    public class DatabaseInstallResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
} 