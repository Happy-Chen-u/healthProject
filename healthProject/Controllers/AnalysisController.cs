using healthProject.Models;
using healthProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;


namespace healthProject.Controllers
{
    [Authorize]
    public class AnalysisController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AnalysisController> _logger;
        private readonly ReportService _reportService;

        public AnalysisController(
            IConfiguration configuration,
            ILogger<AnalysisController> logger,
            ReportService reportService)
        {
            _configuration = configuration;
            _logger = logger;
            _reportService = reportService;
        }

        // ========================================
        // 🏠 首頁 - 分流管理員/病患
        // ========================================
        public IActionResult Index()
        {
            if (User.IsInRole("Admin"))
            {
                return View("AdminAnalysis");
            }
            return View("PatientAnalysis");
        }

        // ========================================
        // 📊 病患查看自己的報表
        // ========================================
        [HttpPost]
        public async Task<IActionResult> GetPatientReport([FromBody] ReportRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await GetUserByIdAsync(userId);

                if (user == null)
                {
                    return Json(new { success = false, message = "找不到使用者資料" });
                }

                var analysis = await GenerateAnalysisAsync(
                    userId,
                    user.FullName,
                    user.IDNumber,
                    request.ReportType,
                    request.StartDate,
                    request.EndDate
                );

                return Json(new { success = true, data = analysis });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "產生報表失敗");
                return Json(new { success = false, message = "系統錯誤，請稍後再試" });
            }
        }

        // ========================================
        // 📊 管理員查看病患報表
        // ========================================
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> GetAdminReport([FromBody] AdminReportRequest request)
        {
            try
            {
                var patient = await GetPatientByIdNumberAsync(request.IDNumber);

                if (patient == null)
                {
                    return Json(new { success = false, message = "查無此病患" });
                }

                var analysis = await GenerateAnalysisAsync(
                    patient.Id,
                    patient.FullName,
                    patient.IDNumber,
                    request.ReportType,
                    request.StartDate,
                    request.EndDate
                );

                return Json(new { success = true, data = analysis });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "產生報表失敗");
                return Json(new { success = false, message = "系統錯誤" });
            }
        }

        // ========================================
        // 🔐 週報驗證頁面
        // ========================================
        [AllowAnonymous]
        [HttpGet]
        public IActionResult VerifyWeeklyReport(string reportId)
        {
            ViewBag.ReportId = reportId;
            return View();
        }

        // ========================================
        // 🔐 驗證身分證並下載週報
        // ========================================
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> VerifyAndDownloadWeeklyReport([FromBody] WeeklyReportRequest request)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // 查詢報表
                var query = @"
            SELECT wr.""PdfData"", wr.""UserId"", wr.""ExpiresAt"", u.""IDNumber"", u.""FullName""
            FROM ""WeeklyReports"" wr
            JOIN ""Users"" u ON wr.""UserId"" = u.""Id""
            WHERE wr.""Id"" = @ReportId";

                await using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ReportId", request.ReportDate); // 這裡用 ReportDate 欄位傳 reportId

                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Json(new { success = false, message = "找不到報表或連結已過期" });
                }

                var pdfData = (byte[])reader["PdfData"];
                var storedIdNumber = reader.GetString(reader.GetOrdinal("IDNumber"));
                var expiresAt = reader.GetDateTime(reader.GetOrdinal("ExpiresAt"));
                var fullName = reader.GetString(reader.GetOrdinal("FullName"));

                // 檢查是否過期
                if (DateTime.Now > expiresAt)
                {
                    return Json(new { success = false, message = "此報表連結已過期" });
                }

                // 驗證身分證
                if (request.IDNumber != storedIdNumber)
                {
                    return Json(new { success = false, message = "身分證字號驗證失敗" });
                }

                // 標記為已驗證
                await reader.CloseAsync();
                var updateQuery = @"
            UPDATE ""WeeklyReports""
            SET ""IsVerified"" = true
            WHERE ""Id"" = @ReportId";

                await using var updateCmd = new NpgsqlCommand(updateQuery, conn);
                updateCmd.Parameters.AddWithValue("@ReportId", request.ReportDate);
                await updateCmd.ExecuteNonQueryAsync();

                // 回傳 PDF 的 Base64
                var base64Pdf = Convert.ToBase64String(pdfData);

                return Json(new
                {
                    success = true,
                    message = "驗證成功",
                    pdfBase64 = base64Pdf,
                    fileName = $"週報_{fullName}_{DateTime.Now:yyyyMMdd}.pdf"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "週報驗證失敗");
                return Json(new { success = false, message = "系統錯誤" });
            }
        }


        // ========================================
        // 📥 下載 PDF 報表
        // ========================================
        [HttpGet]
        public async Task<IActionResult> DownloadPdf(int userId, string reportType, string startDate, string endDate)
        {
            try
            {
                // 權限檢查
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                if (!User.IsInRole("Admin") && currentUserId != userId)
                {
                    return Forbid();
                }

                var user = await GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound("找不到使用者");
                }

                var analysis = await GenerateAnalysisAsync(
                    userId,
                    user.FullName,
                    user.IDNumber,
                    Enum.Parse<ReportType>(reportType),
                    DateTime.Parse(startDate),
                    DateTime.Parse(endDate)
                );

                var pdfBytes = _reportService.GeneratePdfReport(analysis);
                var fileName = $"健康報表_{user.FullName}_{startDate}_{endDate}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下載 PDF 失敗");
                return BadRequest("產生 PDF 失敗");
            }
        }

        // ========================================
        // 🔍 產生分析報表
        // ========================================
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

        // ========================================
        // 📈 計算統計數據
        // ========================================
        private AnalysisStatistics CalculateStatistics(List<HealthRecordViewModel> records)
        {
            if (!records.Any())
            {
                return new AnalysisStatistics { TotalDays = 0 };
            }

            return new AnalysisStatistics
            {
                TotalDays = records.Count,

                // 平均值
                AvgSystolicBP = records.Where(r => r.SystolicBP.HasValue).Average(r => r.SystolicBP),
                AvgDiastolicBP = records.Where(r => r.DiastolicBP.HasValue).Average(r => r.DiastolicBP),
                AvgBloodSugar = records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar),
                AvgWaterIntake = records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake),
                AvgExerciseDuration = records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration),
                AvgCigarettes = records.Where(r => r.Cigarettes.HasValue).Average(r => r.Cigarettes),

                // 異常天數
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

        // ========================================
        // 📊 產生圖表數據
        // ========================================
        private ChartData GenerateChartData(List<HealthRecordViewModel> records, ReportType reportType)
        {
            var charts = new ChartData();

            foreach (var record in records.OrderBy(r => r.RecordDate))
            {
                var dateStr = FormatDateForChart(record.RecordDate, reportType);

                // 血壓數據
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

                // 三餐記錄
                if (!string.IsNullOrEmpty(record.Meals))
                {
                    charts.MealRecords.Add(new MealRecord
                    {
                        Date = record.RecordDate.ToString("MM/dd"),
                        Meals = record.Meals
                    });
                }

                // 飲料記錄
                if (!string.IsNullOrEmpty(record.Beverage))
                {
                    charts.BeverageRecords.Add(new BeverageRecord
                    {
                        Date = record.RecordDate.ToString("MM/dd"),
                        Beverage = record.Beverage
                    });
                }
            }

            return charts;
        }

        private string FormatDateForChart(DateTime date, ReportType reportType)
        {
            return reportType switch
            {
                ReportType.Daily => date.ToString("HH:mm"),
                ReportType.Weekly => date.ToString("MM/dd"),
                ReportType.Monthly => date.ToString("MM/dd"),
                ReportType.Yearly => date.ToString("yyyy/MM"),
                _ => date.ToString("MM/dd")
            };
        }

        // ========================================
        // 🗄️ 資料庫查詢
        // ========================================
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

        private async Task<UserDBModel> GetUserByIdAsync(int userId)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT ""Id"", ""FullName"", ""IDNumber"", ""LineUserId""
                FROM ""Users""
                WHERE ""Id"" = @UserId AND ""IsActive"" = true
                LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UserDBModel
                {
                    Id = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    IDNumber = reader.GetString(2),
                    LineUserId = reader.IsDBNull(3) ? null : reader.GetString(3)
                };
            }

            return null;
        }

        private async Task<UserDBModel> GetPatientByIdNumberAsync(string idNumber)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT ""Id"", ""FullName"", ""IDNumber"", ""LineUserId""
                FROM ""Users""
                WHERE ""IDNumber"" = @IDNumber AND ""IsActive"" = true
                LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@IDNumber", idNumber);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UserDBModel
                {
                    Id = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    IDNumber = reader.GetString(2),
                    LineUserId = reader.IsDBNull(3) ? null : reader.GetString(3)
                };
            }

            return null;
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
    }

    // 請求模型
    public class ReportRequest
    {
        public ReportType ReportType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class AdminReportRequest : ReportRequest
    {
        public string IDNumber { get; set; }
    }
}