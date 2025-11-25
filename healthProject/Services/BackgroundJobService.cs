using healthProject.Services;

namespace healthProject
{
    public class BackgroundJobService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundJobService> _logger;

        public BackgroundJobService(
            IServiceProvider serviceProvider,
            ILogger<BackgroundJobService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("⏰ 背景排程服務已啟動");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;

                    // 每天早上 9 點檢查未填寫
                    if (now.Hour == 9 && now.Minute == 0)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var scheduledService = scope.ServiceProvider
                            .GetRequiredService<ScheduledJobService>();

                        await scheduledService.CheckAndRemindMissedRecordsAsync();

                        // 等待 60 秒避免重複執行
                        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                    }

                    // 每 30 秒檢查一次時間
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ 背景任務執行失敗");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
    }
}