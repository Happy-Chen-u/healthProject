using healthProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Collections.Generic;
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
        // ➕ 新增今日紀錄 - 病患專用
        // ========================================
        
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index");
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // ✅ 檢查今天是否已經有紀錄
            var todayRecord = await GetTodayRecordAsync(userId);

            if (todayRecord != null)
            {
                // 今天已經上傳過了，顯示提示並導向編輯頁面
                TempData["InfoMessage"] = "📝 您今天已經上傳過健康資訊了，如需修改請在下方編輯。";
                return RedirectToAction("Edit", new { id = todayRecord.Id });
            }

            var model = new HealthRecordViewModel
            {
                RecordDate = DateTime.Today
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HealthRecordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            model.UserId = userId;
            model.RecordDate = DateTime.Today;

            // 顯示確認頁面
            return View("Confirm", model);
        }

        // ========================================
        // ✅ 確認上傳
        // ========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmSubmit(HealthRecordViewModel model)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                model.UserId = userId;

                await SaveRecordAsync(model);

                // 產生建議訊息
                var feedback = GenerateFeedback(model);
                TempData["Feedback"] = JsonSerializer.Serialize(feedback);

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
        // 📚 查看歷史紀錄 - 病患專用
        // ========================================
        public async Task<IActionResult> MyRecords()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var records = await GetUserRecordsAsync(userId);
            return View(records);
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
        public async Task<IActionResult> Edit(HealthRecordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Create", model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (model.UserId != userId)
            {
                return Forbid();
            }

            try
            {
                await UpdateRecordAsync(model);
                TempData["SuccessMessage"] = "紀錄更新成功！";
                return RedirectToAction("MyRecords");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新紀錄失敗");
                ModelState.AddModelError("", "更新失敗，請稍後再試");
                return View("Create", model);
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
                // 先找到病患
                var patient = await GetPatientByIdNumberAsync(request.idNumber);
                if (patient == null)
                {
                    return Json(new { success = false, message = "查無此病患" });
                }

                // 取得該病患的所有紀錄
                var records = await GetUserRecordsAsync(patient.Id);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        patientName = patient.FullName,
                        idNumber = patient.IDNumber,
                        records = records
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
        // 🧠 資料庫操作
        // ========================================
        private async Task SaveRecordAsync(HealthRecordViewModel model)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                INSERT INTO ""Today"" 
                (""UserId"", ""RecordDate"", ""ExerciseType"", ""ExerciseDuration"", 
                 ""WaterIntake"", ""Beverage"", ""Meals"", ""Cigarettes"", 
                 ""BetelNut"", ""BloodSugar"", ""SystolicBP"", ""DiastolicBP"")
                VALUES 
                (@UserId, @RecordDate, @ExerciseType, @ExerciseDuration,
                 @WaterIntake, @Beverage, @Meals, @Cigarettes,
                 @BetelNut, @BloodSugar, @SystolicBP, @DiastolicBP)";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", model.UserId);
            cmd.Parameters.AddWithValue("@RecordDate", model.RecordDate);
            cmd.Parameters.AddWithValue("@ExerciseType", model.ExerciseType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ExerciseDuration", model.ExerciseDuration ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@WaterIntake", model.WaterIntake ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Beverage", model.Beverage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals", model.Meals ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Cigarettes", model.Cigarettes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BetelNut", model.BetelNut ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BloodSugar", model.BloodSugar ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@SystolicBP", model.SystolicBP ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@DiastolicBP", model.DiastolicBP ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task UpdateRecordAsync(HealthRecordViewModel model)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                UPDATE ""Today""
                SET ""ExerciseType"" = @ExerciseType, ""ExerciseDuration"" = @ExerciseDuration,
                    ""WaterIntake"" = @WaterIntake, ""Beverage"" = @Beverage,
                    ""Meals"" = @Meals, ""Cigarettes"" = @Cigarettes,
                    ""BetelNut"" = @BetelNut, ""BloodSugar"" = @BloodSugar,
                    ""SystolicBP"" = @SystolicBP, ""DiastolicBP"" = @DiastolicBP
                WHERE ""Id"" = @Id AND ""UserId"" = @UserId";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            cmd.Parameters.AddWithValue("@UserId", model.UserId);
            cmd.Parameters.AddWithValue("@ExerciseType", model.ExerciseType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ExerciseDuration", model.ExerciseDuration ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@WaterIntake", model.WaterIntake ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Beverage", model.Beverage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals", model.Meals ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Cigarettes", model.Cigarettes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BetelNut", model.BetelNut ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BloodSugar", model.BloodSugar ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@SystolicBP", model.SystolicBP ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@DiastolicBP", model.DiastolicBP ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<HealthRecordViewModel>> GetUserRecordsAsync(int userId)
        {
            var records = new List<HealthRecordViewModel>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT * FROM ""Today""
                WHERE ""UserId"" = @UserId
                ORDER BY ""RecordDate"" DESC";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                records.Add(MapFromReader(reader));
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
                return MapFromReader(reader);
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

        // ========================================
        // 🧠 資料庫操作
        // ========================================

        // ✅ 新增這個方法
        private async Task<HealthRecordViewModel> GetTodayRecordAsync(int userId)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
        SELECT * FROM ""Today""
        WHERE ""UserId"" = @UserId AND ""RecordDate"" = @RecordDate
        LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@RecordDate", DateTime.Today);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapFromReader(reader);
            }

            return null;
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
            if (model.Cigarettes.HasValue)
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

            // 血壓 - 收縮壓
            if (model.SystolicBP.HasValue && model.SystolicBP.Value > 120)
            {
                feedback.BloodPressureMessage = "⚠️ 收縮壓偏高（>120 mmHg），建議注意飲食與作息，並諮詢醫師！";
                feedback.BloodPressureStatus = "danger";
            }
            // 血壓 - 舒張壓
            else if (model.DiastolicBP.HasValue && model.DiastolicBP.Value > 80)
            {
                feedback.BloodPressureMessage = "⚠️ 舒張壓偏高（>80 mmHg），建議注意飲食與作息，並諮詢醫師！";
                feedback.BloodPressureStatus = "danger";
            }
            else if (model.SystolicBP.HasValue || model.DiastolicBP.HasValue)
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

        public class SearchRequest
        {
            public string idNumber { get; set; }
        }
    }
}