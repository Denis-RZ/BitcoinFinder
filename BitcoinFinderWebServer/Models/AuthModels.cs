using System.ComponentModel.DataAnnotations;

namespace BitcoinFinderWebServer.Models
{
    public class LoginRequest
    {
        [Required]
        public string Username { get; set; } = "";
        
        [Required]
        public string Password { get; set; } = "";
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Token { get; set; } = "";
    }

    public class ChangePasswordRequest
    {
        [Required]
        public string CurrentPassword { get; set; } = "";
        
        [Required]
        public string NewPassword { get; set; } = "";
        
        [Required]
        public string ConfirmPassword { get; set; } = "";
    }

    public class AdminConfig
    {
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin123";
        public bool RequireAuth { get; set; } = true;
    }

    public class LoginViewModel
    {
        [Required]
        public string Username { get; set; }
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
} 