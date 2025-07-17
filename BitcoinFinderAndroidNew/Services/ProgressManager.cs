#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BitcoinFinderAndroidNew.Services
{
    public class ProgressManager
    {
        private readonly string progressFilePath;
        private readonly string resultsFilePath;
        private readonly object lockObject = new object();

        public ProgressManager(string progressPath = "progress.json", string resultsPath = "results.json")
        {
            progressFilePath = progressPath;
            resultsFilePath = resultsPath;
        }

        public void SaveProgress(SearchProgress progress)
        {
            try
            {
                progress.LastSaved = DateTime.Now;
                var json = JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true });
                
                lock (lockObject)
                {
                    File.WriteAllText(progressFilePath, json);
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но не прерываем поиск
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения прогресса: {ex.Message}");
            }
        }

        public SearchProgress? LoadProgress()
        {
            try
            {
                if (!File.Exists(progressFilePath))
                    return null;

                lock (lockObject)
                {
                    var json = File.ReadAllText(progressFilePath);
                    return JsonSerializer.Deserialize<SearchProgress>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки прогресса: {ex.Message}");
                return null;
            }
        }

        public void SaveFoundResult(FoundResult result)
        {
            try
            {
                var results = LoadFoundResults();
                results.Add(result);
                
                var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
                
                lock (lockObject)
                {
                    File.WriteAllText(resultsFilePath, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения результата: {ex.Message}");
            }
        }

        public List<FoundResult> LoadFoundResults()
        {
            try
            {
                if (!File.Exists(resultsFilePath))
                    return new List<FoundResult>();

                lock (lockObject)
                {
                    var json = File.ReadAllText(resultsFilePath);
                    return JsonSerializer.Deserialize<List<FoundResult>>(json) ?? new List<FoundResult>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки результатов: {ex.Message}");
                return new List<FoundResult>();
            }
        }

        public void ClearProgress()
        {
            try
            {
                lock (lockObject)
                {
                    if (File.Exists(progressFilePath))
                        File.Delete(progressFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка очистки прогресса: {ex.Message}");
            }
        }

        public void ClearResults()
        {
            try
            {
                lock (lockObject)
                {
                    if (File.Exists(resultsFilePath))
                        File.Delete(resultsFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка очистки результатов: {ex.Message}");
            }
        }

        public bool HasProgress()
        {
            return File.Exists(progressFilePath);
        }

        public bool HasResults()
        {
            return File.Exists(resultsFilePath);
        }

        // Async версии для совместимости
        public Task SaveProgressAsync(SearchProgress progress)
        {
            SaveProgress(progress);
            return Task.CompletedTask;
        }

        public Task<SearchProgress?> LoadProgressAsync()
        {
            return Task.FromResult(LoadProgress());
        }

        public Task SaveFoundResultAsync(FoundResult result)
        {
            SaveFoundResult(result);
            return Task.CompletedTask;
        }

        public Task<List<FoundResult>> LoadFoundResultsAsync()
        {
            return Task.FromResult(LoadFoundResults());
        }

        public Task ClearProgressAsync()
        {
            ClearProgress();
            return Task.CompletedTask;
        }

        public Task ClearResultsAsync()
        {
            ClearResults();
            return Task.CompletedTask;
        }
    }
} 