using healthProject.Models;
using healthProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            var rawRecords = await GetRecordsInRangeAsync(userId, startDate, endDate);

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
                WaterIntake = d.TotalWater > 0 ? d.TotalWater : null,
                ExerciseDuration = d.TotalExercise > 0 ? d.TotalExercise : null,
                Cigarettes = d.TotalCigarettes > 0 ? d.TotalCigarettes : null,
                BetelNut = d.TotalBetelNut > 0 ? d.TotalBetelNut : null,
                Meals_Breakfast = d.HasAnyMeals ? CreateDailyMealSummary(d, "Breakfast") : null,
                Meals_Lunch = d.HasAnyMeals ? CreateDailyMealSummary(d, "Lunch") : null,
                Meals_Dinner = d.HasAnyMeals ? CreateDailyMealSummary(d, "Dinner") : null,
                Beverage = string.Join(", ", d.Records
                    .Where(r => !string.IsNullOrEmpty(r.Beverage))
                    .Select(r => r.Beverage)
                    .Distinct())
            }).ToList();

            var statistics = CalculateStatistics(aggregatedRecords);

            // ✅ 年報表特殊處理：圖表用月平均,但三餐飲料用原始每日數據
            ChartData charts;
            if (reportType == ReportType.Yearly)
            {
                var monthlyRecords = AggregateToMonthly(aggregatedRecords);
                charts = GenerateChartData(monthlyRecords, reportType);

                // ✅ 但三餐和飲料用原始每日數據
                charts.MealRecords = GenerateDailyMealRecords(aggregatedRecords);
                charts.BeverageRecords = GenerateDailyBeverageRecords(aggregatedRecords);

                // ✅ 重新計算三餐統計
                charts.YearlyMealSummary = CalculateMealStatistics(aggregatedRecords);
            }
            else
            {
                charts = GenerateChartData(aggregatedRecords, reportType);
            }

            return new AnalysisViewModel
            {
                PatientName = fullName,
                IDNumber = idNumber,
                ReportType = reportType,
                StartDate = startDate,
                EndDate = endDate,
                Statistics = statistics,
                Records = aggregatedRecords,
                Charts = charts
            };
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
                await scheduledJobService.SendWeeklyReportToUserAsync(user, lastMonday, lastSunday);

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
        private AnalysisStatistics CalculateStatistics(List<HealthRecordViewModel> records)
        {
            if (!records.Any())
            {
                return new AnalysisStatistics { TotalDays = 0 };
            }

            var bpRecords = records.Where(r =>
                r.BP_First_1_Systolic.HasValue || r.BP_First_1_Diastolic.HasValue).ToList();

            // 🆕 計算三餐總計與平均
            var mealStats = CalculateMealStatistics(records);

            // 🆕 計算抽菸檳榔總計
            var totalCigs = records.Where(r => r.Cigarettes.HasValue).Sum(r => r.Cigarettes.Value);
            var totalBetel = records.Where(r => r.BetelNut.HasValue).Sum(r => r.BetelNut.Value);

            var stats = new AnalysisStatistics
            {
                TotalDays = records.Count,

                // 血壓
                AvgSystolicBP = bpRecords.Any(r => r.BP_First_1_Systolic.HasValue)
                    ? bpRecords.Where(r => r.BP_First_1_Systolic.HasValue)
                        .Average(r => r.BP_First_1_Systolic.Value)
                    : null,

                AvgDiastolicBP = bpRecords.Any(r => r.BP_First_1_Diastolic.HasValue)
                    ? bpRecords.Where(r => r.BP_First_1_Diastolic.HasValue)
                        .Average(r => r.BP_First_1_Diastolic.Value)
                    : null,

                // 血糖
                AvgBloodSugar = records.Where(r => r.BloodSugar.HasValue).Any()
                    ? records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar.Value)
                    : null,

                // 飲水
                AvgWaterIntake = records.Where(r => r.WaterIntake.HasValue).Any()
                    ? records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake.Value)
                    : null,

                // 運動
                AvgExerciseDuration = records.Where(r => r.ExerciseDuration.HasValue).Any()
                    ? records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration.Value)
                    : null,

                //  抽菸
                TotalCigarettes = totalCigs,
                AvgCigarettes = records.Count > 0 ? totalCigs / records.Count : 0,
                SmokingDays = records.Count(r => r.Cigarettes.HasValue && r.Cigarettes.Value > 0),

                //  檳榔
                TotalBetelNut = totalBetel,
                AvgBetelNut = records.Count > 0 ? totalBetel / records.Count : 0,
                BetelNutDays = records.Count(r => r.BetelNut.HasValue && r.BetelNut.Value > 0),

                //  三餐平均
                AvgVegetables = mealStats.AvgVegetables,
                AvgProtein = mealStats.AvgProtein,
                AvgCarbs = mealStats.AvgCarbs,

                // 異常天數
                HighBPDays = records.Count(r =>
                    (r.BP_First_1_Systolic.HasValue && r.BP_First_1_Systolic.Value > 120) ||
                    (r.BP_First_1_Diastolic.HasValue && r.BP_First_1_Diastolic.Value > 80)),

                HighBloodSugarDays = records.Count(r =>
                    r.BloodSugar.HasValue && r.BloodSugar.Value > 99),

                LowWaterDays = records.Count(r =>
                    r.WaterIntake.HasValue && r.WaterIntake.Value < 2000),

                LowExerciseDays = records.Count(r =>
                    r.ExerciseDuration.HasValue && r.ExerciseDuration.Value < 150)
            };

            return stats;
        }

        //  計算三餐統計
        private MealSummary CalculateMealStatistics(List<HealthRecordViewModel> records)
        {
            var totalVeg = 0m;
            var totalProtein = 0m;
            var totalCarbs = 0m;
            var mealCount = 0;

            foreach (var record in records)
            {
                var meals = new[] { record.Meals_Breakfast, record.Meals_Lunch, record.Meals_Dinner };

                foreach (var meal in meals)
                {
                    if (meal == null) continue;
                    mealCount++;

                    if (!string.IsNullOrEmpty(meal.Vegetables) && decimal.TryParse(meal.Vegetables, out var veg))
                        totalVeg += veg;
                    if (!string.IsNullOrEmpty(meal.Protein) && decimal.TryParse(meal.Protein, out var protein))
                        totalProtein += protein;
                    if (!string.IsNullOrEmpty(meal.Carbs) && decimal.TryParse(meal.Carbs, out var carbs))
                        totalCarbs += carbs;
                }
            }

            return new MealSummary
            {
                TotalVegetables = totalVeg,
                TotalProtein = totalProtein,
                TotalCarbs = totalCarbs,
                AvgVegetables = records.Count > 0 ? totalVeg / records.Count : 0,
                AvgProtein = records.Count > 0 ? totalProtein / records.Count : 0,
                AvgCarbs = records.Count > 0 ? totalCarbs / records.Count : 0,
                DaysWithMeals = records.Count(r =>
                    r.Meals_Breakfast != null || r.Meals_Lunch != null || r.Meals_Dinner != null)
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
        private ChartData GenerateChartData(List<HealthRecordViewModel> records, ReportType reportType)
        {
            var charts = new ChartData();

            foreach (var record in records.OrderBy(r => r.RecordDate))
            {
                var dateStr = FormatDateForChart(record.RecordDate, reportType);

                // 血壓
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

                // 血糖
                if (record.BloodSugar.HasValue)
                {
                    charts.BloodSugarData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BloodSugar,
                        IsAbnormal = record.BloodSugar.Value > 99
                    });
                }

                // 飲水量
                if (record.WaterIntake.HasValue)
                {
                    charts.WaterIntakeData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.WaterIntake,
                        IsAbnormal = record.WaterIntake.Value < 2000
                    });
                }

                // 運動時間
                if (record.ExerciseDuration.HasValue)
                {
                    charts.ExerciseDurationData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.ExerciseDuration,
                        IsAbnormal = record.ExerciseDuration.Value < 150
                    });
                }

                // 抽菸
                if (record.Cigarettes.HasValue && record.Cigarettes.Value > 0)
                {
                    charts.CigarettesData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.Cigarettes,
                        IsAbnormal = record.Cigarettes.Value > 10
                    });
                }

                // 檳榔
                if (record.BetelNut.HasValue && record.BetelNut.Value > 0)
                {
                    charts.BetelNutData.Add(new ChartPoint
                    {
                        Date = dateStr,
                        Value = record.BetelNut,
                        IsAbnormal = record.BetelNut.Value > 10
                    });
                }

                // ✅ 三餐記錄 (非年報表才處理,年報表在外面特別處理)
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

                // ✅ 飲料記錄 (非年報表才處理)
                if (reportType != ReportType.Yearly && !string.IsNullOrEmpty(record.Beverage))
                {
                    charts.BeverageRecords.Add(new BeverageRecord
                    {
                        Date = record.RecordDate.ToString("MM/dd"),
                        Beverage = record.Beverage
                    });
                }
            }

            // 計算三餐統計
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