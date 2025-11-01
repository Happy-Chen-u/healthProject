using healthProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace healthProject.Controllers
{
    [Authorize]
    public class DailyHealthController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DailyHealthController> _logger;

        public DailyHealthController(IConfiguration configuration, ILogger<DailyHealthController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // ========================================
        // 🏠 首頁 - 病患或管理員
        // ========================================
        public IActionResult Index()
        {
            if (User.IsInRole("Admin"))
            {
                return View("AdminSearch");
            }
            return View("PatientMenu");
        }

        // ========================================
        // ➕ 新增今日紀錄 - GET (顯示表單)
        // ========================================
        [HttpGet]
        public IActionResult Create(
            int? Id,
            decimal? BP_First_1_Systolic, decimal? BP_First_1_Diastolic,
            decimal? BP_First_2_Systolic, decimal? BP_First_2_Diastolic,
            decimal? BP_Second_1_Systolic, decimal? BP_Second_1_Diastolic,
            decimal? BP_Second_2_Systolic, decimal? BP_Second_2_Diastolic,
            string? Meals_Breakfast, string? Meals_Lunch, string? Meals_Dinner,
            string? ExerciseType, decimal? ExerciseDuration,
            decimal? WaterIntake, string? Beverage,
            decimal? Cigarettes, decimal? BetelNut, decimal? BloodSugar)
        {
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index");
            }

            var model = new HealthRecordViewModel
            {
                RecordDate = DateTime.Today,
                RecordTime = DateTime.Now.TimeOfDay
            };

            // 🆕 如果有帶參數(從 Confirm 返回),填入資料
            if (BP_First_1_Systolic.HasValue || BloodSugar.HasValue || WaterIntake.HasValue)
            {
                model.Id = Id ?? 0;
                model.BP_First_1_Systolic = BP_First_1_Systolic;
                model.BP_First_1_Diastolic = BP_First_1_Diastolic;
                model.BP_First_2_Systolic = BP_First_2_Systolic;
                model.BP_First_2_Diastolic = BP_First_2_Diastolic;
                model.BP_Second_1_Systolic = BP_Second_1_Systolic;
                model.BP_Second_1_Diastolic = BP_Second_1_Diastolic;
                model.BP_Second_2_Systolic = BP_Second_2_Systolic;
                model.BP_Second_2_Diastolic = BP_Second_2_Diastolic;

                // 🆕 三餐 JSON 反序列化
                if (!string.IsNullOrEmpty(Meals_Breakfast))
                    model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(Meals_Breakfast);
                if (!string.IsNullOrEmpty(Meals_Lunch))
                    model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(Meals_Lunch);
                if (!string.IsNullOrEmpty(Meals_Dinner))
                    model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(Meals_Dinner);

                model.ExerciseType = ExerciseType;
                model.ExerciseDuration = ExerciseDuration;
                model.WaterIntake = WaterIntake;
                model.Beverage = Beverage;
                model.Cigarettes = Cigarettes;
                model.BetelNut = BetelNut;
                model.BloodSugar = BloodSugar;
            }

            return View(model);
        }

        // ========================================
        // ➕ 新增今日紀錄 - POST (提交表單)
        // ========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HealthRecordViewModel model,
            string Meals_Breakfast, string Meals_Lunch, string Meals_Dinner)
        {
            // ✅ 驗證血壓完整性
            var bpWarnings = model.ValidateBloodPressure();
            if (bpWarnings.Any())
            {
                TempData["BPWarnings"] = string.Join("\n", bpWarnings);
            }

            // 🆕 手動處理三餐 JSON
            if (!string.IsNullOrEmpty(Meals_Breakfast))
                model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(Meals_Breakfast);
            if (!string.IsNullOrEmpty(Meals_Lunch))
                model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(Meals_Lunch);
            if (!string.IsNullOrEmpty(Meals_Dinner))
                model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(Meals_Dinner);

            // 移除不需要驗證的欄位
            ModelState.Remove("Meals_Breakfast");
            ModelState.Remove("Meals_Lunch");
            ModelState.Remove("Meals_Dinner");
            ModelState.Remove("BP_First_1_Input");
            ModelState.Remove("BP_First_2_Input");
            ModelState.Remove("BP_Second_1_Input");
            ModelState.Remove("BP_Second_2_Input");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            model.UserId = userId;
            model.RecordDate = DateTime.Today;
            model.RecordTime = DateTime.Now.TimeOfDay;

            // 顯示確認頁面
            return View("Confirm", model);
        }


        // ========================================
        // ✅ 確認上傳
        // ========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmSubmit(HealthRecordViewModel model,
            string Meals_Breakfast, string Meals_Lunch, string Meals_Dinner)
        {
            try
            {
                // 🆕 處理三餐 JSON 反序列化
                if (!string.IsNullOrEmpty(Meals_Breakfast))
                    model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(Meals_Breakfast);
                if (!string.IsNullOrEmpty(Meals_Lunch))
                    model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(Meals_Lunch);
                if (!string.IsNullOrEmpty(Meals_Dinner))
                    model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(Meals_Dinner);

                ModelState.Clear();

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                model.UserId = userId;
                model.RecordDate = DateTime.Today;
                model.RecordTime = DateTime.Now.TimeOfDay;

                await SaveRecordAsync(model);

                // 🆕 取得今日所有紀錄來計算總計
                var todayRecords = await GetUserRecordsByDateAsync(userId, DateTime.Today);

                // 產生建議訊息（使用今日總計）
                var feedback = GenerateFeedbackWithDailyTotal(todayRecords);
                TempData["Feedback"] = JsonSerializer.Serialize(feedback);

                // 🔔 發送 LINE 通知
                await SendLineNotification(userId, feedback);

                return RedirectToAction("Success");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存今日健康紀錄失敗");
                ModelState.AddModelError("", "儲存失敗，請稍後再試");
                return View("Confirm", model);
            }
        }

            // ========================================
            // 🎉 上傳成功頁面
            // ========================================
        public IActionResult Success()
        {
            if (TempData["Feedback"] != null)
            {
                var feedback = JsonSerializer.Deserialize<FeedbackViewModel>(TempData["Feedback"].ToString());
                return View(feedback);
            }
            return RedirectToAction("Index");
        }

        // ========================================
        // 📚 查看歷史紀錄 - 病患專用 (分組顯示)
        // ========================================
        public async Task<IActionResult> MyRecords()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var records = await GetUserRecordsAsync(userId);

            // 🆕 按日期分組
            var groupedRecords = records
                .GroupBy(r => r.RecordDate.Date)
                .Select(g => new DailyRecordGroup
                {
                    Date = g.Key,
                    Records = g.OrderBy(r => r.RecordTime).ToList()
                })
                .OrderByDescending(g => g.Date)
                .ToList();

            return View(groupedRecords);
        }

        // ========================================
        // 📝 編輯紀錄
        // ========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var record = await GetRecordByIdAsync(id);

            if (record == null || record.UserId != userId)
            {
                return NotFound();
            }

            return View("Create", record);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(HealthRecordViewModel model,
            string Meals_Breakfast, string Meals_Lunch, string Meals_Dinner)  // ⚠️ 加入三餐參數
        {
            // 🆕 處理三餐 JSON 反序列化
            if (!string.IsNullOrEmpty(Meals_Breakfast))
                model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(Meals_Breakfast);
            if (!string.IsNullOrEmpty(Meals_Lunch))
                model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(Meals_Lunch);
            if (!string.IsNullOrEmpty(Meals_Dinner))
                model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(Meals_Dinner);

            // ⚠️ 清除 ModelState（因為我們手動處理了三餐）
            ModelState.Clear();

            // 重新驗證（如果需要）
            if (!TryValidateModel(model))
            {
                return View("Create", model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (model.UserId != userId)
            {
                return Forbid();
            }

            // 顯示確認頁面
            return View("Confirm", model);
        }

        // ========================================
        // ✅ 確認更新
        // ========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmUpdate(HealthRecordViewModel model,
            string Meals_Breakfast, string Meals_Lunch, string Meals_Dinner)
        {
            try
            {
                if (!string.IsNullOrEmpty(Meals_Breakfast))
                    model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(Meals_Breakfast);
                if (!string.IsNullOrEmpty(Meals_Lunch))
                    model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(Meals_Lunch);
                if (!string.IsNullOrEmpty(Meals_Dinner))
                    model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(Meals_Dinner);

                ModelState.Clear();

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                if (model.UserId != userId)
                {
                    return Forbid();
                }

                await UpdateRecordAsync(model);

                // 🆕 取得今日所有紀錄來計算總計
                var todayRecords = await GetUserRecordsByDateAsync(userId, model.RecordDate);

                // 產生建議訊息（使用今日總計）
                var feedback = GenerateFeedbackWithDailyTotal(todayRecords);
                TempData["Feedback"] = JsonSerializer.Serialize(feedback);

                await SendLineNotification(userId, feedback);

                return RedirectToAction("Success");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新紀錄失敗");
                ModelState.AddModelError("", "更新失敗，請稍後再試");
                return View("Confirm", model);
            }
        }

        // ========================================
        // 🔍 管理員搜尋病患紀錄
        // ========================================

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> SearchPatientRecords([FromBody] SearchRequest request)
        {
            try
            {
                var patient = await GetPatientByIdNumberAsync(request.idNumber);
                if (patient == null)
                {
                    return Json(new { success = false, message = "查無此病患" });
                }

                var records = await GetUserRecordsAsync(patient.Id);

                // 確保三餐欄位是物件 (不是已序列化的字串)
                object ToMealObject(MealSelection m) =>
                    m == null ? null : new
                    {
                        Vegetables = string.IsNullOrWhiteSpace(m.Vegetables) ? null : m.Vegetables,
                        Protein = string.IsNullOrWhiteSpace(m.Protein) ? null : m.Protein,
                        Carbs = string.IsNullOrWhiteSpace(m.Carbs) ? null : m.Carbs
                    };

                var groupedRecords = records
                    .GroupBy(r => r.RecordDate.Date)
                    .Select(g => new
                    {
                        date = g.Key.ToString("yyyy-MM-dd"),
                        records = g.OrderBy(r => r.RecordTime).Select(r => new
                        {
                            recordTime = r.RecordTime?.ToString(@"hh\:mm"),

                            // 血壓 (維持原樣)
                            bp_First_1_Systolic = r.BP_First_1_Systolic,
                            bp_First_1_Diastolic = r.BP_First_1_Diastolic,
                            bp_First_2_Systolic = r.BP_First_2_Systolic,
                            bp_First_2_Diastolic = r.BP_First_2_Diastolic,
                            bp_Second_1_Systolic = r.BP_Second_1_Systolic,
                            bp_Second_1_Diastolic = r.BP_Second_1_Diastolic,
                            bp_Second_2_Systolic = r.BP_Second_2_Systolic,
                            bp_Second_2_Diastolic = r.BP_Second_2_Diastolic,

                            // 三餐 - 明確轉成物件 (避免字串包 JSON 的情況)
                            meals_Breakfast = ToMealObject(r.Meals_Breakfast),
                            meals_Lunch = ToMealObject(r.Meals_Lunch),
                            meals_Dinner = ToMealObject(r.Meals_Dinner),

                            // 其他欄位
                            exerciseType = r.ExerciseType,
                            exerciseDuration = r.ExerciseDuration,
                            waterIntake = r.WaterIntake,
                            beverage = r.Beverage,
                            cigarettes = r.Cigarettes,
                            betelNut = r.BetelNut,
                            bloodSugar = r.BloodSugar
                        }).ToList()
                    })
                    .OrderByDescending(g => g.date)
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        patientName = patient.FullName,
                        idNumber = patient.IDNumber,
                        records = groupedRecords
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜尋失敗");
                return Json(new { success = false, message = "系統錯誤" });
            }
        }



        // ========================================
        // 🧠 資料庫操作 - 新增
        // ========================================
        private async Task SaveRecordAsync(HealthRecordViewModel model)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
        INSERT INTO ""Today"" 
        (""UserId"", ""RecordDate"", ""RecordTime"",
         ""BP_First_1_Systolic"", ""BP_First_1_Diastolic"",
         ""BP_First_2_Systolic"", ""BP_First_2_Diastolic"",
         ""BP_Second_1_Systolic"", ""BP_Second_1_Diastolic"",
         ""BP_Second_2_Systolic"", ""BP_Second_2_Diastolic"",
         ""Meals_Breakfast"", ""Meals_Lunch"", ""Meals_Dinner"",
         ""ExerciseType"", ""ExerciseDuration"", 
         ""WaterIntake"", ""Beverage"", ""Cigarettes"", 
         ""BetelNut"", ""BloodSugar"")
        VALUES 
        (@UserId, @RecordDate, @RecordTime,
         @BP_First_1_Systolic, @BP_First_1_Diastolic,
         @BP_First_2_Systolic, @BP_First_2_Diastolic,
         @BP_Second_1_Systolic, @BP_Second_1_Diastolic,
         @BP_Second_2_Systolic, @BP_Second_2_Diastolic,
         @Meals_Breakfast::jsonb, @Meals_Lunch::jsonb, @Meals_Dinner::jsonb,
         @ExerciseType, @ExerciseDuration,
         @WaterIntake, @Beverage, @Cigarettes,
         @BetelNut, @BloodSugar)";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", model.UserId);
            cmd.Parameters.AddWithValue("@RecordDate", model.RecordDate);
            cmd.Parameters.AddWithValue("@RecordTime", model.RecordTime ?? (object)DBNull.Value);

            // 血壓 - 8個欄位
            cmd.Parameters.AddWithValue("@BP_First_1_Systolic", model.BP_First_1_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_1_Diastolic", model.BP_First_1_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_2_Systolic", model.BP_First_2_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_2_Diastolic", model.BP_First_2_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_1_Systolic", model.BP_Second_1_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_1_Diastolic", model.BP_Second_1_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_2_Systolic", model.BP_Second_2_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_2_Diastolic", model.BP_Second_2_Diastolic ?? (object)DBNull.Value);

            // ⚠️ 三餐 JSON - 加入 ::jsonb 轉換
            cmd.Parameters.AddWithValue("@Meals_Breakfast",
                model.Meals_Breakfast != null ? JsonSerializer.Serialize(model.Meals_Breakfast) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals_Lunch",
                model.Meals_Lunch != null ? JsonSerializer.Serialize(model.Meals_Lunch) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals_Dinner",
                model.Meals_Dinner != null ? JsonSerializer.Serialize(model.Meals_Dinner) : (object)DBNull.Value);

            // 其他
            cmd.Parameters.AddWithValue("@ExerciseType", model.ExerciseType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ExerciseDuration", model.ExerciseDuration ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@WaterIntake", model.WaterIntake ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Beverage", model.Beverage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Cigarettes", model.Cigarettes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BetelNut", model.BetelNut ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BloodSugar", model.BloodSugar ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        // ========================================
        // 🧠 資料庫操作 - 更新
        // ========================================
        private async Task UpdateRecordAsync(HealthRecordViewModel model)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
        UPDATE ""Today""
        SET ""BP_First_1_Systolic"" = @BP_First_1_Systolic,
            ""BP_First_1_Diastolic"" = @BP_First_1_Diastolic,
            ""BP_First_2_Systolic"" = @BP_First_2_Systolic,
            ""BP_First_2_Diastolic"" = @BP_First_2_Diastolic,
            ""BP_Second_1_Systolic"" = @BP_Second_1_Systolic,
            ""BP_Second_1_Diastolic"" = @BP_Second_1_Diastolic,
            ""BP_Second_2_Systolic"" = @BP_Second_2_Systolic,
            ""BP_Second_2_Diastolic"" = @BP_Second_2_Diastolic,
            ""Meals_Breakfast"" = @Meals_Breakfast::jsonb,
            ""Meals_Lunch"" = @Meals_Lunch::jsonb,
            ""Meals_Dinner"" = @Meals_Dinner::jsonb,
            ""ExerciseType"" = @ExerciseType,
            ""ExerciseDuration"" = @ExerciseDuration,
            ""WaterIntake"" = @WaterIntake,
            ""Beverage"" = @Beverage,
            ""Cigarettes"" = @Cigarettes,
            ""BetelNut"" = @BetelNut,
            ""BloodSugar"" = @BloodSugar
        WHERE ""Id"" = @Id AND ""UserId"" = @UserId";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@UserId", model.UserId);

            // 血壓 - 8個欄位
            cmd.Parameters.AddWithValue("@BP_First_1_Systolic", model.BP_First_1_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_1_Diastolic", model.BP_First_1_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_2_Systolic", model.BP_First_2_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_2_Diastolic", model.BP_First_2_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_1_Systolic", model.BP_Second_1_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_1_Diastolic", model.BP_Second_1_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_2_Systolic", model.BP_Second_2_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_2_Diastolic", model.BP_Second_2_Diastolic ?? (object)DBNull.Value);

            // ⚠️ 三餐 JSON - 加入 ::jsonb 轉換
            cmd.Parameters.AddWithValue("@Meals_Breakfast",
                model.Meals_Breakfast != null ? JsonSerializer.Serialize(model.Meals_Breakfast) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals_Lunch",
                model.Meals_Lunch != null ? JsonSerializer.Serialize(model.Meals_Lunch) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals_Dinner",
                model.Meals_Dinner != null ? JsonSerializer.Serialize(model.Meals_Dinner) : (object)DBNull.Value);

            // 其他
            cmd.Parameters.AddWithValue("@ExerciseType", model.ExerciseType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ExerciseDuration", model.ExerciseDuration ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@WaterIntake", model.WaterIntake ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Beverage", model.Beverage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Cigarettes", model.Cigarettes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BetelNut", model.BetelNut ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BloodSugar", model.BloodSugar ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        // ========================================
        // 🧠 資料庫操作 - 查詢
        // ========================================
        private async Task<List<HealthRecordViewModel>> GetUserRecordsAsync(int userId)
        {
            var records = new List<HealthRecordViewModel>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT * FROM ""Today""
                WHERE ""UserId"" = @UserId
                ORDER BY ""RecordDate"" DESC, ""RecordTime"" DESC";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dbModel = MapFromReader(reader);
                records.Add(dbModel.ToViewModel());
            }

            return records;
        }

        private async Task<HealthRecordViewModel> GetRecordByIdAsync(int id)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT * FROM ""Today""
                WHERE ""Id"" = @Id LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var dbModel = MapFromReader(reader);
                return dbModel.ToViewModel();
            }

            return null;
        }

        private async Task<UserDBModel> GetPatientByIdNumberAsync(string idNumber)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT ""Id"", ""FullName"", ""IDNumber""
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
                    IDNumber = reader.GetString(2)
                };
            }

            return null;
        }

        // ========================================
        // 🧠 資料庫讀取 - MapFromReader (🆕 修正版)
        // ========================================
        private HealthRecordDBModel MapFromReader(NpgsqlDataReader reader)
        {
            return new HealthRecordDBModel
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                RecordDate = reader.GetDateTime(reader.GetOrdinal("RecordDate")),
                RecordTime = reader.IsDBNull(reader.GetOrdinal("RecordTime"))
                    ? null : reader.GetTimeSpan(reader.GetOrdinal("RecordTime")),

                // 🆕 血壓 - 8個欄位
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

                // 三餐
                Meals_Breakfast = reader.IsDBNull(reader.GetOrdinal("Meals_Breakfast"))
                    ? null : reader.GetString(reader.GetOrdinal("Meals_Breakfast")),
                Meals_Lunch = reader.IsDBNull(reader.GetOrdinal("Meals_Lunch"))
                    ? null : reader.GetString(reader.GetOrdinal("Meals_Lunch")),
                Meals_Dinner = reader.IsDBNull(reader.GetOrdinal("Meals_Dinner"))
                    ? null : reader.GetString(reader.GetOrdinal("Meals_Dinner")),

                // 其他
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
        }

            // ========================================
            // 💬 產生回饋訊息
            // ========================================
            private FeedbackViewModel GenerateFeedback(HealthRecordViewModel model)
        {
            var feedback = new FeedbackViewModel();

            // 水分攝取
            if (model.WaterIntake.HasValue)
            {
                if (model.WaterIntake.Value < HealthRecordViewModel.WATER_STANDARD)
                {
                    var diff = HealthRecordViewModel.WATER_STANDARD - (int)model.WaterIntake.Value;
                    feedback.WaterMessage = $"💧 今日水分攝取量較少，還差 {diff} ml 達到建議量！";
                    feedback.WaterStatus = "warning";
                }
                else
                {
                    feedback.WaterMessage = "💧 太棒了！今日水分攝取充足，繼續保持！";
                    feedback.WaterStatus = "success";
                }
            }

            // 運動時間
            if (model.ExerciseDuration.HasValue)
            {
                if (model.ExerciseDuration.Value < HealthRecordViewModel.EXERCISE_STANDARD)
                {
                    var diff = HealthRecordViewModel.EXERCISE_STANDARD - (int)model.ExerciseDuration.Value;
                    feedback.ExerciseMessage = $"🏃 今日運動時間可再增加 {diff} 分鐘達到建議量！";
                    feedback.ExerciseStatus = "warning";
                }
                else
                {
                    feedback.ExerciseMessage = "🏃 很棒！今日運動時間充足，身體會更健康！";
                    feedback.ExerciseStatus = "success";
                }
            }

            // 抽菸
            if (model.Cigarettes.HasValue && model.Cigarettes.Value > 0)
            {
                if (model.Cigarettes.Value < 3)
                {
                    feedback.CigaretteMessage = "🚭 太好了！抽菸量很少，繼續努力戒菸！";
                    feedback.CigaretteStatus = "success";
                }
                else if (model.Cigarettes.Value <= 7)
                {
                    feedback.CigaretteMessage = "🚭 加油！抽得越少身體越健康！";
                    feedback.CigaretteStatus = "info";
                }
                else
                {
                    feedback.CigaretteMessage = "⚠️ 今日抽菸量較多，建議尋求戒菸協助，保護您的健康！";
                    feedback.CigaretteStatus = "danger";
                }
            }

            // 血壓 (使用平均值)
            if (model.AvgSystolicBP.HasValue && model.AvgSystolicBP.Value > 120)
            {
                feedback.BloodPressureMessage = "⚠️ 收縮壓偏高（>120 mmHg），建議注意飲食與作息，並諮詢醫師！";
                feedback.BloodPressureStatus = "danger";
            }
            else if (model.AvgDiastolicBP.HasValue && model.AvgDiastolicBP.Value > 80)
            {
                feedback.BloodPressureMessage = "⚠️ 舒張壓偏高（>80 mmHg），建議注意飲食與作息，並諮詢醫師！";
                feedback.BloodPressureStatus = "danger";
            }
            else if (model.AvgSystolicBP.HasValue || model.AvgDiastolicBP.HasValue)
            {
                feedback.BloodPressureMessage = "✅ 血壓數值正常，繼續保持良好的生活習慣！";
                feedback.BloodPressureStatus = "success";
            }

            // 血糖
            if (model.BloodSugar.HasValue && model.BloodSugar.Value > 99)
            {
                feedback.BloodSugarMessage = "⚠️ 血糖偏高（>99 mg/dL），建議控制飲食並諮詢醫師！";
                feedback.BloodSugarStatus = "danger";
            }
            else if (model.BloodSugar.HasValue)
            {
                feedback.BloodSugarMessage = "✅ 血糖數值正常，繼續維持健康飲食！";
                feedback.BloodSugarStatus = "success";
            }

            return feedback;
        }

        // ========================================
        // 📱 發送 LINE 通知
        // ========================================
        private async Task SendLineNotification(int userId, FeedbackViewModel feedback)
        {
            try
            {
                var lineUserId = await GetUserLineIdAsync(userId);
                if (string.IsNullOrEmpty(lineUserId))
                {
                    _logger.LogWarning($"使用者 {userId} 尚未綁定 LINE");
                    return;
                }

                var channelAccessToken = _configuration["Line:ChannelAccessToken"];
                if (string.IsNullOrEmpty(channelAccessToken))
                {
                    _logger.LogError("LINE Channel Access Token 未設定");
                    return;
                }

                var messages = new List<string>();
                messages.Add("📊 【代謝症候群管理系統】");
                messages.Add("今日健康資訊已記錄成功!");
                messages.Add("═══════════════");

                bool hasWarning = false;

                if (!string.IsNullOrEmpty(feedback.BloodPressureMessage) &&
                    feedback.BloodPressureStatus == "danger")
                {
                    messages.Add(feedback.BloodPressureMessage);
                    hasWarning = true;
                }

                if (!string.IsNullOrEmpty(feedback.BloodSugarMessage) &&
                    feedback.BloodSugarStatus == "danger")
                {
                    messages.Add(feedback.BloodSugarMessage);
                    hasWarning = true;
                }

                if (!string.IsNullOrEmpty(feedback.WaterMessage) &&
                    feedback.WaterStatus == "warning")
                {
                    messages.Add(feedback.WaterMessage);
                }

                if (!string.IsNullOrEmpty(feedback.ExerciseMessage) &&
                    feedback.ExerciseStatus == "warning")
                {
                    messages.Add(feedback.ExerciseMessage);
                }

                if (!string.IsNullOrEmpty(feedback.CigaretteMessage))
                {
                    messages.Add(feedback.CigaretteMessage);
                }

                if (!hasWarning)
                {
                    messages.Add("✅ 各項指標都在正常範圍內!");
                    messages.Add("請繼續保持良好的生活習慣 💪");
                }
                else
                {
                    messages.Add("");
                    messages.Add("⚠️ 請注意上述異常項目");
                    messages.Add("建議諮詢您的醫療團隊");
                }

                var messageText = string.Join("\n", messages);

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channelAccessToken}");

                var payload = new
                {
                    to = lineUserId,
                    messages = new[]
                    {
                        new { type = "text", text = messageText }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("https://api.line.me/v2/bot/message/push", content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"✅ LINE 通知發送成功 - UserId: {userId}");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"❌ LINE 通知發送失敗 - Status: {response.StatusCode}, Error: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 發送 LINE 通知時發生錯誤");
            }
        }

        private async Task<string> GetUserLineIdAsync(int userId)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT ""LineUserId""
                FROM ""Users""
                WHERE ""Id"" = @UserId
                LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }

        public class SearchRequest
        {
            public string idNumber { get; set; }
        }

        // 🆕 新增：取得特定日期的所有紀錄
        private async Task<List<HealthRecordViewModel>> GetUserRecordsByDateAsync(int userId, DateTime date)
        {
            var records = new List<HealthRecordViewModel>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
        SELECT * FROM ""Today""
        WHERE ""UserId"" = @UserId 
          AND ""RecordDate"" = @RecordDate
        ORDER BY ""RecordTime"" DESC";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@RecordDate", date.Date);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dbModel = MapFromReader(reader);
                records.Add(dbModel.ToViewModel());
            }

            return records;
        }

        // 🆕 新增：使用當日總計產生 Feedback
        private FeedbackViewModel GenerateFeedbackWithDailyTotal(List<HealthRecordViewModel> todayRecords)
        {
            var feedback = new FeedbackViewModel();

            // 計算今日總計
            var totalWater = todayRecords.Sum(r => r.WaterIntake ?? 0);
            var totalExercise = todayRecords.Sum(r => r.ExerciseDuration ?? 0);
            var totalCigarettes = todayRecords.Sum(r => r.Cigarettes ?? 0);

            // 血壓平均
            var allSystolic = todayRecords
                .SelectMany(r => new[] { r.BP_First_1_Systolic, r.BP_First_2_Systolic,
                                 r.BP_Second_1_Systolic, r.BP_Second_2_Systolic })
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .ToList();

            var allDiastolic = todayRecords
                .SelectMany(r => new[] { r.BP_First_1_Diastolic, r.BP_First_2_Diastolic,
                                 r.BP_Second_1_Diastolic, r.BP_Second_2_Diastolic })
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .ToList();

            var avgSystolic = allSystolic.Any() ? allSystolic.Average() : (decimal?)null;
            var avgDiastolic = allDiastolic.Any() ? allDiastolic.Average() : (decimal?)null;

            // 血糖平均
            var bloodSugars = todayRecords.Where(r => r.BloodSugar.HasValue).Select(r => r.BloodSugar.Value).ToList();
            var avgBloodSugar = bloodSugars.Any() ? bloodSugars.Average() : (decimal?)null;

            // 💧 水分攝取（使用今日總計）
            if (totalWater > 0)
            {
                if (totalWater < HealthRecordViewModel.WATER_STANDARD)
                {
                    var diff = HealthRecordViewModel.WATER_STANDARD - (int)totalWater;
                    feedback.WaterMessage = $"💧 今日總水分攝取 {totalWater:0} ml，還差 {diff} ml 達到建議量！";
                    feedback.WaterStatus = "warning";
                }
                else
                {
                    feedback.WaterMessage = $"💧 太棒了！今日總水分攝取 {totalWater:0} ml，已達標準！";
                    feedback.WaterStatus = "success";
                }
            }

            // 🏃 運動時間（使用今日總計）
            if (totalExercise > 0)
            {
                if (totalExercise < HealthRecordViewModel.EXERCISE_STANDARD)
                {
                    var diff = HealthRecordViewModel.EXERCISE_STANDARD - (int)totalExercise;
                    feedback.ExerciseMessage = $"🏃 今日總運動時間 {totalExercise:0} 分鐘，可再增加 {diff} 分鐘達到建議量！";
                    feedback.ExerciseStatus = "warning";
                }
                else
                {
                    feedback.ExerciseMessage = $"🏃 很棒！今日總運動時間 {totalExercise:0} 分鐘，已達標準！";
                    feedback.ExerciseStatus = "success";
                }
            }

            // 🚬 抽菸（使用今日總計）
            if (totalCigarettes > 0)
            {
                if (totalCigarettes < 3)
                {
                    feedback.CigaretteMessage = $"🚭 今日抽菸 {totalCigarettes:0} 支，量很少！繼續努力戒菸！";
                    feedback.CigaretteStatus = "success";
                }
                else if (totalCigarettes <= 7)
                {
                    feedback.CigaretteMessage = $"🚭 今日抽菸 {totalCigarettes:0} 支，加油！抽得越少身體越健康！";
                    feedback.CigaretteStatus = "info";
                }
                else
                {
                    feedback.CigaretteMessage = $"⚠️ 今日抽菸量 {totalCigarettes:0} 支較多，建議尋求戒菸協助！";
                    feedback.CigaretteStatus = "danger";
                }
            }

            // ❤️ 血壓 (使用今日平均)
            if (avgSystolic.HasValue && avgSystolic.Value > 120)
            {
                feedback.BloodPressureMessage = $"⚠️ 今日平均收縮壓 {avgSystolic.Value:0} mmHg 偏高（>120），建議注意飲食與作息！";
                feedback.BloodPressureStatus = "danger";
            }
            else if (avgDiastolic.HasValue && avgDiastolic.Value > 80)
            {
                feedback.BloodPressureMessage = $"⚠️ 今日平均舒張壓 {avgDiastolic.Value:0} mmHg 偏高（>80），建議注意飲食與作息！";
                feedback.BloodPressureStatus = "danger";
            }
            else if (avgSystolic.HasValue || avgDiastolic.HasValue)
            {
                feedback.BloodPressureMessage = $"✅ 今日血壓 {avgSystolic:0}/{avgDiastolic:0} mmHg 正常，繼續保持！";
                feedback.BloodPressureStatus = "success";
            }

            // 🩸 血糖 (使用今日平均)
            if (avgBloodSugar.HasValue && avgBloodSugar.Value > 99)
            {
                feedback.BloodSugarMessage = $"⚠️ 今日平均血糖 {avgBloodSugar.Value:0.0} mg/dL 偏高（>99），建議控制飲食！";
                feedback.BloodSugarStatus = "danger";
            }
            else if (avgBloodSugar.HasValue)
            {
                feedback.BloodSugarMessage = $"✅ 今日血糖 {avgBloodSugar.Value:0.0} mg/dL 正常，繼續維持！";
                feedback.BloodSugarStatus = "success";
            }

            return feedback;
        }
    }
}