using healthProject.Models;
using Npgsql;
using System.Text.Json;

namespace healthProject.Services
{
    public class ScheduledJobService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ScheduledJobService> _logger;
        private readonly ReportService _reportService;

        public ScheduledJobService(
            IConfiguration configuration,
            ILogger<ScheduledJobService> logger,
            ReportService reportService)
        {
            _configuration = configuration;
            _logger = logger;
            _reportService = reportService;
        }

        // ========================================
        // 📅 每週日晚上8點發送週報
        // ========================================
        public async Task SendWeeklyReportsAsync()
        {
            try
            {
                _logger.LogInformation("⏰ 開始發送每週報表...");

                // 取得所有綁定 LINE 的使用者
                var users = await GetActiveUsersWithLineAsync();

                if (!users.Any())
                {
                    _logger.LogWarning("⚠️ 沒有使用者需要發送週報");
                    return;
                }

                // 計算上週日期範圍 (週一到週日)
                var today = DateTime.Today;
                var lastSunday = today.AddDays(-(int)today.DayOfWeek);
                var lastMonday = lastSunday.AddDays(-6);

                _logger.LogInformation($"📊 週報日期: {lastMonday:yyyy/MM/dd} ~ {lastSunday:yyyy/MM/dd}");

                int successCount = 0;
                int failCount = 0;

                foreach (var user in users)
                {
                    try
                    {
                        await SendWeeklyReportToUserAsync(user, lastMonday, lastSunday);
                        successCount++;
                        _logger.LogInformation($"✅ 已發送週報給 {user.FullName}");
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        _logger.LogError(ex, $"❌ 發送週報失敗: {user.FullName}");
                    }

                    // 避免發送太快被 LINE 限制
                    await Task.Delay(1000);
                }

                _logger.LogInformation($"📊 週報發送完成: 成功 {successCount} / 失敗 {failCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 發送週報任務失敗");
            }
        }

        // ========================================
        // 📤 發送週報給單一使用者
        // ========================================
        private async Task SendWeeklyReportToUserAsync(
            UserDBModel user,
            DateTime startDate,
            DateTime endDate)
        {
            // 產生分析報表
            var analysis = await GenerateAnalysisAsync(
                user.Id,
                user.FullName,
                user.IDNumber,
                ReportType.Weekly,
                startDate,
                endDate
            );

            // 產生 PDF
            var pdfBytes = _reportService.GeneratePdfReport(analysis);

            // 將 PDF 儲存到暫存區 (用於 LINE 驗證後下載)
            var reportId = await SaveWeeklyReportAsync(user.Id, startDate, endDate, pdfBytes);

            // 發送 LINE 訊息
            await SendLineWeeklyNotificationAsync(user, startDate, endDate, reportId);
        }

        // ========================================
        // 📱 發送 LINE 通知
        // ========================================
        private async Task SendLineWeeklyNotificationAsync(
            UserDBModel user,
            DateTime startDate,
            DateTime endDate,
            string reportId)
        {
            var channelAccessToken = _configuration["Line:ChannelAccessToken"];

            if (string.IsNullOrEmpty(channelAccessToken))
            {
                throw new Exception("LINE Channel Access Token 未設定");
            }

            // 產生驗證連結
            var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://your-domain.com";
            var verifyUrl = $"{baseUrl}/Analysis/VerifyWeeklyReport?reportId={reportId}";

            var message = $@"📊 【代謝症候群管理系統】

您好 {user.FullName}，

本週健康報表已產生完成！
📅 期間: {startDate:MM/dd} ~ {endDate:MM/dd}

🔒 為保護您的隱私，請點擊下方連結並輸入身分證字號驗證後查看報表。

{verifyUrl}

※ 驗證連結有效期限為 7 天
※ 請勿將連結分享給他人

💡 如有任何問題，請洽詢您的醫療團隊。";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channelAccessToken}");

