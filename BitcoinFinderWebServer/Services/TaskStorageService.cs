using BitcoinFinderWebServer.Models;
using System.Text.Json;

namespace BitcoinFinderWebServer.Services
{
    public class TaskStorageService
    {
        private readonly string _configFile = "tasks_config.json";
        private readonly ILogger<TaskStorageService> _logger;

        public TaskStorageService(ILogger<TaskStorageService> logger)
        {
            _logger = logger;
        }

        public async Task SaveTasksAsync(List<SearchTask> tasks)
        {
            try
            {
                var taskData = tasks.Select(t => new
                {
                    Id = t.Id,
                    Name = t.Name,
                    Status = t.Status,
                    CreatedAt = t.CreatedAt,
                    StartedAt = t.StartedAt,
                    StoppedAt = t.StoppedAt,
                    CompletedAt = t.CompletedAt,
                    AssignedTo = t.AssignedTo,
                    TargetAddress = t.SearchParameters.TargetAddress,
                    WordCount = t.SearchParameters.WordCount,
                    Language = t.SearchParameters.Language,
                    StartIndex = t.SearchParameters.StartIndex,
                    EndIndex = t.SearchParameters.EndIndex,
                    BatchSize = t.SearchParameters.BatchSize,
                    BlockSize = t.SearchParameters.BlockSize,
                    Threads = t.SearchParameters.Threads,
                    TotalCombinations = t.TotalCombinations,
                    ProcessedCombinations = t.ProcessedCombinations,
                    FoundSeedPhrase = t.FoundSeedPhrase,
                    FoundAddress = t.FoundAddress,
                    ErrorMessage = t.ErrorMessage,
                    Blocks = t.Blocks
                }).ToList();

                var json = JsonSerializer.Serialize(taskData, new JsonSerializerOptions { WriteIndented = true });
                _logger.LogInformation($"Serialized {tasks.Count} tasks to JSON, size: {json.Length} characters");
                await File.WriteAllTextAsync(_configFile, json);
                _logger.LogInformation($"Successfully wrote {tasks.Count} tasks to {_configFile}");
                
                // Проверяем, что файл действительно создался
                if (File.Exists(_configFile))
                {
                    var fileInfo = new FileInfo(_configFile);
                    _logger.LogInformation($"Config file exists: {_configFile}, size: {fileInfo.Length} bytes");
                }
                else
                {
                    _logger.LogError($"Config file was not created: {_configFile}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving tasks");
                throw;
            }
        }

        public async Task<List<SearchTask>> LoadTasksAsync()
        {
            try
            {
                if (!File.Exists(_configFile))
                {
                    _logger.LogInformation($"Tasks config file {_configFile} not found, returning empty list");
                    return new List<SearchTask>();
                }

                var json = await File.ReadAllTextAsync(_configFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogWarning($"Tasks config file {_configFile} is empty");
                    return new List<SearchTask>();
                }

                var taskData = JsonSerializer.Deserialize<List<JsonElement>>(json);
                var tasks = new List<SearchTask>();
                foreach (var item in taskData)
                {
                    try
                    {
                        // Явная десериализация SearchParameters
                        var task = item.Deserialize<SearchTask>();
                        if (task != null && task.SearchParameters == null)
                        {
                            if (item.TryGetProperty("SearchParameters", out var sp))
                                task.SearchParameters = sp.Deserialize<SearchParameters>() ?? new SearchParameters();
                        }
                        if (task != null)
                            tasks.Add(task);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error parsing task from config: {item}");
                    }
                }
                _logger.LogInformation($"Loaded {tasks.Count} tasks from {_configFile}");
                return tasks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tasks");
                return new List<SearchTask>();
            }
        }

        public async Task SaveTaskAsync(SearchTask task)
        {
            try
            {
                _logger.LogInformation($"SaveTaskAsync called for task {task.Id}: {task.Name}");
                _logger.LogInformation($"Task status: {task.Status}, Blocks count: {task.Blocks?.Count ?? 0}");
                
                var tasks = await LoadTasksAsync();
                _logger.LogInformation($"Loaded {tasks.Count} existing tasks from config");
                
                var existingTask = tasks.FirstOrDefault(t => t.Id == task.Id);
                
                if (existingTask != null)
                {
                    var index = tasks.IndexOf(existingTask);
                    tasks[index] = task;
                    _logger.LogInformation($"Updated existing task at index {index}");
                }
                else
                {
                    tasks.Add(task);
                    _logger.LogInformation($"Added new task to list, total tasks now: {tasks.Count}");
                }

                await SaveTasksAsync(tasks);
                _logger.LogInformation($"Successfully saved task {task.Id} to config file");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in SaveTaskAsync for task {task.Id}: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteTaskAsync(string taskId)
        {
            var tasks = await LoadTasksAsync();
            tasks.RemoveAll(t => t.Id == taskId);
            await SaveTasksAsync(tasks);
        }

        public async Task<SearchTask?> GetTaskAsync(string taskId)
        {
            var tasks = await LoadTasksAsync();
            return tasks.FirstOrDefault(t => t.Id == taskId);
        }
    }
} 