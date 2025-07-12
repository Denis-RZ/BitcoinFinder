using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Linq;

namespace BitcoinFinderWebServer.Services
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAgentApiKeyService _agentApiKeyService;

        public AuthMiddleware(RequestDelegate next, IAgentApiKeyService agentApiKeyService)
        {
            _next = next;
            _agentApiKeyService = agentApiKeyService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";
            if (path.StartsWith("/api/auth") || path.StartsWith("/login") || path.StartsWith("/favicon.ico") || path.StartsWith("/css") || path.StartsWith("/js"))
            {
                await _next(context);
                return;
            }
            // Открытый, но защищённый API для агентов
            if (path.StartsWith("/api/agent"))
            {
                var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
                if (string.IsNullOrEmpty(apiKey) || apiKey != _agentApiKeyService.GetApiKey())
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized: Invalid API key");
                    return;
                }
                await _next(context);
                return;
            }
            // Проверка сессии для всего остального
            var token = context.Session.GetString("AuthToken");
            if (string.IsNullOrEmpty(token))
            {
                context.Response.Redirect("/login.html");
                return;
            }
            await _next(context);
        }
    }
} 