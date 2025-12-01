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

                    // ========================================
                    // 🆕 中午 12:00 - 檢查上午血壓
                    // ========================================
                    if (now.Hour == 12 && now.Minute == 0)
                    {
                        _logger.LogInformation("⏰ [12:00] 開始檢查上午血壓填寫狀況");
                        using var scope = _serviceProvider.CreateScope();
                        var scheduledService = scope.ServiceProvider
                            .GetRequiredService<ScheduledJobService>();

                        await scheduledService.CheckMorningBloodPressureAsync();

                        // 等待 60 秒避免重複執行
                        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                    }

                    // ========================================
                    // 🆕 晚上 22:00 - 執行所有晚間檢查
                    // ========================================
                    if (now.Minute % 1 == 0 && now.Second < 30)

                    {
                        _logger.LogInformation("⏰ [22:00] 開始執行晚間健康檢查");
                        using var scope = _serviceProvider.CreateScope();
                        var scheduledService = scope.ServiceProvider
                            .GetRequiredService<ScheduledJobService>();

                        // 1. 檢查睡前血壓
                        await scheduledService.CheckEveningBloodPressureAsync();
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

                        // 2. 檢查全日血壓
                        await scheduledService.CheckAllDayBloodPressureAsync();
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

                        // 3. 檢查三餐記錄
                        await scheduledService.CheckMealsRecordAsync();
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

                        // 4. 發送完成感謝訊息
                        await scheduledService.SendCompletionThankYouAsync();

                        // 等待 60 秒避免重複執行
                        await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);
                    }

                    // ========================================
                    // 原本的 9:00 檢查(連續兩天未填)
                    // ========================================
                    if (now.Hour == 9 && now.Minute == 0)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var scheduledService = scope.ServiceProvider
                            .GetRequiredService<ScheduledJobService>();

                        await scheduledService.CheckAndRemindMissedRecordsAsync();

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