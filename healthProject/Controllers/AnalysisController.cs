using healthProject.Models;
using healthProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;

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
                return Json(new { success = false, message = "系統錯誤,請稍後再試" });
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

                var query = @"
                    SELECT wr.""PdfData"", wr.""UserId"", wr.""ExpiresAt"", u.""IDNumber"", u.""FullName""
                    FROM ""WeeklyReports"" wr
                    JOIN ""Users"" u ON wr.""UserId"" = u.""Id""
                    WHERE wr.""Id"" = @ReportId";

                await using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ReportId", request.ReportDate);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Json(new { success = false, message = "找不到報表或連結已過期" });
                }

                var pdfData = (byte[])reader["PdfData"];
                var storedIdNumber = reader.GetString(reader.GetOrdinal("IDNumber"));
                var expiresAt = reader.GetDateTime(reader.GetOrdinal("ExpiresAt"));
                var fullName = reader.GetString(reader.GetOrdinal("FullName"));

                if (DateTime.Now > expiresAt)
                {
                    return Json(new { success = false, message = "此報表連結已過期" });
                }

                if (request.IDNumber != storedIdNumber)
                {
                    return Json(new { success = false, message = "身分證字號驗證失敗" });
                }

                await reader.CloseAsync();
                var updateQuery = @"
                    UPDATE ""WeeklyReports""
                    SET ""IsVerified"" = true
                    WHERE ""Id"" = @ReportId";

                await using var updateCmd = new NpgsqlCommand(updateQuery, conn);
                updateCmd.Parameters.AddWithValue("@ReportId", request.ReportDate);
                await updateCmd.ExecuteNonQueryAsync();

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
        // 📥 病患下載自己的分析 PDF（不需傳 userId）
        // ========================================
        [HttpPost]
        public async Task<IActionResult> DownloadAnalysisPdf([FromBody] ReportRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await GetUserByIdAsync(userId);

                if (user == null)
                    return Json(new { success = false, message = "找不到使用者資料" });

                var analysis = await GenerateAnalysisAsync(
                    userId,
                    user.FullName,
                    user.IDNumber,
                    request.ReportType,
                    request.StartDate,
                    request.EndDate
                );

                var pdfBytes = _reportService.GeneratePdfReport(analysis);
                var fileName = $"健康報表_{user.FullName}_{request.StartDate:yyyy-MM-dd}_{request.EndDate:yyyy-MM-dd}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下載分析 PDF 失敗");
                return BadRequest("產生 PDF 失敗");
            }
        }

        // ========================================
        // 📥 管理員下載病患分析 PDF
        // ========================================
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> DownloadAdminAnalysisPdf([FromBody] AdminReportRequest request)
        {
            try
            {
                var patient = await GetPatientByIdNumberAsync(request.IDNumber);

                if (patient == null)
                    return Json(new { success = false, message = "查無此病患" });

                var analysis = await GenerateAnalysisAsync(
                    patient.Id,
                    patient.FullName,
                    patient.IDNumber,
                    request.ReportType,
                    request.StartDate,
                    request.EndDate
                );

                var pdfBytes = _reportService.GeneratePdfReport(analysis);
                var fileName = $"健康報表_{patient.FullName}_{request.StartDate:yyyy-MM-dd}_{request.EndDate:yyyy-MM-dd}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下載管理員分析 PDF 失敗");
                return BadRequest("產生 PDF 失敗");
            }
        }

        // ========================================
        // 🔍 產生分析報表 (年報表特殊處理)
        // ========================================
        private async Task<AnalysisViewModel> GenerateAnalysisAsync(
    int userId,
    string fullName,
    string idNumber,
    ReportType reportType,
    DateTime startDate,
    DateTime endDate)
        {
            var (goals, gender, birthDate) = await GetPatientGoalsAsync(userId);

            // ✅ 統計用原始範圍
            var rawRecords = await GetRecordsInRangeAsync(userId, startDate, endDate);

            
            // ✅ 圖表用擴展範圍
            List<HealthRecordViewModel> chartRawRecords;
            if (reportType == ReportType.Daily)
            {
                chartRawRecords = rawRecords;
            }
            else if (reportType == ReportType.Yearly)
            {
                // 年報表只往前擴展，不往後，避免出現下一年資料
                var extendedStart = startDate.AddDays(-90);
                chartRawRecords = await GetRecordsInRangeAsync(userId, extendedStart, endDate);
            }
            else
            {
                // 週/月報表前後各擴展 90 天
                var extendedStart = startDate.AddDays(-90);
                var extendedEnd = endDate.AddDays(90);
                chartRawRecords = await GetRecordsInRangeAsync(userId, extendedStart, extendedEnd);
            }

            // 統計用的每日彙整
            var dailyGroups = rawRecords
                .GroupBy(r => r.RecordDate.Date)
                .Select(g => new DailyRecordGroup
                {
                    Date = g.Key,
                    Records = g.OrderBy(r => r.RecordTime).ToList()
                })
                .OrderBy(g => g.Date)
                .ToList();

            var aggregatedRecords = dailyGroups.Select(d => new HealthRecordViewModel
            {
                RecordDate = d.Date,
                BP_First_1_Systolic = d.AvgSystolicBP,
                BP_First_1_Diastolic = d.AvgDiastolicBP,
                BloodSugar = d.AvgBloodSugar,
                WaterIntake = d.TotalWater,
                ExerciseDuration = d.TotalExercise,
                Cigarettes = d.TotalCigarettes,
                BetelNut = d.TotalBetelNut,
                Meals_Breakfast = d.HasAnyMeals ? CreateDailyMealSummary(d, "Breakfast") : null,
                Meals_Lunch = d.HasAnyMeals ? CreateDailyMealSummary(d, "Lunch") : null,
                Meals_Dinner = d.HasAnyMeals ? CreateDailyMealSummary(d, "Dinner") : null,
                Beverage = string.Join(", ", d.Records
                    .Where(r => !string.IsNullOrEmpty(r.Beverage))
                    .Select(r => r.Beverage)
                    .Distinct())
            }).ToList();

            // ✅ 圖表用擴展範圍的彙整
            var chartDailyGroups = chartRawRecords
                .GroupBy(r => r.RecordDate.Date)
                .Select(g => new DailyRecordGroup
                {
                    Date = g.Key,
                    Records = g.OrderBy(r => r.RecordTime).ToList()
                })
                .OrderBy(g => g.Date)
                .ToList();

            var chartAggregatedRecords = chartDailyGroups.Select(d => new HealthRecordViewModel
            {
                RecordDate = d.Date,
                BP_First_1_Systolic = d.AvgSystolicBP,
                BP_First_1_Diastolic = d.AvgDiastolicBP,
                BloodSugar = d.AvgBloodSugar,
                WaterIntake = d.TotalWater,
                ExerciseDuration = d.TotalExercise,
                Cigarettes = d.TotalCigarettes,
                BetelNut = d.TotalBetelNut,
                Meals_Breakfast = d.HasAnyMeals ? CreateDailyMealSummary(d, "Breakfast") : null,
                Meals_Lunch = d.HasAnyMeals ? CreateDailyMealSummary(d, "Lunch") : null,
                Meals_Dinner = d.HasAnyMeals ? CreateDailyMealSummary(d, "Dinner") : null,
                Beverage = string.Join(", ", d.Records
                    .Where(r => !string.IsNullOrEmpty(r.Beverage))
                    .Select(r => r.Beverage)
                    .Distinct())
            }).ToList();

            // 統計用原始範圍
            var statistics = CalculateStatistics(aggregatedRecords, goals);

            // ✅ 圖表用擴展範圍
            ChartData charts;
            if (reportType == ReportType.Yearly)
            {
                var monthlyRecords = AggregateToMonthly(chartAggregatedRecords);
                charts = GenerateChartData(monthlyRecords, reportType, goals);
                charts.MealRecords = GenerateDailyMealRecords(aggregatedRecords);
                charts.BeverageRecords = GenerateDailyBeverageRecords(aggregatedRecords);
                charts.YearlyMealSummary = CalculateMealStatistics(aggregatedRecords);
            }
            else
            {
                charts = GenerateChartData(chartAggregatedRecords, reportType, goals);
                charts.WeeklyMealSummary = CalculateMealStatistics(aggregatedRecords);
                charts.MonthlyMealSummary = CalculateMealStatistics(aggregatedRecords);
            }

            return new AnalysisViewModel
            {
                PatientName = fullName,
                IDNumber = idNumber,
                PatientGender = gender,
                PatientBirthDate = birthDate,
                ReportType = reportType,
                StartDate = startDate,
                EndDate = endDate,
                Statistics = statistics,
                Records = aggregatedRecords,
                Charts = charts,
                Goals = goals,
                TrendSummary = await GenerateTrendSummaryAsync(userId, reportType, startDate, endDate, statistics, goals)
            };
        }

        private async Task<TrendSummary> GenerateTrendSummaryAsync(
    int userId,
    ReportType reportType,
    DateTime startDate,
    DateTime endDate,
    AnalysisStatistics currentStats,
    PatientGoals goals)
        {
            // 計算上一期的日期範圍
            var span = (endDate - startDate).Days + 1;
            var prevStart = startDate.AddDays(-span);
            var prevEnd = startDate.AddDays(-1);

            var prevRawRecords = await GetRecordsInRangeAsync(userId, prevStart, prevEnd);
            if (!prevRawRecords.Any())
                return null;

            var prevDailyGroups = prevRawRecords
                .GroupBy(r => r.RecordDate.Date)
                .Select(g => new DailyRecordGroup
                {
                    Date = g.Key,
                    Records = g.OrderBy(r => r.RecordTime).ToList()
                }).ToList();

            var prevAggregated = prevDailyGroups.Select(d => new HealthRecordViewModel
            {
                RecordDate = d.Date,
                BP_First_1_Systolic = d.AvgSystolicBP,
                BP_First_1_Diastolic = d.AvgDiastolicBP,
                BloodSugar = d.AvgBloodSugar,
                WaterIntake = d.TotalWater,
                ExerciseDuration = d.TotalExercise,
                Cigarettes = d.TotalCigarettes,
                BetelNut = d.TotalBetelNut,
            }).ToList();

            var prevStats = CalculateStatistics(prevAggregated, goals);

            var items = new List<TrendItem>();

            // 血壓
            if (currentStats.AvgSystolicBP.HasValue && prevStats.AvgSystolicBP.HasValue)
            {
                var diff = (double)(currentStats.AvgSystolicBP.Value - prevStats.AvgSystolicBP.Value);
                items.Add(new TrendItem
                {
                    Label = "血壓（收縮壓）",
                    Icon = "❤️",
                    CurrentValue = $"{currentStats.AvgSystolicBP:F0}/{currentStats.AvgDiastolicBP:F0} mmHg",
                    PrevValue = $"{prevStats.AvgSystolicBP:F0}/{prevStats.AvgDiastolicBP:F0} mmHg",
                    DiffText = diff == 0 ? "持平" : $"{(diff > 0 ? "↑" : "↓")} {Math.Abs(diff):F0} mmHg",
                    TrendType = diff <= 0 ? "good" : (diff <= 5 ? "warn" : "bad"),
                    Message = diff <= -3 ? "血壓明顯改善，繼續保持！" :
                              diff <= 0 ? "血壓持平或略降，表現不錯。" :
                              diff <= 5 ? "血壓略有上升，請注意飲食與鹽分攝取。" :
                              "血壓明顯上升，建議諮詢醫師。"
                });
            }

            // 血糖
            if (currentStats.AvgBloodSugar.HasValue && prevStats.AvgBloodSugar.HasValue)
            {
                var diff = (double)(currentStats.AvgBloodSugar.Value - prevStats.AvgBloodSugar.Value);
                items.Add(new TrendItem
                {
                    Label = "血糖",
                    Icon = "🩸",
                    CurrentValue = $"{currentStats.AvgBloodSugar:F1} mg/dL",
                    PrevValue = $"{prevStats.AvgBloodSugar:F1} mg/dL",
                    DiffText = diff == 0 ? "持平" : $"{(diff > 0 ? "↑" : "↓")} {Math.Abs(diff):F1} mg/dL",
                    TrendType = diff <= 0 ? "good" : (diff <= 5 ? "warn" : "bad"),
                    Message = diff <= -3 ? "血糖控制進步，很棒！" :
                              diff <= 0 ? "血糖維持穩定。" :
                              diff <= 5 ? "血糖略升，注意甜食攝取。" :
                              "血糖明顯上升，建議回診確認。"
                });
            }

            // 飲水量
            if (currentStats.AvgWaterIntake.HasValue && prevStats.AvgWaterIntake.HasValue)
            {
                var diff = (double)(currentStats.AvgWaterIntake.Value - prevStats.AvgWaterIntake.Value);
                items.Add(new TrendItem
                {
                    Label = "飲水量",
                    Icon = "💧",
                    CurrentValue = $"{currentStats.AvgWaterIntake:F0} ml",
                    PrevValue = $"{prevStats.AvgWaterIntake:F0} ml",
                    DiffText = diff == 0 ? "持平" : $"{(diff > 0 ? "↑" : "↓")} {Math.Abs(diff):F0} ml",
                    TrendType = diff >= 0 ? "good" : (diff >= -200 ? "warn" : "bad"),
                    Message = diff >= 200 ? "飲水量明顯增加，很棒！" :
                              diff >= 0 ? "飲水量維持或小幅提升。" :
                              diff >= -200 ? "飲水量略有減少，記得多喝水。" :
                              "飲水量明顯減少，請養成規律補水習慣。"
                });
            }

            // 運動時間
            if (currentStats.AvgExerciseDuration.HasValue && prevStats.AvgExerciseDuration.HasValue)
            {
                var diff = (double)(currentStats.AvgExerciseDuration.Value - prevStats.AvgExerciseDuration.Value);
                items.Add(new TrendItem
                {
                    Label = "運動時間",
                    Icon = "🏃",
                    CurrentValue = $"{currentStats.AvgExerciseDuration:F0} 分鐘",
                    PrevValue = $"{prevStats.AvgExerciseDuration:F0} 分鐘",
                    DiffText = diff == 0 ? "持平" : $"{(diff > 0 ? "↑" : "↓")} {Math.Abs(diff):F0} 分鐘",
                    TrendType = diff >= 0 ? "good" : (diff >= -15 ? "warn" : "bad"),
                    Message = diff >= 30 ? "運動量大幅提升，非常棒！" :
                              diff >= 0 ? "運動量維持或小幅增加。" :
                              diff >= -15 ? "運動時間略有減少，試著維持規律運動。" :
                              "運動時間明顯減少，建議重新安排運動計畫。"
                });
            }

            return new TrendSummary
            {
                PeriodLabel = reportType == ReportType.Weekly ? "上週" :
                              reportType == ReportType.Monthly ? "上月" : "去年",
                Items = items
            };
        }

        private async Task<(PatientGoals goals, string gender, DateTime? birthDate)> GetPatientGoalsAsync(int userId)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // 抓最新一筆 CaseManagement
            var query = @"
        SELECT 
            ""Gender"", ""BirthDate"",
            ""SystolicBP"", ""SystolicBP_Value"",
            ""DiastolicBP"", ""DiastolicBP_Value"",
            ""FastingGlucoseTarget"", ""FastingGlucoseTarget_Value"",
            ""HbA1cTarget"", ""HbA1cTarget_Value"",
            ""TriglyceridesTarget"", ""TriglyceridesTarget_Value"",
            ""HDL_CholesterolTarget"", ""HDL_CholesterolTarget_Value"",
            ""LDL_CholesterolTarget"", ""LDL_CholesterolTarget_Value"",
            ""WaistTarget_Value"", ""WeightTarget_Value""
        FROM ""CaseManagement""
        WHERE ""UserId"" = @UserId
        ORDER BY ""Id"" DESC
        LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var reader = await cmd.ExecuteReaderAsync();

            var goals = new PatientGoals();
            string gender = null;
            DateTime? birthDate = null;

            if (await reader.ReadAsync())
            {
                gender = reader.IsDBNull(reader.GetOrdinal("Gender")) ? null : reader.GetString(reader.GetOrdinal("Gender"));
                birthDate = reader.IsDBNull(reader.GetOrdinal("BirthDate")) ? null : reader.GetDateTime(reader.GetOrdinal("BirthDate"));

                // 血壓目標：有勾選且有值才用，否則用預設
                bool hasSysBP = !reader.IsDBNull(reader.GetOrdinal("SystolicBP")) && reader.GetBoolean(reader.GetOrdinal("SystolicBP"));
                goals.SystolicBPTarget = hasSysBP && !reader.IsDBNull(reader.GetOrdinal("SystolicBP_Value"))
                    ? reader.GetDecimal(reader.GetOrdinal("SystolicBP_Value")) : 130;

                bool hasDiaBP = !reader.IsDBNull(reader.GetOrdinal("DiastolicBP")) && reader.GetBoolean(reader.GetOrdinal("DiastolicBP"));
                goals.DiastolicBPTarget = hasDiaBP && !reader.IsDBNull(reader.GetOrdinal("DiastolicBP_Value"))
                    ? reader.GetDecimal(reader.GetOrdinal("DiastolicBP_Value")) : 80;

                // 血糖目標
                bool hasGlucose = !reader.IsDBNull(reader.GetOrdinal("FastingGlucoseTarget")) && reader.GetBoolean(reader.GetOrdinal("FastingGlucoseTarget"));
                goals.FastingGlucoseTarget = hasGlucose && !reader.IsDBNull(reader.GetOrdinal("FastingGlucoseTarget_Value"))
                    ? reader.GetDecimal(reader.GetOrdinal("FastingGlucoseTarget_Value")) : 100;

                // HbA1c
                bool hasHbA1c = !reader.IsDBNull(reader.GetOrdinal("HbA1cTarget")) && reader.GetBoolean(reader.GetOrdinal("HbA1cTarget"));
                goals.HbA1cTarget = hasHbA1c && !reader.IsDBNull(reader.GetOrdinal("HbA1cTarget_Value"))
                    ? reader.GetDecimal(reader.GetOrdinal("HbA1cTarget_Value")) : null;

                // 三酸甘油酯
                bool hasTG = !reader.IsDBNull(reader.GetOrdinal("TriglyceridesTarget")) && reader.GetBoolean(reader.GetOrdinal("TriglyceridesTarget"));
                goals.TriglyceridesTarget = hasTG && !reader.IsDBNull(reader.GetOrdinal("TriglyceridesTarget_Value"))
                    ? reader.GetDecimal(reader.GetOrdinal("TriglyceridesTarget_Value")) : null;

                // HDL
                bool hasHDL = !reader.IsDBNull(reader.GetOrdinal("HDL_CholesterolTarget")) && reader.GetBoolean(reader.GetOrdinal("HDL_CholesterolTarget"));
                goals.HDLTarget = hasHDL && !reader.IsDBNull(reader.GetOrdinal("HDL_CholesterolTarget_Value"))
                    ? reader.GetDecimal(reader.GetOrdinal("HDL_CholesterolTarget_Value")) : null;

                // LDL
                bool hasLDL = !reader.IsDBNull(reader.GetOrdinal("LDL_CholesterolTarget")) && reader.GetBoolean(reader.GetOrdinal("LDL_CholesterolTarget"));
                goals.LDLTarget = hasLDL && !reader.IsDBNull(reader.GetOrdinal("LDL_CholesterolTarget_Value"))
                    ? reader.GetDecimal(reader.GetOrdinal("LDL_CholesterolTarget_Value")) : null;

                // 腰圍體重目標
                goals.WaistTarget = reader.IsDBNull(reader.GetOrdinal("WaistTarget_Value")) ? null : reader.GetDecimal(reader.GetOrdinal("WaistTarget_Value"));
                goals.WeightTarget = reader.IsDBNull(reader.GetOrdinal("WeightTarget_Value")) ? null : reader.GetDecimal(reader.GetOrdinal("WeightTarget_Value"));
            }
            else
            {
                // 沒有 CaseManagement 紀錄，用預設值
                goals.SystolicBPTarget = 130;
                goals.DiastolicBPTarget = 80;
                goals.FastingGlucoseTarget = 100;
            }

            return (goals, gender, birthDate);
        }
        private List<HealthRecordViewModel> AggregateToMonthly(List<HealthRecordViewModel> dailyRecords)
        {
            var monthlyRecords = new List<HealthRecordViewModel>();

            var groupedByMonth = dailyRecords
                .GroupBy(r => new { r.RecordDate.Year, r.RecordDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

            foreach (var monthGroup in groupedByMonth)
            {
                var records = monthGroup.ToList();
                var firstDay = new DateTime(monthGroup.Key.Year, monthGroup.Key.Month, 1);

                monthlyRecords.Add(new HealthRecordViewModel
                {
                    RecordDate = firstDay,

                    // 血壓平均
                    BP_First_1_Systolic = records.Any(r => r.BP_First_1_Systolic.HasValue)
                        ? records.Where(r => r.BP_First_1_Systolic.HasValue).Average(r => r.BP_First_1_Systolic.Value)
                        : null,
                    BP_First_1_Diastolic = records.Any(r => r.BP_First_1_Diastolic.HasValue)
                        ? records.Where(r => r.BP_First_1_Diastolic.HasValue).Average(r => r.BP_First_1_Diastolic.Value)
                        : null,

                    // 血糖平均
                    BloodSugar = records.Any(r => r.BloodSugar.HasValue)
                        ? records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar.Value)
                        : null,

                    // 飲水平均
                    WaterIntake = records.Any(r => r.WaterIntake.HasValue)
                        ? records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake.Value)
                        : null,

                    // 運動平均
                    ExerciseDuration = records.Any(r => r.ExerciseDuration.HasValue)
                        ? records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration.Value)
                        : null,

                    // 🆕 抽菸平均
                    Cigarettes = records.Any(r => r.Cigarettes.HasValue)
                        ? records.Where(r => r.Cigarettes.HasValue).Average(r => r.Cigarettes.Value)
                        : null,

                    // 🆕 檳榔平均
                    BetelNut = records.Any(r => r.BetelNut.HasValue)
                        ? records.Where(r => r.BetelNut.HasValue).Average(r => r.BetelNut.Value)
                        : null
                });
            }

            return monthlyRecords;
        }



        // ========================================
        // 🆕 產生每日三餐記錄 (給年報表用)
        // ========================================
        private List<MealRecord> GenerateDailyMealRecords(List<HealthRecordViewModel> records)
        {
            var mealRecords = new List<MealRecord>();

            foreach (var record in records.Where(r =>
                r.Meals_Breakfast != null || r.Meals_Lunch != null || r.Meals_Dinner != null))
            {
                // ✅ 計算當天三餐總和
                decimal totalVeg = 0, totalProt = 0, totalCarb = 0;

                foreach (var meal in new[] { record.Meals_Breakfast, record.Meals_Lunch, record.Meals_Dinner })
                {
                    if (meal != null)
                    {
                        if (decimal.TryParse(meal.Vegetables, out decimal v)) totalVeg += v;
                        if (decimal.TryParse(meal.Protein, out decimal p)) totalProt += p;
                        if (decimal.TryParse(meal.Carbs, out decimal c)) totalCarb += c;
                    }
                }

                mealRecords.Add(new MealRecord
                {
                    Date = record.RecordDate.ToString("yyyy/MM/dd"),
                    MealData = new MealStatistics
                    {
                        // ✅ 直接顯示總和,不是算式
                        Vegetables = totalVeg > 0 ? new List<string> { totalVeg.ToString("0.#") } : new List<string>(),
                        Protein = totalProt > 0 ? new List<string> { totalProt.ToString("0.#") } : new List<string>(),
                        Carbs = totalCarb > 0 ? new List<string> { totalCarb.ToString("0.#") } : new List<string>()
                    }
                });
            }

            return mealRecords;
        }

        // ========================================
        // 🆕 產生每日飲料記錄 (給年報表用)
        // ========================================
        private List<BeverageRecord> GenerateDailyBeverageRecords(List<HealthRecordViewModel> records)
        {
            return records
                .Where(r => !string.IsNullOrEmpty(r.Beverage))
                .Select(r => new BeverageRecord
                {
                    Date = r.RecordDate.ToString("yyyy/MM/dd"),
                    Beverage = r.Beverage
                })
                .ToList();
        }



        // ========================================
        // 🆕 產生每日三餐顯示文字
        // ========================================
        private string GetDailyMealsDisplay(DailyRecordGroup dailyGroup)
        {
            var parts = new List<string>();

            // 蔬菜
            var veggies = dailyGroup.TotalVegetables;
            if (veggies.NumericTotal > 0 || veggies.OtherTexts.Any())
                parts.Add($"🥬蔬菜:{veggies.Display}");

            // 蛋白質
            var protein = dailyGroup.TotalProtein;
            if (protein.NumericTotal > 0 || protein.OtherTexts.Any())
                parts.Add($"🥩蛋白質:{protein.Display}");

            // 澱粉
            var carbs = dailyGroup.TotalCarbs;
            if (carbs.NumericTotal > 0 || carbs.OtherTexts.Any())
                parts.Add($"🍚澱粉:{carbs.Display}");

            return parts.Any() ? string.Join(", ", parts) : "未記錄";
        }

        //下載報表
        [HttpGet]
        [AllowAnonymous] // 允許未登入使用者存取
        public async Task<IActionResult> DownloadWeeklyReport(string reportId)
        {
            try
            {
                if (string.IsNullOrEmpty(reportId))
                {
                    return NotFound("報表 ID 不正確");
                }

                // 從資料庫取得 PDF
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
            SELECT ""PdfData"", ""StartDate"", ""EndDate"", ""ExpiresAt"", ""UserId""
            FROM ""WeeklyReports""
            WHERE ""Id"" = @ReportId";

                await using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ReportId", reportId);
                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return NotFound("找不到報表或報表已過期");
                }

                // 檢查是否過期
                var expiresAt = reader.GetDateTime(3);
                if (DateTime.Now > expiresAt)
                {
                    return BadRequest("報表已過期");
                }

                var pdfData = (byte[])reader["PdfData"];
                var startDate = reader.GetDateTime(1);
                var endDate = reader.GetDateTime(2);

                // 產生檔案名稱
                var fileName = $"健康週報_{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.pdf";

                // 返回 PDF 檔案
                return File(pdfData, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下載週報失敗");
                return StatusCode(500, "下載失敗,請稍後再試");
            }
        }

        //有改的
        [HttpPost]
        public async Task<IActionResult> TestWeeklyReport()
        {
            try
            {
                // 1️ 取得登入使用者 ID (修正)
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Json(new { success = false, message = "無法取得使用者資訊,請重新登入" });
                }

                var userId = int.Parse(userIdClaim);

                // 2️ 查出使用者資料
                var user = await GetUserByIdAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "找不到使用者資料" });
                }

                // 3️ 檢查是否已綁定 LINE
                if (string.IsNullOrEmpty(user.LineUserId))
                {
                    return Json(new { success = false, message = "您尚未綁定 LINE 帳號,無法傳送週報" });
                }

                // 4️ 計算上週日期 (週一到週日)
                var today = DateTime.Today;
                var dayOfWeek = (int)today.DayOfWeek;

                // 計算上週一
                var lastMonday = today.AddDays(-(dayOfWeek == 0 ? 13 : dayOfWeek + 6));
                // 上週日
                var lastSunday = lastMonday.AddDays(6);

                _logger.LogInformation($"準備產生週報: {user.FullName} ({lastMonday:yyyy-MM-dd} ~ {lastSunday:yyyy-MM-dd})");

                // 5️ 呼叫服務產生週報 PDF 並傳 LINE
                var scheduledJobService = HttpContext.RequestServices.GetRequiredService<ScheduledJobService>();
                var baseUrl = _configuration["AppSettings:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                await scheduledJobService.SendWeeklyReportToUserAsync(user, lastMonday, lastSunday, baseUrl);
                return Json(new
                {
                    success = true,
                    message = $"週報已成功傳送!\n\n期間: {lastMonday:yyyy-MM-dd} ~ {lastSunday:yyyy-MM-dd}\n請檢查您的 LINE 訊息。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "測試報表失敗");
                return Json(new { success = false, message = $"傳送失敗: {ex.Message}" });
            }
        }
        private async Task<UserDBModel> GetUserById(int userId)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
SELECT ""Id"", ""FullName"", ""IDNumber"", ""LineUserId""
FROM ""Users""
WHERE ""Id"" = @UserId";

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

            throw new Exception("找不到使用者");
        }


        // ========================================
        // 📈 計算統計數據
        // ========================================
        private AnalysisStatistics CalculateStatistics(List<HealthRecordViewModel> records, PatientGoals goals)
{
    if (!records.Any())
        return new AnalysisStatistics { TotalDays = 0 };

    var bpRecords = records.Where(r =>
        r.BP_First_1_Systolic.HasValue || r.BP_First_1_Diastolic.HasValue).ToList();

    var mealStats = CalculateMealStatistics(records);
    var totalCigs = records.Where(r => r.Cigarettes.HasValue).Sum(r => r.Cigarettes.Value);
    var totalBetel = records.Where(r => r.BetelNut.HasValue).Sum(r => r.BetelNut.Value);

    // ✅ 這兩行要在 return 之前宣告
    var smokingRecords = records.Where(r => r.Cigarettes.HasValue).ToList();
    var betelRecords = records.Where(r => r.BetelNut.HasValue).ToList();

    decimal sysBPLimit = goals?.SystolicBPTarget ?? 130;
    decimal diaBPLimit = goals?.DiastolicBPTarget ?? 80;
    decimal glucoseLimit = goals?.FastingGlucoseTarget ?? 100;
    decimal waterLimit = goals?.WaterTarget ?? 2000;

    return new AnalysisStatistics
    {
        TotalDays = records.Count,

        AvgSystolicBP = bpRecords.Any(r => r.BP_First_1_Systolic.HasValue)
            ? bpRecords.Where(r => r.BP_First_1_Systolic.HasValue).Average(r => r.BP_First_1_Systolic.Value) : null,
        AvgDiastolicBP = bpRecords.Any(r => r.BP_First_1_Diastolic.HasValue)
            ? bpRecords.Where(r => r.BP_First_1_Diastolic.HasValue).Average(r => r.BP_First_1_Diastolic.Value) : null,

        AvgBloodSugar = records.Any(r => r.BloodSugar.HasValue)
            ? records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar.Value) : null,

        AvgWaterIntake = records.Any(r => r.WaterIntake.HasValue)
            ? records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake.Value) : null,

        AvgExerciseDuration = records.Any(r => r.ExerciseDuration.HasValue)
            ? records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration.Value) : null,

        TotalCigarettes = totalCigs,
        AvgCigarettes = smokingRecords.Any() ? totalCigs / smokingRecords.Count : (decimal?)null,
        SmokingDays = records.Count(r => r.Cigarettes.HasValue && r.Cigarettes.Value > 0),

        TotalBetelNut = totalBetel,
        AvgBetelNut = betelRecords.Any() ? totalBetel / betelRecords.Count : (decimal?)null,
        BetelNutDays = records.Count(r => r.BetelNut.HasValue && r.BetelNut.Value > 0),

        AvgVegetables = mealStats.AvgVegetables,
        AvgProtein = mealStats.AvgProtein,
        AvgCarbs = mealStats.AvgCarbs,

        HighBPDays = records.Count(r =>
            (r.BP_First_1_Systolic.HasValue && r.BP_First_1_Systolic.Value > sysBPLimit) ||
            (r.BP_First_1_Diastolic.HasValue && r.BP_First_1_Diastolic.Value > diaBPLimit)),

        HighBloodSugarDays = records.Count(r =>
            r.BloodSugar.HasValue && r.BloodSugar.Value > glucoseLimit),

        LowWaterDays = records.Count(r =>
            r.WaterIntake.HasValue && r.WaterIntake.Value < waterLimit),

        LowExerciseDays = records.Count(r =>
            r.ExerciseDuration.HasValue && r.ExerciseDuration.Value < 150)
    };
}

        //  計算三餐統計
        private MealSummary CalculateMealStatistics(List<HealthRecordViewModel> records)
        {
            var totalVeg = 0m;
            var totalProtein = 0m;
            var totalCarbs = 0m;

            // ✅ 有記錄三餐的天數
            int daysWithMeals = 0;

            foreach (var record in records)
            {
                bool hasMeal = false;
                decimal dayVeg = 0, dayProt = 0, dayCarbs = 0;

                foreach (var meal in new[] { record.Meals_Breakfast, record.Meals_Lunch, record.Meals_Dinner })
                {
                    if (meal == null) continue;
                    hasMeal = true;

                    if (!string.IsNullOrEmpty(meal.Vegetables) && decimal.TryParse(meal.Vegetables, out var veg))
                        dayVeg += veg;
                    if (!string.IsNullOrEmpty(meal.Protein) && decimal.TryParse(meal.Protein, out var protein))
                        dayProt += protein;
                    if (!string.IsNullOrEmpty(meal.Carbs) && decimal.TryParse(meal.Carbs, out var carbs))
                        dayCarbs += carbs;
                }

                if (hasMeal)
                {
                    daysWithMeals++;
                    totalVeg += dayVeg;
                    totalProtein += dayProt;
                    totalCarbs += dayCarbs;
                }
            }

            return new MealSummary
            {
                TotalVegetables = totalVeg,
                TotalProtein = totalProtein,
                TotalCarbs = totalCarbs,
                // ✅ 平均 = 總量 ÷ 有記錄天數（不是總天數）
                AvgVegetables = daysWithMeals > 0 ? totalVeg / daysWithMeals : 0,
                AvgProtein = daysWithMeals > 0 ? totalProtein / daysWithMeals : 0,
                AvgCarbs = daysWithMeals > 0 ? totalCarbs / daysWithMeals : 0,
                DaysWithMeals = daysWithMeals
            };
        }

        // ========================================
        //  建立每日三餐統計摘要
        // ========================================
        private MealSelection CreateDailyMealSummary(DailyRecordGroup dailyGroup, string mealType)
        {
            // 收集當天該餐的所有記錄
            var meals = mealType switch
            {
                "Breakfast" => dailyGroup.Records.Where(r => r.Meals_Breakfast != null).Select(r => r.Meals_Breakfast).ToList(),
                "Lunch" => dailyGroup.Records.Where(r => r.Meals_Lunch != null).Select(r => r.Meals_Lunch).ToList(),
                "Dinner" => dailyGroup.Records.Where(r => r.Meals_Dinner != null).Select(r => r.Meals_Dinner).ToList(),
                _ => new List<MealSelection>()
            };

            if (!meals.Any()) return null;

            // 如果只有一筆,直接回傳
            if (meals.Count == 1) return meals[0];

            // 合併多筆記錄
            return new MealSelection
            {
                Vegetables = CombineMealItem(meals.Select(m => m.Vegetables)),
                Protein = CombineMealItem(meals.Select(m => m.Protein)),
                Carbs = CombineMealItem(meals.Select(m => m.Carbs))
            };
        }


        // ========================================
        //  修改：合併三餐項目，計算總和而不是顯示算式
        // ========================================
        private string CombineMealItem(IEnumerable<string> items)
        {
            var validItems = items.Where(i => !string.IsNullOrEmpty(i) && i != "0").ToList();
            if (!validItems.Any()) return "0";

            decimal total = 0;
            var otherTexts = new List<string>();

            foreach (var item in validItems)
            {
                // 處理帶 "+" 號的算式 (例如: "1+1.5")
                if (item.Contains("+"))
                {
                    var parts = item.Split('+');
                    foreach (var part in parts)
                    {
                        if (decimal.TryParse(part.Trim(), out decimal value))
                        {
                            total += value;
                        }
                        else
                        {
                            otherTexts.Add(part.Trim());
                        }
                    }
                }
                // 單純數值
                else if (decimal.TryParse(item, out decimal value))
                {
                    total += value;
                }
                // 文字描述
                else
                {
                    otherTexts.Add(item);
                }
            }

            // ✅ 組合結果：直接顯示總和
            var resultParts = new List<string>(); // ← 改名
            if (total > 0)
            {
                // 如果是整數就不顯示小數點，否則最多顯示一位小數
                resultParts.Add(total % 1 == 0 ? total.ToString("0") : total.ToString("0.#"));
            }
            resultParts.AddRange(otherTexts.Distinct());

            return string.Join(" + ", resultParts);
        }



        // ========================================
        // 📊 產生圖表數據 
        // ========================================
        private ChartData GenerateChartData(List<HealthRecordViewModel> records, ReportType reportType, PatientGoals goals)
        {
            var charts = new ChartData();

            decimal sysBPLimit = goals?.SystolicBPTarget ?? 130;
            decimal diaBPLimit = goals?.DiastolicBPTarget ?? 80;
            decimal glucoseLimit = goals?.FastingGlucoseTarget ?? 100;
            decimal waterLimit = goals?.WaterTarget ?? 2000;

            foreach (var record in records.OrderBy(r => r.RecordDate))
            {
                var dateStr = FormatDateForChart(record.RecordDate, reportType);

                if (record.BP_First_1_Systolic.HasValue || record.BP_First_1_Diastolic.HasValue)
                {
                    charts.BloodPressureData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BP_First_1_Systolic,
                        Value2 = record.BP_First_1_Diastolic,
                        // ✅ 用個人目標值
                        IsAbnormal = (record.BP_First_1_Systolic ?? 0) > sysBPLimit ||
                                     (record.BP_First_1_Diastolic ?? 0) > diaBPLimit
                    });
                }

                if (record.BloodSugar.HasValue)
                {
                    charts.BloodSugarData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BloodSugar,
                        IsAbnormal = record.BloodSugar.Value > glucoseLimit
                    });
                }

                if (record.WaterIntake.HasValue)
                {
                    charts.WaterIntakeData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.WaterIntake,
                        IsAbnormal = record.WaterIntake.Value < waterLimit
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

                if (record.Cigarettes.HasValue)
{
                    charts.CigarettesData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.Cigarettes,
                        IsAbnormal = record.Cigarettes.Value > 0
                    });
                }

                if (record.BetelNut.HasValue)
                {
                    charts.BetelNutData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BetelNut,
                        IsAbnormal = record.BetelNut.Value > 0
                    });
                }

                // 三餐（非年報表）
                if (reportType != ReportType.Yearly &&
                    (record.Meals_Breakfast != null || record.Meals_Lunch != null || record.Meals_Dinner != null))
                {
                    decimal totalVeg = 0, totalProt = 0, totalCarb = 0;
                    foreach (var meal in new[] { record.Meals_Breakfast, record.Meals_Lunch, record.Meals_Dinner })
                    {
                        if (meal != null)
                        {
                            if (decimal.TryParse(meal.Vegetables, out decimal v)) totalVeg += v;
                            if (decimal.TryParse(meal.Protein, out decimal p)) totalProt += p;
                            if (decimal.TryParse(meal.Carbs, out decimal c)) totalCarb += c;
                        }
                    }

                    charts.MealRecords.Add(new MealRecord
                    {
                        Date = record.RecordDate.ToString("MM/dd"),
                        MealData = new MealStatistics
                        {
                            Vegetables = totalVeg > 0 ? new List<string> { totalVeg.ToString("0.#") } : new List<string>(),
                            Protein = totalProt > 0 ? new List<string> { totalProt.ToString("0.#") } : new List<string>(),
                            Carbs = totalCarb > 0 ? new List<string> { totalCarb.ToString("0.#") } : new List<string>()
                        }
                    });
                }

                if (reportType != ReportType.Yearly && !string.IsNullOrEmpty(record.Beverage))
                {
                    charts.BeverageRecords.Add(new BeverageRecord
                    {
                        Date = record.RecordDate.ToString("MM/dd"),
                        Beverage = record.Beverage
                    });
                }
            }

            charts.WeeklyMealSummary = CalculateMealStatistics(records);
            charts.MonthlyMealSummary = CalculateMealStatistics(records);
            charts.YearlyMealSummary = CalculateMealStatistics(records);

            return charts;
        }

        private string FormatDateForChart(DateTime date, ReportType reportType)
        {
            return reportType switch
            {
                ReportType.Daily => date.ToString("HH:mm"),
                ReportType.Weekly => date.ToString("yyyy-MM-dd"),   
                ReportType.Monthly => date.ToString("yyyy-MM-dd"),  
                ReportType.Yearly => date.ToString("yyyy-MM"),      
                _ => date.ToString("yyyy-MM-dd")
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
                ORDER BY ""RecordDate"" ASC, ""RecordTime"" ASC";

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

        // ========================================
        // 新增輔助方法：計算三餐項目總和
        // ========================================
        private string CalculateMealTotal(List<string> items)
        {
            if (!items.Any()) return null;

            decimal total = 0;
            var otherTexts = new List<string>();

            foreach (var item in items)
            {
                // 處理帶 "+" 號的算式
                if (item.Contains("+"))
                {
                    var parts = item.Split('+');
                    foreach (var part in parts)
                    {
                        if (decimal.TryParse(part.Trim(), out decimal value))
                        {
                            total += value;
                        }
                        else
                        {
                            otherTexts.Add(part.Trim());
                        }
                    }
                }
                // 單純數值
                else if (decimal.TryParse(item, out decimal value))
                {
                    total += value;
                }
                // 文字描述
                else
                {
                    otherTexts.Add(item);
                }
            }

            // 組合結果
            var result = new List<string>();
            if (total > 0)
            {
                result.Add(total % 1 == 0 ? total.ToString("0") : total.ToString("0.#"));
            }
            result.AddRange(otherTexts.Distinct());

            return result.Any() ? string.Join(" + ", result) : null;
        }

        // ========================================
        // ✅ MapFromReader (支援新的血壓與三餐欄位)
        // ========================================
        private HealthRecordViewModel MapFromReader(NpgsqlDataReader reader)
        {
            var model = new HealthRecordViewModel
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                RecordDate = reader.GetDateTime(reader.GetOrdinal("RecordDate")),
                RecordTime = reader.IsDBNull(reader.GetOrdinal("RecordTime"))
                    ? null
                    : reader.GetTimeSpan(reader.GetOrdinal("RecordTime")),

                //  血壓資料 - 8個欄位
                BP_First_1_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_First_1_Systolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_1_Systolic")),
                BP_First_1_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_First_1_Diastolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_1_Diastolic")),

                BP_First_2_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_First_2_Systolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_2_Systolic")),
                BP_First_2_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_First_2_Diastolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_First_2_Diastolic")),

                BP_Second_1_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_1_Systolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_1_Systolic")),
                BP_Second_1_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_1_Diastolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_1_Diastolic")),

                BP_Second_2_Systolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_2_Systolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_2_Systolic")),
                BP_Second_2_Diastolic = reader.IsDBNull(reader.GetOrdinal("BP_Second_2_Diastolic"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BP_Second_2_Diastolic")),

                // 其他欄位
                ExerciseType = reader.IsDBNull(reader.GetOrdinal("ExerciseType"))
                    ? null : reader.GetString(reader.GetOrdinal("ExerciseType")),
                ExerciseDuration = reader.IsDBNull(reader.GetOrdinal("ExerciseDuration"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("ExerciseDuration")),
                WaterIntake = reader.IsDBNull(reader.GetOrdinal("WaterIntake"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("WaterIntake")),
                Beverage = reader.IsDBNull(reader.GetOrdinal("Beverage"))
                    ? null : reader.GetString(reader.GetOrdinal("Beverage")),
                Cigarettes = reader.IsDBNull(reader.GetOrdinal("Cigarettes"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("Cigarettes")),
                BetelNut = reader.IsDBNull(reader.GetOrdinal("BetelNut"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BetelNut")),
                BloodSugar = reader.IsDBNull(reader.GetOrdinal("BloodSugar"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("BloodSugar"))
            };

            // 🍱 三餐資料 JSON 解析
            try
            {
                var breakfastJson = reader.IsDBNull(reader.GetOrdinal("Meals_Breakfast"))
                    ? null : reader.GetString(reader.GetOrdinal("Meals_Breakfast"));
                if (!string.IsNullOrEmpty(breakfastJson))
                    model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(breakfastJson);

                var lunchJson = reader.IsDBNull(reader.GetOrdinal("Meals_Lunch"))
                    ? null : reader.GetString(reader.GetOrdinal("Meals_Lunch"));
                if (!string.IsNullOrEmpty(lunchJson))
                    model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(lunchJson);

                var dinnerJson = reader.IsDBNull(reader.GetOrdinal("Meals_Dinner"))
                    ? null : reader.GetString(reader.GetOrdinal("Meals_Dinner"));
                if (!string.IsNullOrEmpty(dinnerJson))
                    model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(dinnerJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"解析三餐 JSON 失敗: {ex.Message}");
            }

            return model;
        }

        [HttpPost]
        public async Task<IActionResult> SendAnalysisPdfToLine([FromBody] ReportRequest request)
        {
            _logger.LogInformation(
            "收到 SendAnalysisPdfToLine 請求: UserAgent={UserAgent}, Start={StartDate}, End={EndDate}",
            Request.Headers.UserAgent.ToString(),
            request.StartDate,
            request.EndDate
            );
            try
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdClaim))
                    return Json(new { success = false, message = "無法取得使用者資訊,請重新登入" });

                var userId = int.Parse(userIdClaim);
                var user = await GetUserByIdAsync(userId);

                if (user == null)
                    return Json(new { success = false, message = "找不到使用者資料" });

                if (string.IsNullOrEmpty(user.LineUserId))
                    return Json(new { success = false, message = "您尚未綁定 LINE 帳號,無法傳送 PDF 報表" });

                var baseUrl = _configuration["AppSettings:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

                var capturedUser = user;
                var capturedStartDate = request.StartDate;
                var capturedEndDate = request.EndDate;
                var capturedReportType = request.ReportType;
                var capturedBaseUrl = baseUrl;
                var capturedLogger = _logger;

                _ = Task.Run(async () =>
                {
                    using var scope = scopeFactory.CreateScope();

                    try
                    {
                        var scheduledJobService = scope.ServiceProvider.GetRequiredService<ScheduledJobService>();

                        await scheduledJobService.SendWeeklyReportToUserAsync(
                            capturedUser,
                            capturedStartDate,
                            capturedEndDate,
                            capturedBaseUrl,
                            capturedReportType
                        );

                        capturedLogger.LogInformation(
                            $"PDF 報表已傳送到 LINE: {capturedUser.FullName} ({capturedStartDate:yyyy-MM-dd} ~ {capturedEndDate:yyyy-MM-dd})");
                    }
                    catch (Exception ex)
                    {
                        capturedLogger.LogError(ex, "背景傳送 PDF 報表到 LINE 失敗");
                    }
                });

                return Json(new
                {
                    success = true,
                    message = $"PDF 報表產生中,完成後會傳送到 LINE。\n\n期間: {request.StartDate:yyyy-MM-dd} ~ {request.EndDate:yyyy-MM-dd}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "啟動傳送 PDF 報表到 LINE 失敗");
                return Json(new { success = false, message = $"啟動失敗: {ex.Message}" });
            }
        }


        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> SendAdminAnalysisPdfToLine([FromBody] AdminReportRequest request)
        {
            try
            {
                var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(adminIdClaim))
                    return Json(new { success = false, message = "無法取得管理員資訊,請重新登入" });

                var adminId = int.Parse(adminIdClaim);
                var admin = await GetUserByIdAsync(adminId);

                if (admin == null)
                    return Json(new { success = false, message = "找不到管理員資料" });

                if (string.IsNullOrEmpty(admin.LineUserId))
                    return Json(new { success = false, message = "您的管理員帳號尚未綁定 LINE，無法接收 PDF 報表" });

                var patient = await GetPatientByIdNumberAsync(request.IDNumber);
                if (patient == null)
                    return Json(new { success = false, message = "查無此病患" });

                var baseUrl = _configuration["AppSettings:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

                var capturedPatient = patient;
                var capturedAdmin = admin;
                var capturedStartDate = request.StartDate;
                var capturedEndDate = request.EndDate;
                var capturedReportType = request.ReportType;
                var capturedBaseUrl = baseUrl;
                var capturedLogger = _logger;

                _ = Task.Run(async () =>
                {
                    using var scope = scopeFactory.CreateScope();

                    try
                    {
                        var scheduledJobService = scope.ServiceProvider.GetRequiredService<ScheduledJobService>();

                        var reportUser = new UserDBModel
                        {
                            Id = capturedPatient.Id,
                            FullName = capturedPatient.FullName,
                            IDNumber = capturedPatient.IDNumber,

                            // 重點：報表資料用病患，LINE 接收者用管理員
                            LineUserId = capturedAdmin.LineUserId
                        };

                        await scheduledJobService.SendWeeklyReportToUserAsync(
                            reportUser,
                            capturedStartDate,
                            capturedEndDate,
                            capturedBaseUrl,
                            capturedReportType
                        );

                        capturedLogger.LogInformation(
                            $"管理員已接收病患 PDF 報表: 管理員={capturedAdmin.FullName}, 病患={capturedPatient.FullName}, 期間={capturedStartDate:yyyy-MM-dd} ~ {capturedEndDate:yyyy-MM-dd}");
                    }
                    catch (Exception ex)
                    {
                        capturedLogger.LogError(ex, "管理員背景接收病患 PDF 報表失敗");
                    }
                });

                return Json(new
                {
                    success = true,
                    message = $"PDF 報表產生中，完成後會傳送到您的 LINE。\n\n病患: {patient.FullName}\n期間: {request.StartDate:yyyy-MM-dd} ~ {request.EndDate:yyyy-MM-dd}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "管理員啟動接收病患 PDF 報表失敗");
                return Json(new { success = false, message = $"啟動失敗: {ex.Message}" });
            }
        }


        [HttpPost]
        public async Task<IActionResult> StartGenerateAnalysisPdf([FromBody] ReportRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await GetUserByIdAsync(userId);
                if (user == null)
                    return Json(new { success = false, message = "找不到使用者資料" });

                var reportId = Guid.NewGuid().ToString("N");
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var insertQuery = @"
                    INSERT INTO ""WeeklyReports"" 
                    (""Id"", ""UserId"", ""StartDate"", ""EndDate"", ""PdfData"", ""ExpiresAt"", ""IsVerified"")
                    VALUES (@Id, @UserId, @StartDate, @EndDate, @PdfData, @ExpiresAt, false)";
                await using var cmd = new NpgsqlCommand(insertQuery, conn);
                cmd.Parameters.AddWithValue("@Id", reportId);
                cmd.Parameters.AddWithValue("@UserId", userId);
                cmd.Parameters.AddWithValue("@StartDate", request.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", request.EndDate);
                cmd.Parameters.AddWithValue("@PdfData", Array.Empty<byte>());
                cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.Now.AddDays(7));
                await cmd.ExecuteNonQueryAsync();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var analysis = await GenerateAnalysisAsync(
                            userId, user.FullName, user.IDNumber,
                            request.ReportType, request.StartDate, request.EndDate);
                        var pdfBytes = _reportService.GeneratePdfReport(analysis);

                        var connStr2 = _configuration.GetConnectionString("DefaultConnection");
                        await using var conn2 = new NpgsqlConnection(connStr2);
                        await conn2.OpenAsync();
                        var updateQuery = @"
                    UPDATE ""WeeklyReports"" 
                    SET ""PdfData"" = @PdfData, ""IsVerified"" = true
                    WHERE ""Id"" = @Id";
                        await using var cmd2 = new NpgsqlCommand(updateQuery, conn2);
                        cmd2.Parameters.AddWithValue("@PdfData", pdfBytes);
                        cmd2.Parameters.AddWithValue("@Id", reportId);
                        await cmd2.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "背景產生PDF失敗");
                    }
                });

                return Json(new { success = true, reportId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "啟動PDF產生失敗");
                return Json(new { success = false, message = "系統錯誤" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> CheckPdfReady(string reportId)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
                    SELECT ""IsVerified"", octet_length(""PdfData"") as size
                    FROM ""WeeklyReports"" WHERE ""Id"" = @Id";
                await using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Id", reportId);
                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return Json(new { ready = false });

                var isReady = reader.GetBoolean(0) && reader.GetInt64(1) > 0;
                var baseUrl = _configuration["AppSettings:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var downloadUrl = $"{baseUrl}/Analysis/DownloadWeeklyReport?reportId={reportId}";

                return Json(new { ready = isReady, url = downloadUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢PDF狀態失敗");
                return Json(new { ready = false });
            }
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