            var payload = new
            {
                to = user.LineUserId,
                messages = new[]
                {
                    new
                    {
                        type = "text",
                        text = message
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("https://api.line.me/v2/bot/message/push", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"LINE API 錯誤: {response.StatusCode} - {errorContent}");
            }
        }

        // ========================================
        // 💾 儲存週報 PDF 到資料庫
        // ========================================
        private async Task<string> SaveWeeklyReportAsync(
            int userId,
            DateTime startDate,
            DateTime endDate,
            byte[] pdfBytes)
        {
            var reportId = Guid.NewGuid().ToString("N");
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // 先檢查 WeeklyReports 資料表是否存在，如果不存在就建立
            var createTableQuery = @"
                CREATE TABLE IF NOT EXISTS ""WeeklyReports"" (
                    ""Id"" VARCHAR(50) PRIMARY KEY,
                    ""UserId"" INTEGER NOT NULL,
                    ""StartDate"" DATE NOT NULL,
                    ""EndDate"" DATE NOT NULL,
                    ""PdfData"" BYTEA NOT NULL,
                    ""CreatedAt"" TIMESTAMP NOT NULL DEFAULT NOW(),
                    ""ExpiresAt"" TIMESTAMP NOT NULL,
                    ""IsVerified"" BOOLEAN DEFAULT FALSE,
                    FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"")
                )";

            await using var createCmd = new NpgsqlCommand(createTableQuery, conn);
            await createCmd.ExecuteNonQueryAsync();

            // 插入報表資料
            var insertQuery = @"
                INSERT INTO ""WeeklyReports"" 
                (""Id"", ""UserId"", ""StartDate"", ""EndDate"", ""PdfData"", ""ExpiresAt"")
VALUES (@Id, @UserId, @StartDate, @EndDate, @PdfData, @ExpiresAt)";
            await using var cmd = new NpgsqlCommand(insertQuery, conn);
            cmd.Parameters.AddWithValue("@Id", reportId);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@StartDate", startDate);
            cmd.Parameters.AddWithValue("@EndDate", endDate);
            cmd.Parameters.AddWithValue("@PdfData", pdfBytes);
            cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.Now.AddDays(7)); // 7天後過期

            await cmd.ExecuteNonQueryAsync();

