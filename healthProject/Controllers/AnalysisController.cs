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
        // 🔍 產生分析報表 (修正版)
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

            // ✅ 修正:建立合併的三餐物件
            var aggregatedRecords = dailyGroups.Select(d => new HealthRecordViewModel
            {
                RecordDate = d.Date,

                // 血壓:使用當日所有測量的平均
                BP_First_1_Systolic = d.AvgSystolicBP,
                BP_First_1_Diastolic = d.AvgDiastolicBP,

                // 血糖:使用當日平均
                BloodSugar = d.AvgBloodSugar,

                // 飲水量:使用當日總和
                WaterIntake = d.TotalWater > 0 ? d.TotalWater : null,

                // 運動時間:使用當日總和
                ExerciseDuration = d.TotalExercise > 0 ? d.TotalExercise : null,

                // 抽菸:使用當日總和
                Cigarettes = d.TotalCigarettes > 0 ? d.TotalCigarettes : null,

                // 檳榔:使用當日總和
                BetelNut = d.TotalBetelNut > 0 ? d.TotalBetelNut : null,

                // ✅ 三餐:建立合併的物件 (讓 MealsDisplay 自動計算)
                Meals_Breakfast = d.HasAnyMeals ? CreateDailyMealSummary(d, "Breakfast") : null,
                Meals_Lunch = d.HasAnyMeals ? CreateDailyMealSummary(d, "Lunch") : null,
                Meals_Dinner = d.HasAnyMeals ? CreateDailyMealSummary(d, "Dinner") : null,

                // 飲料:合併顯示
                Beverage = string.Join(", ", d.Records
                    .Where(r => !string.IsNullOrEmpty(r.Beverage))
                    .Select(r => r.Beverage)
                    .Distinct())
            }).ToList();

            var statistics = CalculateStatistics(aggregatedRecords);
            var charts = GenerateChartData(aggregatedRecords, reportType);

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

        // ========================================
        // 📈 計算統計數據 (使用每日聚合後的數據)
        // ========================================
        private AnalysisStatistics CalculateStatistics(List<HealthRecordViewModel> records)
        {
            if (!records.Any())
            {
                return new AnalysisStatistics { TotalDays = 0 };
            }

            // 取得有血壓數據的記錄
            var bpRecords = records.Where(r =>
                r.BP_First_1_Systolic.HasValue || r.BP_First_1_Diastolic.HasValue).ToList();

            return new AnalysisStatistics
            {
                TotalDays = records.Count, // 🆕 改為天數,不是筆數

                // 平均血壓 (各天平均的平均)
                AvgSystolicBP = bpRecords.Any(r => r.BP_First_1_Systolic.HasValue)
                    ? bpRecords.Where(r => r.BP_First_1_Systolic.HasValue)
                        .Average(r => r.BP_First_1_Systolic.Value)
                    : null,

                AvgDiastolicBP = bpRecords.Any(r => r.BP_First_1_Diastolic.HasValue)
                    ? bpRecords.Where(r => r.BP_First_1_Diastolic.HasValue)
                        .Average(r => r.BP_First_1_Diastolic.Value)
                    : null,

                // 平均血糖
                AvgBloodSugar = records.Where(r => r.BloodSugar.HasValue).Any()
                    ? records.Where(r => r.BloodSugar.HasValue).Average(r => r.BloodSugar.Value)
                    : null,

                // 平均飲水量 (各天總和的平均)
                AvgWaterIntake = records.Where(r => r.WaterIntake.HasValue).Any()
                    ? records.Where(r => r.WaterIntake.HasValue).Average(r => r.WaterIntake.Value)
                    : null,

                // 平均運動時間 (各天總和的平均)
                AvgExerciseDuration = records.Where(r => r.ExerciseDuration.HasValue).Any()
                    ? records.Where(r => r.ExerciseDuration.HasValue).Average(r => r.ExerciseDuration.Value)
                    : null,

                // 平均抽菸
                AvgCigarettes = records.Where(r => r.Cigarettes.HasValue).Any()
                    ? records.Where(r => r.Cigarettes.HasValue).Average(r => r.Cigarettes.Value)
                    : null,

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
        }

        // ========================================
        // 🆕 建立每日三餐統計摘要
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
        // 🆕 修改：合併三餐項目，計算總和而不是顯示算式
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

                // 🆕 三餐記錄 - 改為統計格式
                if (record.Meals_Breakfast != null || record.Meals_Lunch != null || record.Meals_Dinner != null)
                {
                    var vegetables = new List<string>();
                    var protein = new List<string>();
                    var carbs = new List<string>();

                    // 收集早午晚餐的各營養素
                    foreach (var meal in new[] { record.Meals_Breakfast, record.Meals_Lunch, record.Meals_Dinner })
                    {
                        if (meal != null)
                        {
                            if (!string.IsNullOrEmpty(meal.Vegetables) && meal.Vegetables != "0")
                                vegetables.Add(meal.Vegetables);
                            if (!string.IsNullOrEmpty(meal.Protein) && meal.Protein != "0")
                                protein.Add(meal.Protein);
                            if (!string.IsNullOrEmpty(meal.Carbs) && meal.Carbs != "0")
                                carbs.Add(meal.Carbs);
                        }
                    }

                    // ✅ 計算總和
                    var vegTotal = CalculateMealTotal(vegetables);
                    var proteinTotal = CalculateMealTotal(protein);
                    var carbsTotal = CalculateMealTotal(carbs);

                    charts.MealRecords.Add(new MealRecord
                    {
                        Date = record.RecordDate.ToString("MM/dd"),
                        MealData = new MealStatistics
                        {
                            Vegetables = string.IsNullOrEmpty(vegTotal) ? new List<string>() : new List<string> { vegTotal },
                            Protein = string.IsNullOrEmpty(proteinTotal) ? new List<string>() : new List<string> { proteinTotal },
                            Carbs = string.IsNullOrEmpty(carbsTotal) ? new List<string>() : new List<string> { carbsTotal }
                        }
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
        // 🆕 新增輔助方法：計算三餐項目總和
        // 加在 AnalysisController 類別中
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

                // 🩸 血壓資料 - 8個欄位
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