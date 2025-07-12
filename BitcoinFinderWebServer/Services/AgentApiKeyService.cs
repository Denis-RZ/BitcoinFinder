using BitcoinFinderWebServer.Models;
using System.Text.Json;

namespace BitcoinFinderWebServer.Services
{
    public interface IAgentApiKeyService
    {
        string GetApiKey();
        void SetApiKey(string key);
    }

    public class AgentApiKeyService : IAgentApiKeyService
    {
        private readonly string _configFile = "agent_api_config.json";
        private AgentApiConfig _config;

        public AgentApiKeyService()
        {
            _config = LoadConfig();
        }

        public string GetApiKey() => _config.ApiKey;

        public void SetApiKey(string key)
        {
            _config.ApiKey = key;
            SaveConfig(_config);
        }

        private AgentApiConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    var config = JsonSerializer.Deserialize<AgentApiConfig>(json);
                    if (config != null) return config;
                }
            }
            catch { }
            var def = new AgentApiConfig();
            SaveConfig(def);
            return def;
        }

        private void SaveConfig(AgentApiConfig config)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json);
        }
    }
} 