            return reportId;
        }

        // ========================================
        // 🗄️ 資料庫查詢
        // ========================================
        private async Task<List<UserDBModel>> GetActiveUsersWithLineAsync()
        {
            var users = new List<UserDBModel>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
            SELECT ""Id"", ""FullName"", ""IDNumber"", ""LineUserId""
            FROM ""Users""
            WHERE ""IsActive"" = true 
              AND ""LineUserId"" IS NOT NULL 
              AND ""LineUserId"" != ''
              AND ""Role"" = 'Patient'";

            await using var cmd = new NpgsqlCommand(query, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add(new UserDBModel
                {
                    Id = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    IDNumber = reader.GetString(2),
                    LineUserId = reader.GetString(3)
                });
            }

            return users;
        }

        private async Task<AnalysisViewModel> GenerateAnalysisAsync(
            int userId,
            string fullName,
            string idNumber,
            ReportType reportType,
            DateTime startDate,
            DateTime endDate)
        {
            var records = await GetRecordsInRangeAsync(userId, startDate, endDate);
            var statistics = CalculateStatistics(records);
            var charts = GenerateChartData(records, reportType);

            return new AnalysisViewModel
            {
                PatientName = fullName,
                IDNumber = idNumber,
                ReportType = reportType,
                StartDate = startDate,
                EndDate = endDate,
                Statistics = statistics,
                Records = records,
                Charts = charts
            };
        }

        private async Task<List<HealthRecordViewModel>> GetRecordsInRangeAsync(
            int userId, DateTime startDate, DateTime endDate)
        {
            var records = new List<HealthRecordViewModel>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
            SELECT * FROM ""Today""
            WHERE ""UserId"" = @UserId 
              AND ""RecordDate"" >= @StartDate 
              AND ""RecordDate"" <= @EndDate
            ORDER BY ""RecordDate"" ASC";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@StartDate", startDate);
            cmd.Parameters.AddWithValue("@EndDate", endDate);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                records.Add(MapFromReader(reader));
            }

            return records;
        }

        private HealthRecordViewModel MapFromReader(NpgsqlDataReader reader)
        {
            return new HealthRecordViewModel
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                RecordDate = reader.GetDateTime(2),
                ExerciseType = reader.IsDBNull(3) ? null : reader.GetString(3),
                ExerciseDuration = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                WaterIntake = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                Beverage = reader.IsDBNull(6) ? null : reader.GetString(6),
                Meals = reader.IsDBNull(7) ? null : reader.GetString(7),
                Cigarettes = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                BetelNut = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                BloodSugar = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                SystolicBP = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                DiastolicBP = reader.IsDBNull(12) ? null : reader.GetDecimal(12)
            };
        }

        private AnalysisStatistics CalculateStatistics(List<HealthRecordViewModel> records)
        {
            if (!records.Any())
            {
                return new AnalysisStatistics { TotalDays = 0 };
            }

            return new AnalysisStatistics
            {
                TotalDays = records.Count,
                AvgSystolicBP = records.Where(r => r.SystolicBP.HasValue).Any()
                    ? records.Where(r => r.SystolicBP.HasValue).Average(r => r.SystolicBP) : null,
                AvgDiastolicBP = records.Where(r => r.DiastolicBP.HasValue).Any()
                    ? records.Where(r => r.DiastolicBP.HasValue).Average(r => r.DiastolicBP) : null,
                AvgBloodSugar = records.Where(r => r.BloodSugar.HasValue).Any()
                    ? records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar) : null,
                AvgWaterIntake = records.Where(r => r.WaterIntake.HasValue).Any()
                    ? records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake) : null,
                AvgExerciseDuration = records.Where(r => r.ExerciseDuration.HasValue).Any()
                    ? records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration) : null,
                AvgCigarettes = records.Where(r => r.Cigarettes.HasValue).Any()
                    ? records.Where(r => r.Cigarettes.HasValue).Average(r => r.Cigarettes) : null,
                HighBPDays = records.Count(r =>
                    (r.SystolicBP.HasValue && r.SystolicBP.Value > 120) ||
                    (r.DiastolicBP.HasValue && r.DiastolicBP.Value > 80)),
                HighBloodSugarDays = records.Count(r =>
                    r.BloodSugar.HasValue && r.BloodSugar.Value > 99),
                LowWaterDays = records.Count(r =>
                    r.WaterIntake.HasValue && r.WaterIntake.Value < 2000),
                LowExerciseDays = records.Count(r =>
                    r.ExerciseDuration.HasValue && r.ExerciseDuration.Value < 150)
            };
        }

        private ChartData GenerateChartData(List<HealthRecordViewModel> records, ReportType reportType)
        {
            var charts = new ChartData();

            foreach (var record in records.OrderBy(r => r.RecordDate))
            {
                var dateStr = record.RecordDate.ToString("MM/dd");

                if (record.SystolicBP.HasValue || record.DiastolicBP.HasValue)
                {
                    charts.BloodPressureData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.SystolicBP,
                        Value2 = record.DiastolicBP,
                        IsAbnormal = (record.SystolicBP ?? 0) > 120 || (record.DiastolicBP ?? 0) > 80
                    });
                }

                if (record.BloodSugar.HasValue)
                {
                    charts.BloodSugarData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BloodSugar,
                        IsAbnormal = record.BloodSugar.Value > 99
                    });
                }

                if (record.WaterIntake.HasValue)
                {
                    charts.WaterIntakeData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.WaterIntake,
                        IsAbnormal = record.WaterIntake.Value < 2000
                    });
                }

                if (record.ExerciseDuration.HasValue)
                {
                    charts.ExerciseDurationData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.ExerciseDuration,
                        IsAbnormal = record.ExerciseDuration.Value < 150
                    });
                }

                if (!string.IsNullOrEmpty(record.Meals))
                {
                    charts.MealRecords.Add(new MealRecord
                    {
                        Date = dateStr,
                        Meals = record.Meals
                    });
                }

                if (!string.IsNullOrEmpty(record.Beverage))
                {
                    charts.BeverageRecords.Add(new BeverageRecord
                    {
                        Date = dateStr,
                        Beverage = record.Beverage
                    });
                }
            }

            return charts;
        }
    }

}

