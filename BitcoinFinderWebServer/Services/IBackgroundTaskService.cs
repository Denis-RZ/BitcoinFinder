namespace BitcoinFinderWebServer.Services
{
    public interface IBackgroundTaskService
    {
        Task StartTaskAsync(string taskId);
        Task StopTaskAsync(string taskId);
        Task PauseTaskAsync(string taskId);
        bool IsTaskRunning(string taskId);
        List<string> GetRunningTaskIds();
    }
} 