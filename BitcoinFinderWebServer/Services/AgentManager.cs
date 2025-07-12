using BitcoinFinderWebServer.Models;
using System.Collections.Concurrent;

namespace BitcoinFinderWebServer.Services
{
    public class AgentManager
    {
        private readonly ConcurrentDictionary<string, Agent> _agents = new();
        private readonly ILogger<AgentManager> _logger;

        public AgentManager(ILogger<AgentManager> logger)
        {
            _logger = logger;
        }

        public async Task RegisterAgentAsync(Agent agent)
        {
            _agents.AddOrUpdate(agent.Name, agent, (key, existing) => agent);
            _logger.LogInformation($"Агент {agent.Name} зарегистрирован");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task UnregisterAgentAsync(string agentName)
        {
            if (_agents.TryRemove(agentName, out var agent))
            {
                _logger.LogInformation($"Агент {agentName} отключен");
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task UpdateAgentActivityAsync(string agentName)
        {
            if (_agents.TryGetValue(agentName, out var agent))
            {
                agent.LastSeen = DateTime.UtcNow;
                agent.Status = "Active";
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task<List<Agent>> GetAllAgentsAsync()
        {
            return await System.Threading.Tasks.Task.FromResult(_agents.Values.ToList());
        }

        public async Task<object> GetAgentStatusAsync()
        {
            var agents = _agents.Values.ToList();
            var totalAgents = agents.Count;
            var activeAgents = agents.Count(a => a.Status == "Active" || a.Status == "Connected");
            var totalThreads = agents.Sum(a => a.Threads);
            var totalTasksProcessed = agents.Sum(a => a.TotalTasksProcessed);
            var totalCombinationsProcessed = agents.Sum(a => a.TotalCombinationsProcessed);

            return await System.Threading.Tasks.Task.FromResult(new
            {
                TotalAgents = totalAgents,
                ActiveAgents = activeAgents,
                TotalThreads = totalThreads,
                TotalTasksProcessed = totalTasksProcessed,
                TotalCombinationsProcessed = totalCombinationsProcessed,
                Agents = agents
            });
        }

        public async Task UpdateAgentStatsAsync(string agentName, long tasksProcessed, long combinationsProcessed, double processingRate)
        {
            if (_agents.TryGetValue(agentName, out var agent))
            {
                agent.TotalTasksProcessed += tasksProcessed;
                agent.TotalCombinationsProcessed += combinationsProcessed;
                agent.ProcessingRate = processingRate;
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task<bool> SetAgentThreadsAsync(string agentName, int threads)
        {
            if (_agents.TryGetValue(agentName, out var agent))
            {
                agent.Threads = threads;
                return true;
            }
            return false;
        }

        // Методы для Keep-Alive API
        public string GetStatus()
        {
            var agents = _agents.Values.ToList();
            var activeAgents = agents.Count(a => a.Status == "Active" || a.Status == "Connected");
            return $"Active: {activeAgents}/{agents.Count}";
        }

        public int GetActiveAgentsCount()
        {
            return _agents.Values.Count(a => a.Status == "Active" || a.Status == "Connected");
        }

        public bool IsHealthy()
        {
            return true; // Упрощенная проверка
        }

        public async Task ActivateAsync()
        {
            // Активация всех агентов
            foreach (var agent in _agents.Values)
            {
                agent.Status = "Active";
                agent.LastSeen = DateTime.UtcNow;
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }
    }
} 