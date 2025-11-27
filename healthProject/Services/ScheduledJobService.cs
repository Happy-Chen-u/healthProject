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
        public async Task SendWeeklyReportToUserAsync(
            UserDBModel user,
            DateTime startDate,
            DateTime endDate)
        {
            try
            {
                _logger.LogInformation($"📄 開始為 {user.FullName} 產生週報");

                // 1. 產生分析資料
                var analysis = await GenerateAnalysisAsync(
                    user.Id, user.FullName, user.IDNumber,
                    ReportType.Weekly, startDate, endDate);

                // 2. 產生 PDF
                var pdfBytes = _reportService.GeneratePdfReport(analysis);
                _logger.LogInformation($"✅ PDF 產生完成,大小: {pdfBytes.Length / 1024}KB");

                // 3. 儲存到資料庫並取得下載連結
                var reportId = await SaveWeeklyReportAsync(user.Id, startDate, endDate, pdfBytes);

                // 4. 產生下載連結(使用你的網站域名)
                var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://你的網域.com";
                var downloadUrl = $"{baseUrl}/Analysis/DownloadWeeklyReport?reportId={reportId}";

                // 5. 傳送 LINE 訊息
                await SendLineNotificationAsync(user, startDate, endDate, downloadUrl);

                _logger.LogInformation($"🎉 週報已成功傳送: {user.FullName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"發送週報失敗: {user.FullName}");
                throw;
            }
        }


        // ========================================
        // 🔔 檢查並提醒兩天未填寫的個案
        // ========================================
        public async Task CheckAndRemindMissedRecordsAsync()
        {
            try
            {
                _logger.LogInformation("⏰ 開始檢查未填寫健康資訊的個案...");

                var today = DateTime.Today;
                var twoDaysAgo = today.AddDays(-2);

                var missedUsers = await GetUsersWithMissedRecordsAsync(twoDaysAgo);

                if (!missedUsers.Any())
                {
                    _logger.LogInformation("✅ 沒有個案需要提醒");
                    return;
                }

                _logger.LogInformation($"📢 找到 {missedUsers.Count} 位個案需要提醒");

                int successCount = 0, failCount = 0;

                foreach (var user in missedUsers)
                {
                    try
                    {
                        await SendMissedRecordReminderAsync(user);

                        // 更新最後提醒日期
                        await UpdateLastReminderDateAsync(user.Id);

                        successCount++;
                        _logger.LogInformation($"✅ 已提醒 {user.FullName} (連續 {user.MissedDays} 天未填)");
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        _logger.LogError(ex, $"❌ 提醒失敗: {user.FullName}");
                    }

                    await Task.Delay(1000); // 避免 LINE API 限流
                }

                _logger.LogInformation($"📊 提醒發送完成: 成功 {successCount} / 失敗 {failCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 檢查未填寫任務失敗");
            }
        }

        // ========================================
        // 🔍 取得連續兩天以上未填寫的個案（修正版）
        // ========================================
        private async Task<List<MissedUserInfo>> GetUsersWithMissedRecordsAsync(DateTime twoDaysAgo)
        {
            var users = new List<MissedUserInfo>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // 修正：使用小寫的欄位名稱
            var query = @"
        WITH LastRecords AS (
            SELECT 
                ""UserId"",
                MAX(""RecordDate"") as lastrecorddate  -- 改用小寫
            FROM ""Today""
            WHERE ""IsReminderRecord"" = FALSE
            GROUP BY ""UserId""
        ),
        TodayReminders AS (
            SELECT DISTINCT ""UserId""
            FROM ""Today""
            WHERE ""IsReminderRecord"" = TRUE 
              AND ""RecordDate"" = @Today
        )
        SELECT 
            u.""Id"",
            u.""FullName"",
            u.""LineUserId"",
            COALESCE(lr.lastrecorddate, DATE '1900-01-01') as lastrecorddate  -- 改用小寫
        FROM ""Users"" u
        LEFT JOIN LastRecords lr ON u.""Id"" = lr.""UserId""
        LEFT JOIN TodayReminders tr ON u.""Id"" = tr.""UserId""
        WHERE u.""IsActive"" = true
          AND u.""Role"" = 'Patient'
          AND u.""LineUserId"" IS NOT NULL
          AND u.""LineUserId"" != ''
          AND (
              lr.lastrecorddate IS NULL 
              OR lr.lastrecorddate <= @TwoDaysAgo
          )
          AND tr.""UserId"" IS NULL
    ";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@TwoDaysAgo", twoDaysAgo);
            cmd.Parameters.AddWithValue("@Today", DateTime.Today);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var lastRecordDate = reader.GetDateTime(3);
                var missedDays = lastRecordDate.Year == 1900
                    ? 999 // 從未填寫
                    : (DateTime.Today - lastRecordDate).Days;

                // 只加入連續兩天以上未填寫的
                if (missedDays >= 2)
                {
                    users.Add(new MissedUserInfo
                    {
                        Id = reader.GetInt32(0),
                        FullName = reader.GetString(1),
                        LineUserId = reader.GetString(2),
                        LastRecordDate = lastRecordDate.Year == 1900 ? null : lastRecordDate,
                        MissedDays = missedDays
                    });
                }
            }

            return users;
        }

        // ========================================
        // 📤 發送 LINE 提醒訊息
        // ========================================
        private async Task SendMissedRecordReminderAsync(MissedUserInfo user)
        {
            var token = _configuration["Line:ChannelAccessToken"];
            if (string.IsNullOrEmpty(token))
                throw new Exception("LINE Channel Access Token 未設定");

            // 建立 Quick Reply 按鈕
            var quickReply = new
            {
                items = new[]
                {
            new
            {
                type = "action",
                action = new
                {
                    type = "message",
                    label = "🗓️ 工作行程太忙",
                    text = "🗓️ 工作行程太忙"  // ✅ 改為顯示文字而非代碼
                }
            },
            new
            {
                type = "action",
                action = new
                {
                    type = "message",
                    label = "😷 身體有點不舒服",
                    text = "😷 身體有點不舒服"  // ✅ 改為顯示文字
                }
            },
            new
            {
                type = "action",
                action = new
                {
                    type = "message",
                    label = "🔢 不確定要填寫什麼",
                    text = "🔢 不確定要填寫什麼"  // ✅ 改為顯示文字
                }
            },
            new
            {
                type = "action",
                action = new
                {
                    type = "message",
                    label = "📱 手機不在身邊/沒電",
                    text = "📱 手機不在身邊/沒電"  // ✅ 改為顯示文字
                }
            },
            new
            {
                type = "action",
                action = new
                {
                    type = "message",
                    label = "💬 其他原因",
                    text = "💬 其他原因"  // ✅ 改為顯示文字
                }
            }
        }
            };

            var message = user.LastRecordDate.HasValue
                ? $@"⚠️ 【代謝症候群管理系統】

{user.FullName} 您好,

我們注意到您已經連續 {user.MissedDays} 天沒有填寫今日健康資訊了。

上次填寫日期: {user.LastRecordDate.Value:yyyy/MM/dd}

📋 定期記錄健康資訊對於疾病管理非常重要!

請問是什麼原因讓您沒有填寫呢?
請點選下方原因,或輸入其他原因:"
                : $@"⚠️ 【代謝症候群管理系統】

{user.FullName} 您好,

我們注意到您還沒有填寫過今日健康資訊。

📋 定期記錄健康資訊對於疾病管理非常重要!

請問是什麼原因讓您沒有填寫呢?
請點選下方原因,或輸入其他原因:";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var payload = new
            {
                to = user.LineUserId,
                messages = new[]
                {
            new
            {
                type = "text",
                text = message,
                quickReply = quickReply
            }
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

        // ========================================
        // 💾 更新最後提醒日期 (改為插入提醒記錄)
        // ========================================
        private async Task UpdateLastReminderDateAsync(int userId)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // 在 Today 表插入一筆提醒記錄
            var query = @"
        INSERT INTO ""Today"" 
        (""UserId"", ""RecordDate"", ""IsReminderRecord"")
        VALUES (@UserId, @Today, TRUE)
    ";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@Today", DateTime.Today);
            await cmd.ExecuteNonQueryAsync();
        }

        // ========================================
        // 💾 儲存使用者未填寫原因
        // ========================================
        public async Task SaveMissedReasonAsync(int userId, string reason)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // 更新今天的提醒記錄,加入原因
            var query = @"
        UPDATE ""Today""
        SET ""MissedReason"" = @Reason
        WHERE ""UserId"" = @UserId
          AND ""RecordDate"" = @Today
          AND ""IsReminderRecord"" = TRUE
    ";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Reason", reason);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@Today", DateTime.Today);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();

            if (rowsAffected > 0)
            {
                _logger.LogInformation($"✅ 已儲存未填寫原因: UserId {userId} - {reason}");
            }
            else
            {
                _logger.LogWarning($"⚠️ 找不到今日的提醒記錄: UserId {userId}");
            }
        }

        // 新增輔助類別
        public class MissedUserInfo
        {
            public int Id { get; set; }
            public string FullName { get; set; }
            public string LineUserId { get; set; }
            public DateTime? LastRecordDate { get; set; }
            public int MissedDays { get; set; }
        }

        // ========================================
        // 📤 傳送 LINE 通知
        // ========================================
        private async Task SendLineNotificationAsync(
            UserDBModel user,
            DateTime startDate,
            DateTime endDate,
            string downloadUrl)
        {
            var token = _configuration["Line:ChannelAccessToken"];
            if (string.IsNullOrEmpty(token))
                throw new Exception("LINE Channel Access Token 未設定");

            var message = $@"📊 【代謝症候群管理系統】

您好 {user.FullName},

本週健康報表已產生完成!
📅 期間: {startDate:MM/dd} ~ {endDate:MM/dd}

📥 請點擊下方連結下載報表:
{downloadUrl}

✅ 點擊後會自動下載 PDF 檔案
📱 下載後請使用 PDF 閱讀器開啟

※ 連結有效期限為 30 天
※ 請妥善保存您的報表";

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

            _logger.LogInformation($"✅ LINE 訊息已傳送: {user.LineUserId}");
        }

        // ========================================
        // 💾 儲存報表到資料庫
        // ========================================
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
            cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.Now.AddDays(30));
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation($"✅ 報表已儲存到資料庫: {reportId}");
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

        private HealthRecordViewModel MapFromReader(NpgsqlDataReader reader)
        {
            var record = new HealthRecordViewModel
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                RecordDate = reader.GetDateTime(reader.GetOrdinal("RecordDate")),
                RecordTime = reader.IsDBNull(reader.GetOrdinal("RecordTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("RecordTime")),
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

            try
            {
                if (!reader.IsDBNull(reader.GetOrdinal("Meals_Breakfast")))
                    record.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(reader.GetString(reader.GetOrdinal("Meals_Breakfast")));
                if (!reader.IsDBNull(reader.GetOrdinal("Meals_Lunch")))
                    record.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(reader.GetString(reader.GetOrdinal("Meals_Lunch")));
                if (!reader.IsDBNull(reader.GetOrdinal("Meals_Dinner")))
                    record.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(reader.GetString(reader.GetOrdinal("Meals_Dinner")));
            }
            catch { }

            return record;
        }

        private AnalysisStatistics CalculateStatistics(List<HealthRecordViewModel> records)
        {
            if (!records.Any()) return new AnalysisStatistics { TotalDays = 0 };

            decimal totalVeg = 0, totalProtein = 0, totalCarbs = 0;
            int mealDays = 0;

            foreach (var record in records)
            {
                bool hasMeal = false;
                if (record.Meals_Breakfast != null)
                {
                    totalVeg += ParseMealValue(record.Meals_Breakfast.Vegetables);
                    totalProtein += ParseMealValue(record.Meals_Breakfast.Protein);
                    totalCarbs += ParseMealValue(record.Meals_Breakfast.Carbs);
                    hasMeal = true;
                }
                if (record.Meals_Lunch != null)
                {
                    totalVeg += ParseMealValue(record.Meals_Lunch.Vegetables);
                    totalProtein += ParseMealValue(record.Meals_Lunch.Protein);
                    totalCarbs += ParseMealValue(record.Meals_Lunch.Carbs);
                    hasMeal = true;
                }
                if (record.Meals_Dinner != null)
                {
                    totalVeg += ParseMealValue(record.Meals_Dinner.Vegetables);
                    totalProtein += ParseMealValue(record.Meals_Dinner.Protein);
                    totalCarbs += ParseMealValue(record.Meals_Dinner.Carbs);
                    hasMeal = true;
                }
                if (hasMeal) mealDays++;
            }

            return new AnalysisStatistics
            {
                TotalDays = records.Count,
                AvgSystolicBP = records.Where(r => r.AvgSystolicBP.HasValue).Any() ? records.Where(r => r.AvgSystolicBP.HasValue).Average(r => r.AvgSystolicBP) : 0,
                AvgDiastolicBP = records.Where(r => r.AvgDiastolicBP.HasValue).Any() ? records.Where(r => r.AvgDiastolicBP.HasValue).Average(r => r.AvgDiastolicBP) : 0,
                AvgBloodSugar = records.Where(r => r.BloodSugar.HasValue).Any() ? records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar) : 0,
                AvgWaterIntake = records.Where(r => r.WaterIntake.HasValue).Any() ? records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake) : 0,
                AvgExerciseDuration = records.Where(r => r.ExerciseDuration.HasValue).Any() ? records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration) : 0,
                AvgCigarettes = records.Where(r => r.Cigarettes.HasValue).Any() ? records.Where(r => r.Cigarettes.HasValue).Average(r => r.Cigarettes) : 0,
                TotalCigarettes = records.Where(r => r.Cigarettes.HasValue).Sum(r => r.Cigarettes) ?? 0,
                AvgBetelNut = records.Where(r => r.BetelNut.HasValue).Any() ? records.Where(r => r.BetelNut.HasValue).Average(r => r.BetelNut) : 0,
                TotalBetelNut = records.Where(r => r.BetelNut.HasValue).Sum(r => r.BetelNut) ?? 0,
                HighBPDays = records.Count(r => (r.AvgSystolicBP ?? 0) > 120 || (r.AvgDiastolicBP ?? 0) > 80),
                HighBloodSugarDays = records.Count(r => (r.BloodSugar ?? 0) > 99),
                LowWaterDays = records.Count(r => (r.WaterIntake ?? 0) < 2000),
                LowExerciseDays = records.Count(r => (r.ExerciseDuration ?? 0) < 150),
                AvgVegetables = mealDays > 0 ? totalVeg / mealDays : 0,
                AvgProtein = mealDays > 0 ? totalProtein / mealDays : 0,
                AvgCarbs = mealDays > 0 ? totalCarbs / mealDays : 0
            };
        }

        private decimal ParseMealValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "0") return 0;
            decimal total = 0;
            if (value.Contains('+'))
            {
                foreach (var part in value.Split('+'))
                    if (decimal.TryParse(part.Trim(), out decimal num)) total += num;
            }
            else if (decimal.TryParse(value.Trim(), out decimal num)) total = num;
            return total;
        }

        private ChartData GenerateChartData(List<HealthRecordViewModel> records, ReportType reportType)
        {
            var charts = new ChartData
            {
                CigarettesData = new List<ChartPoint>(),
                BetelNutData = new List<ChartPoint>(),
                MealRecords = new List<MealRecord>(),
                BeverageRecords = new List<BeverageRecord>()
            };

            foreach (var record in records.OrderBy(r => r.RecordDate))
            {
                var dateStr = record.RecordDate.ToString("MM/dd");
                if (record.BP_First_1_Systolic.HasValue || record.BP_First_1_Diastolic.HasValue)
                    charts.BloodPressureData.Add(new ChartPoint { Date = dateStr, Value = record.BP_First_1_Systolic, Value2 = record.BP_First_1_Diastolic, IsAbnormal = (record.BP_First_1_Systolic ?? 0) > 120 || (record.BP_First_1_Diastolic ?? 0) > 80 });
                if (record.BloodSugar.HasValue)
                    charts.BloodSugarData.Add(new ChartPoint { Date = dateStr, Value = record.BloodSugar, IsAbnormal = record.BloodSugar.Value > 99 });
                if (record.WaterIntake.HasValue)
                    charts.WaterIntakeData.Add(new ChartPoint { Date = dateStr, Value = record.WaterIntake, IsAbnormal = record.WaterIntake.Value < 2000 });
                if (record.ExerciseDuration.HasValue)
                    charts.ExerciseDurationData.Add(new ChartPoint { Date = dateStr, Value = record.ExerciseDuration, IsAbnormal = record.ExerciseDuration.Value < 150 });
                if (record.Cigarettes.HasValue && record.Cigarettes.Value > 0)
                    charts.CigarettesData.Add(new ChartPoint { Date = dateStr, Value = record.Cigarettes, IsAbnormal = true });
                if (record.BetelNut.HasValue && record.BetelNut.Value > 0)
                    charts.BetelNutData.Add(new ChartPoint { Date = dateStr, Value = record.BetelNut, IsAbnormal = true });
                if (!string.IsNullOrEmpty(record.MealsDisplay) && record.MealsDisplay != "未記錄")
                    charts.MealRecords.Add(new MealRecord { Date = dateStr, Meals = record.MealsDisplay });
                if (!string.IsNullOrEmpty(record.Beverage))
                    charts.BeverageRecords.Add(new BeverageRecord { Date = dateStr, Beverage = record.Beverage });
            }

            return charts;
        }
    }
}