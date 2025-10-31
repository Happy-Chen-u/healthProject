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

                var users = await GetActiveUsersWithLineAsync();

                if (!users.Any())
                {
                    _logger.LogWarning("⚠️ 沒有使用者需要發送週報");
                    return;
                }

                var today = DateTime.Today;
                var lastSunday = today.AddDays(-(int)today.DayOfWeek);
                var lastMonday = lastSunday.AddDays(-6);

                _logger.LogInformation($"📊 週報日期: {lastMonday:yyyy/MM/dd} ~ {lastSunday:yyyy/MM/dd}");

                int successCount = 0, failCount = 0;

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

                    await Task.Delay(1000); // 避免 LINE API 過載
                }

                _logger.LogInformation($"📊 週報發送完成: 成功 {successCount} / 失敗 {failCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 發送週報任務失敗");
            }
        }

        private async Task SendWeeklyReportToUserAsync(
            UserDBModel user,
            DateTime startDate,
            DateTime endDate)
        {
            var analysis = await GenerateAnalysisAsync(
                user.Id,
                user.FullName,
                user.IDNumber,
                ReportType.Weekly,
                startDate,
                endDate);

            var pdfBytes = _reportService.GeneratePdfReport(analysis);
            var reportId = await SaveWeeklyReportAsync(user.Id, startDate, endDate, pdfBytes);
            await SendLineWeeklyNotificationAsync(user, startDate, endDate, reportId);
        }

        private async Task SendLineWeeklyNotificationAsync(
            UserDBModel user,
            DateTime startDate,
            DateTime endDate,
            string reportId)
        {
            var token = _configuration["Line:ChannelAccessToken"];
            if (string.IsNullOrEmpty(token)) throw new Exception("LINE Channel Access Token 未設定");

            var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://your-domain.com";
            var verifyUrl = $"{baseUrl}/Analysis/VerifyWeeklyReport?reportId={reportId}";

            var message = $@"📊 【代謝症候群管理系統】

您好 {user.FullName}，

本週健康報表已產生完成！
📅 期間: {startDate:MM/dd} ~ {endDate:MM/dd}

🔒 為保護您的隱私，請點擊下方連結並輸入身分證字號驗證後查看報表。
{verifyUrl}

※ 驗證連結有效期限為 7 天
※ 請勿將連結分享給他人";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var payload = new
            {
                to = user.LineUserId,
                messages = new[]
                {
                    new { type = "text", text = message }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://api.line.me/v2/bot/message/push", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"LINE API 錯誤: {response.StatusCode} - {error}");
            }
        }

        private async Task<string> SaveWeeklyReportAsync(
            int userId, DateTime startDate, DateTime endDate, byte[] pdfBytes)
        {
            var reportId = Guid.NewGuid().ToString("N");
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

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
            await using (var createCmd = new NpgsqlCommand(createTableQuery, conn))
                await createCmd.ExecuteNonQueryAsync();

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
            cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.Now.AddDays(7));
            await cmd.ExecuteNonQueryAsync();

            return reportId;
        }

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
            int userId, string fullName, string idNumber,
            ReportType reportType, DateTime startDate, DateTime endDate)
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

        // ✅ 已更新相容新版 HealthRecordViewModel
        private HealthRecordViewModel MapFromReader(NpgsqlDataReader reader)
        {
            var record = new HealthRecordViewModel
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                RecordDate = reader.GetDateTime(reader.GetOrdinal("RecordDate")),
                RecordTime = reader.IsDBNull(reader.GetOrdinal("RecordTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("RecordTime")),

                // 🩸 新版血壓欄位
                BP_First_1_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_First_1_Systolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_1_Systolic")),
                BP_First_1_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_First_1_Diastolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_1_Diastolic")),
                BP_First_2_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_First_2_Systolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_2_Systolic")),
                BP_First_2_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_First_2_Diastolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_2_Diastolic")),
                BP_Second_1_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_1_Systolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_1_Systolic")),
                BP_Second_1_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_1_Diastolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_1_Diastolic")),
                BP_Second_2_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_2_Systolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_2_Systolic")),
                BP_Second_2_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_2_Diastolic")) ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_2_Diastolic")),

                ExerciseType = reader.IsDBNull(reader.GetOrdinal("ExerciseType")) ? null : reader.GetString(reader.GetOrdinal("ExerciseType")),
                ExerciseDuration = reader.IsDBNull(reader.GetOrdinal("ExerciseDuration")) ? null : reader.GetDecimal(reader.GetOrdinal("ExerciseDuration")),
                WaterIntake = reader.IsDBNull(reader.GetOrdinal("WaterIntake")) ? null : reader.GetDecimal(reader.GetOrdinal("WaterIntake")),
                Beverage = reader.IsDBNull(reader.GetOrdinal("Beverage")) ? null : reader.GetString(reader.GetOrdinal("Beverage")),
                Cigarettes = reader.IsDBNull(reader.GetOrdinal("Cigarettes")) ? null : reader.GetDecimal(reader.GetOrdinal("Cigarettes")),
                BetelNut = reader.IsDBNull(reader.GetOrdinal("BetelNut")) ? null : reader.GetDecimal(reader.GetOrdinal("BetelNut")),
                BloodSugar = reader.IsDBNull(reader.GetOrdinal("BloodSugar")) ? null : reader.GetDecimal(reader.GetOrdinal("BloodSugar"))
            };

            // 🍱 三餐 JSON
            try
            {
                if (!reader.IsDBNull(reader.GetOrdinal("Meals_Breakfast")))
                    record.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(reader.GetString(reader.GetOrdinal("Meals_Breakfast")));
                if (!reader.IsDBNull(reader.GetOrdinal("Meals_Lunch")))
                    record.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(reader.GetString(reader.GetOrdinal("Meals_Lunch")));
                if (!reader.IsDBNull(reader.GetOrdinal("Meals_Dinner")))
                    record.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(reader.GetString(reader.GetOrdinal("Meals_Dinner")));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 解析三餐 JSON 失敗: {ex.Message}");
            }

            return record;
        }

        private AnalysisStatistics CalculateStatistics(List<HealthRecordViewModel> records)
        {
            if (!records.Any()) return new AnalysisStatistics { TotalDays = 0 };

            return new AnalysisStatistics
            {
                TotalDays = records.Count,
                AvgSystolicBP = records.Where(r => r.AvgSystolicBP.HasValue).Average(r => r.AvgSystolicBP),
                AvgDiastolicBP = records.Where(r => r.AvgDiastolicBP.HasValue).Average(r => r.AvgDiastolicBP),
                AvgBloodSugar = records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar),
                AvgWaterIntake = records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake),
                AvgExerciseDuration = records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration),
                AvgCigarettes = records.Where(r => r.Cigarettes.HasValue).Average(r => r.Cigarettes),
                HighBPDays = records.Count(r => (r.AvgSystolicBP ?? 0) > 120 || (r.AvgDiastolicBP ?? 0) > 80),
                HighBloodSugarDays = records.Count(r => (r.BloodSugar ?? 0) > 99),
                LowWaterDays = records.Count(r => (r.WaterIntake ?? 0) < 2000),
                LowExerciseDays = records.Count(r => (r.ExerciseDuration ?? 0) < 150)
            };
        }

        // ========================================
        // 📊 產生圖表數據 (ScheduledJobService.cs)
        // ========================================
        private ChartData GenerateChartData(List<HealthRecordViewModel> records, ReportType reportType)
        {
            var charts = new ChartData();

            foreach (var record in records.OrderBy(r => r.RecordDate))
            {
                var dateStr = record.RecordDate.ToString("MM/dd");

                // 血壓數據
                if (record.BP_First_1_Systolic.HasValue || record.BP_First_1_Diastolic.HasValue)
                {
                    charts.BloodPressureData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BP_First_1_Systolic,
                        Value2 = record.BP_First_1_Diastolic,
                        IsAbnormal = (record.BP_First_1_Systolic ?? 0) > 120 ||
                                     (record.BP_First_1_Diastolic ?? 0) > 80
                    });
                }

                // 血糖數據
                if (record.BloodSugar.HasValue)
                {
                    charts.BloodSugarData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BloodSugar,
                        IsAbnormal = record.BloodSugar.Value > 99
                    });
                }

                // 飲水量數據
                if (record.WaterIntake.HasValue)
                {
                    charts.WaterIntakeData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.WaterIntake,
                        IsAbnormal = record.WaterIntake.Value < 2000
                    });
                }

                // 運動時間數據
                if (record.ExerciseDuration.HasValue)
                {
                    charts.ExerciseDurationData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.ExerciseDuration,
                        IsAbnormal = record.ExerciseDuration.Value < 150
                    });
                }

                // ✅ 三餐記錄 - 使用 MealsDisplay
                if (!string.IsNullOrEmpty(record.MealsDisplay) && record.MealsDisplay != "未記錄")
                {
                    charts.MealRecords.Add(new MealRecord
                    {
                        Date = dateStr,
                        Meals = record.MealsDisplay
                    });
                }

                // 飲料記錄
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
