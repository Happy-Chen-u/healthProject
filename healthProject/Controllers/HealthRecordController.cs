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
        public async Task<IActionResult> Create(
            int? Id,
            decimal? BP_First_1_Systolic, decimal? BP_First_1_Diastolic,
            decimal? BP_First_2_Systolic, decimal? BP_First_2_Diastolic,
            decimal? BP_Second_1_Systolic, decimal? BP_Second_1_Diastolic,
            decimal? BP_Second_2_Systolic, decimal? BP_Second_2_Diastolic,
            bool? BP_Morning_NotMeasured, bool? BP_Evening_NotMeasured, // 🆕 新增
            string? Meals_Breakfast, string? Meals_Lunch, string? Meals_Dinner,
            string? ExerciseType, decimal? ExerciseDuration,
            decimal? WaterIntake, string? Beverage,
            decimal? Cigarettes, decimal? BetelNut, decimal? BloodSugar)
        {
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index");
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var todayRecords = await GetUserRecordsByDateAsync(userId, DateTime.Today);

            // 🎯 修正點 (邏輯 1: 檢查當日是否已完成該時段的完整紀錄)
            bool isMorningCompleted = todayRecords.Any(r =>
                r.BP_First_1_Systolic.HasValue && r.BP_First_1_Diastolic.HasValue &&
                r.BP_First_2_Systolic.HasValue && r.BP_First_2_Diastolic.HasValue);

            bool isEveningCompleted = todayRecords.Any(r =>
                r.BP_Second_1_Systolic.HasValue && r.BP_Second_1_Diastolic.HasValue &&
                r.BP_Second_2_Systolic.HasValue && r.BP_Second_2_Diastolic.HasValue);

            var model = new HealthRecordViewModel
            {
                RecordDate = DateTime.Today,
                RecordTime = DateTime.Now.TimeOfDay,
                // 傳遞完成狀態給 ViewModel 的驗證邏輯
                IsMorningCompletedToday = isMorningCompleted,
                IsEveningCompletedToday = isEveningCompleted,
            };

            // 🆕 如果有帶參數(從 Confirm 返回),填入資料
            if (BP_First_1_Systolic.HasValue || BloodSugar.HasValue || WaterIntake.HasValue || BP_Morning_NotMeasured.HasValue)
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
                // 🆕 恢復血壓狀態
                model.BP_Morning_NotMeasured = BP_Morning_NotMeasured;
                model.BP_Evening_NotMeasured = BP_Evening_NotMeasured;

                // 🆕 三餐 JSON 反序列化 (省略不變)
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
            // 🎯 修正點 (三餐必填錯誤): 在所有驗證邏輯和 ModelState.IsValid 檢查之前，先移除三餐欄位的錯誤。
            // 這是解決 "Meals_Breakfast field is required" 錯誤的關鍵步驟。
            ModelState.Remove("Meals_Breakfast");
            ModelState.Remove("Meals_Lunch");
            ModelState.Remove("Meals_Dinner");

            // 🎯 修正點 (三餐): 儘早將 JSON 字串反序列化成物件，供後續邏輯使用
            if (!string.IsNullOrEmpty(Meals_Breakfast))
                model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(Meals_Breakfast);
            if (!string.IsNullOrEmpty(Meals_Lunch))
                model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(Meals_Lunch);
            if (!string.IsNullOrEmpty(Meals_Dinner))
                model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(Meals_Dinner);


            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var todayRecords = await GetUserRecordsByDateAsync(userId, DateTime.Today);

            // 🎯 修正點 (邏輯 1: 傳遞當日完成狀態給 ViewModel 進行必填豁免判斷)
            model.IsMorningCompletedToday = todayRecords.Any(r =>
                r.BP_First_1_Systolic.HasValue && r.BP_First_1_Diastolic.HasValue &&
                r.BP_First_2_Systolic.HasValue && r.BP_First_2_Diastolic.HasValue);

            model.IsEveningCompletedToday = todayRecords.Any(r =>
                r.BP_Second_1_Systolic.HasValue && r.BP_Second_1_Diastolic.HasValue &&
                r.BP_Second_2_Systolic.HasValue && r.BP_Second_2_Diastolic.HasValue);


            // 🎯 修正點 (執行 ViewModel 內的強制限值和配對檢查)
            var bpWarnings = model.ValidateBloodPressure();
            if (bpWarnings.Any())
            {
                // 檢查是否有硬性錯誤 (必填錯誤、兩遍配對錯誤、收縮/舒張配對錯誤)
                if (bpWarnings.Any(w => w.StartsWith("🔴")))
                {
                    TempData["BPWarnings"] = string.Join("\n", bpWarnings);
                    // 返回 View，顯示錯誤
                    return View(model);
                }
                // 其他是警告 (數值過低)，暫存起來
                TempData["BPWarnings"] = string.Join("\n", bpWarnings);
            }

            // --- 重複提交檢查邏輯 (保持不變) ---

            // 1. 檢查今日紀錄是否已存在「第一次量測」的數據 (任一欄位有值即算)
            bool todayHasFirstBP = todayRecords.Any(r =>
                r.BP_First_1_Systolic.HasValue || r.BP_First_1_Diastolic.HasValue ||
                r.BP_First_2_Systolic.HasValue || r.BP_First_2_Diastolic.HasValue
            );

            // 2. 檢查今日紀錄是否已存在「第二次量測」的數據 (任一欄位有值即算)
            bool todayHasSecondBP = todayRecords.Any(r =>
                r.BP_Second_1_Systolic.HasValue || r.BP_Second_1_Diastolic.HasValue ||
                r.BP_Second_2_Systolic.HasValue || r.BP_Second_2_Diastolic.HasValue
            );

            // 3. 檢查當前表單是否有填寫「第一次量測」的血壓 (排除勾選尚未測量)
            bool currentHasFirstBP = (model.BP_Morning_NotMeasured != true) &&
                                     (model.BP_First_1_Systolic.HasValue || model.BP_First_1_Diastolic.HasValue ||
                                      model.BP_First_2_Systolic.HasValue || model.BP_First_2_Diastolic.HasValue);

            // 4. 檢查當前表單是否有填寫「第二次量測」的血壓 (排除勾選尚未測量)
            bool currentHasSecondBP = (model.BP_Evening_NotMeasured != true) &&
                                      (model.BP_Second_1_Systolic.HasValue || model.BP_Second_1_Diastolic.HasValue ||
                                       model.BP_Second_2_Systolic.HasValue || model.BP_Second_2_Diastolic.HasValue);

            // 檢查是否有填寫其他資訊 (不變)
            bool hasOtherData = !string.IsNullOrEmpty(Meals_Breakfast) || !string.IsNullOrEmpty(Meals_Lunch) ||
                                !string.IsNullOrEmpty(Meals_Dinner) || !string.IsNullOrEmpty(model.ExerciseType) ||
                                model.ExerciseDuration.HasValue || model.WaterIntake.HasValue ||
                                !string.IsNullOrEmpty(model.Beverage) || model.Cigarettes.HasValue ||
                                model.BetelNut.HasValue || model.BloodSugar.HasValue;

            bool hasDuplicatedBP = false;
            string warningMessage = "";
            string bpSection = "";

            if (currentHasFirstBP && todayHasFirstBP)
            {
                hasDuplicatedBP = true;
                bpSection = "第一次 (上午)";
            }
            else if (currentHasSecondBP && todayHasSecondBP)
            {
                hasDuplicatedBP = true;
                bpSection = "第二次 (睡前)";
            }

            // 如果發現重複提交同一時段的血壓，則導向警告頁面 (不變)
            if (hasDuplicatedBP)
            {
                warningMessage = $"⚠️ 您今天已記錄過【{bpSection}】的血壓數據。若要修改請使用『編輯』功能，請勿重複新增。";

                ViewBag.BPWarningMessage = warningMessage;
                ViewBag.HasOtherData = hasOtherData;

                // 傳遞所有表單資料到 View
                ViewBag.FormData = new
                {
                    model.BP_First_1_Systolic,
                    model.BP_First_1_Diastolic,
                    model.BP_First_2_Systolic,
                    model.BP_First_2_Diastolic,
                    model.BP_Second_1_Systolic,
                    model.BP_Second_1_Diastolic,
                    model.BP_Second_2_Systolic,
                    model.BP_Second_2_Diastolic,
                    BP_Morning_NotMeasured = model.BP_Morning_NotMeasured ?? false,
                    BP_Evening_NotMeasured = model.BP_Evening_NotMeasured ?? false,
                    Meals_Breakfast,
                    Meals_Lunch,
                    Meals_Dinner,
                    model.ExerciseType,
                    model.ExerciseDuration,
                    model.WaterIntake,
                    model.Beverage,
                    model.Cigarettes,
                    model.BetelNut,
                    model.BloodSugar
                };

                return View("BPWarning", model);
            }
            // --- 重複提交檢查邏輯結束 ---

            // 移除不必要的 ModelState.Remove (因為已經在開頭移除了三餐)
            // ModelState.Remove("Meals_Breakfast"); // 移除
            // ModelState.Remove("Meals_Lunch"); // 移除
            // ModelState.Remove("Meals_Dinner"); // 移除

            if (!ModelState.IsValid)
            {
                return View(model);
            }

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

                // 產生建議訊息(新增時一定是今日,所以 isToday = true)
                var feedback = GenerateFeedbackWithDailyTotal(todayRecords, DateTime.Today, isToday: true);
                TempData["Feedback"] = JsonSerializer.Serialize(feedback);

                // 🔔 發送 LINE 通知(新增時一定是今日)
                await SendLineNotification(userId, feedback, DateTime.Today, isToday: true);

                return RedirectToAction("Success");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存今日健康紀錄失敗");
                ModelState.AddModelError("", "儲存失敗,請稍後再試");
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

                // 🆕 從資料庫讀取原始紀錄的日期
                var originalRecord = await GetRecordByIdAsync(model.Id);
                if (originalRecord == null)
                {
                    return NotFound();
                }

                // 使用原始紀錄的日期
                DateTime recordDate = originalRecord.RecordDate;

                _logger.LogInformation($"從資料庫讀取的原始日期: {recordDate:yyyy-MM-dd}");

                await UpdateRecordAsync(model);

                // 判斷編輯的日期是否為今天
                bool isToday = recordDate.Date == DateTime.Today;

                _logger.LogInformation($"isToday: {isToday}");

                // 取得該日期的所有紀錄來計算總計
                var recordsOnDate = await GetUserRecordsByDateAsync(userId, recordDate);

                // 產生建議訊息
                var feedback = GenerateFeedbackWithDailyTotal(recordsOnDate, recordDate, isToday);
                TempData["Feedback"] = JsonSerializer.Serialize(feedback);

                // 發送 LINE 通知
                await SendLineNotification(userId, feedback, recordDate, isToday);

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

                // 🆕 日期篩選
                if (!string.IsNullOrEmpty(request.startDate) || !string.IsNullOrEmpty(request.endDate))
                {
                    DateTime? start = string.IsNullOrEmpty(request.startDate) ? null : DateTime.Parse(request.startDate);
                    DateTime? end = string.IsNullOrEmpty(request.endDate) ? null : DateTime.Parse(request.endDate);

                    if (start.HasValue)
                    {
                        records = records.Where(r => r.RecordDate >= start.Value).ToList();
                    }
                    if (end.HasValue)
                    {
                        records = records.Where(r => r.RecordDate <= end.Value).ToList();
                    }
                }

                // 按日期分組並計算每日統計
                var dailyStats = records
                    .GroupBy(r => r.RecordDate.Date)
                    .Select(g => {
                        var dayRecords = g.ToList();

                        var bpReadings = new List<object>();
                        foreach (var r in dayRecords)
                        {
                            if (r.BP_First_1_Systolic.HasValue && r.BP_First_1_Diastolic.HasValue)
                                bpReadings.Add(new
                                {
                                    label = "第一次第一遍",
                                    systolic = Math.Round(r.BP_First_1_Systolic.Value),
                                    diastolic = Math.Round(r.BP_First_1_Diastolic.Value)
                                });
                            if (r.BP_First_2_Systolic.HasValue && r.BP_First_2_Diastolic.HasValue)
                                bpReadings.Add(new
                                {
                                    label = "第一次第二遍",
                                    systolic = Math.Round(r.BP_First_2_Systolic.Value),
                                    diastolic = Math.Round(r.BP_First_2_Diastolic.Value)
                                });
                            if (r.BP_Second_1_Systolic.HasValue && r.BP_Second_1_Diastolic.HasValue)
                                bpReadings.Add(new
                                {
                                    label = "第二次第一遍",
                                    systolic = Math.Round(r.BP_Second_1_Systolic.Value),
                                    diastolic = Math.Round(r.BP_Second_1_Diastolic.Value)
                                });
                            if (r.BP_Second_2_Systolic.HasValue && r.BP_Second_2_Diastolic.HasValue)
                                bpReadings.Add(new
                                {
                                    label = "第二次第二遍",
                                    systolic = Math.Round(r.BP_Second_2_Systolic.Value),
                                    diastolic = Math.Round(r.BP_Second_2_Diastolic.Value)
                                });
                        }

                        var totalWater = dayRecords.Sum(r => r.WaterIntake ?? 0);
                        var totalExercise = dayRecords.Sum(r => r.ExerciseDuration ?? 0);
                        var totalCigarettes = dayRecords.Sum(r => r.Cigarettes ?? 0);
                        var totalBetelNut = dayRecords.Sum(r => r.BetelNut ?? 0);

                        var bloodSugars = dayRecords.Where(r => r.BloodSugar.HasValue).Select(r => r.BloodSugar.Value).ToList();
                        var avgBloodSugar = bloodSugars.Any() ? bloodSugars.Average() : (decimal?)null;

                        var exercises = dayRecords
                            .Where(r => !string.IsNullOrEmpty(r.ExerciseType) || r.ExerciseDuration.HasValue)
                            .Select(r => new {
                                type = r.ExerciseType ?? "運動",
                                duration = r.ExerciseDuration ?? 0
                            })
                            .Where(e => e.duration > 0)
                            .ToList();

                        var beverages = dayRecords
                            .Where(r => !string.IsNullOrEmpty(r.Beverage))
                            .Select(r => r.Beverage)
                            .ToList();

                        var breakfastMeals = dayRecords.Where(r => r.Meals_Breakfast != null).Select(r => r.Meals_Breakfast).ToList();
                        var lunchMeals = dayRecords.Where(r => r.Meals_Lunch != null).Select(r => r.Meals_Lunch).ToList();
                        var dinnerMeals = dayRecords.Where(r => r.Meals_Dinner != null).Select(r => r.Meals_Dinner).ToList();

                        return new
                        {
                            date = g.Key.ToString("yyyy-MM-dd"),
                            bloodPressure = bpReadings.Any() ? new { readings = bpReadings } : null,
                            bloodSugar = avgBloodSugar.HasValue ? Math.Round(avgBloodSugar.Value, 1) : (decimal?)null,
                            exercise = exercises.Any() ? exercises : null,
                            water = totalWater > 0 ? totalWater : (decimal?)null,
                            beverage = beverages.Any() ? string.Join(", ", beverages) : null,
                            breakfast = breakfastMeals.Any() ? breakfastMeals : null,
                            lunch = lunchMeals.Any() ? lunchMeals : null,
                            dinner = dinnerMeals.Any() ? dinnerMeals : null,
                            cigarettes = totalCigarettes > 0 ? totalCigarettes : (decimal?)null,
                            betelNut = totalBetelNut > 0 ? totalBetelNut : (decimal?)null
                        };
                    })
                    .OrderByDescending(s => s.date)
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        patientName = patient.FullName,
                        idNumber = patient.IDNumber,
                        gender = patient.Gender,
                        birthDate = patient.BirthDate?.ToString("yyyy/MM/dd"),
                        dailyStats = dailyStats
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜尋失敗");
                return Json(new { success = false, message = "系統錯誤" });
            }
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmWithoutBP(string formDataJson)
        {
            try
            {
                var formData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(formDataJson);

                var model = new HealthRecordViewModel
                {
                    UserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)),
                    RecordDate = DateTime.Today,
                    RecordTime = DateTime.Now.TimeOfDay,

                    // 🆕 不包含血壓資料
                    BP_First_1_Systolic = null,
                    BP_First_1_Diastolic = null,
                    BP_First_2_Systolic = null,
                    BP_First_2_Diastolic = null,
                    BP_Second_1_Systolic = null,
                    BP_Second_1_Diastolic = null,
                    BP_Second_2_Systolic = null,
                    BP_Second_2_Diastolic = null,
                    // 🎯 修正點 (傳遞血壓狀態，讓資料庫知道這次是特意不填)
                    BP_Morning_NotMeasured = formData.ContainsKey("BP_Morning_NotMeasured") && formData["BP_Morning_NotMeasured"].ValueKind == JsonValueKind.True,
                    BP_Evening_NotMeasured = formData.ContainsKey("BP_Evening_NotMeasured") && formData["BP_Evening_NotMeasured"].ValueKind == JsonValueKind.True
                };

                // ... (恢復其他資料邏輯保持不變)
                if (formData.ContainsKey("Meals_Breakfast") && formData["Meals_Breakfast"].ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrEmpty(formData["Meals_Breakfast"].GetString()))
                    model.Meals_Breakfast = JsonSerializer.Deserialize<MealSelection>(formData["Meals_Breakfast"].GetString());
                if (formData.ContainsKey("Meals_Lunch") && formData["Meals_Lunch"].ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrEmpty(formData["Meals_Lunch"].GetString()))
                    model.Meals_Lunch = JsonSerializer.Deserialize<MealSelection>(formData["Meals_Lunch"].GetString());
                if (formData.ContainsKey("Meals_Dinner") && formData["Meals_Dinner"].ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrEmpty(formData["Meals_Dinner"].GetString()))
                    model.Meals_Dinner = JsonSerializer.Deserialize<MealSelection>(formData["Meals_Dinner"].GetString());

                if (formData.ContainsKey("ExerciseType") && formData["ExerciseType"].ValueKind == System.Text.Json.JsonValueKind.String)
                    model.ExerciseType = formData["ExerciseType"].GetString();
                if (formData.ContainsKey("ExerciseDuration") && formData["ExerciseDuration"].ValueKind == System.Text.Json.JsonValueKind.Number)
                    model.ExerciseDuration = formData["ExerciseDuration"].GetDecimal();
                if (formData.ContainsKey("WaterIntake") && formData["WaterIntake"].ValueKind == System.Text.Json.JsonValueKind.Number)
                    model.WaterIntake = formData["WaterIntake"].GetDecimal();
                if (formData.ContainsKey("Beverage") && formData["Beverage"].ValueKind == System.Text.Json.JsonValueKind.String)
                    model.Beverage = formData["Beverage"].GetString();
                if (formData.ContainsKey("Cigarettes") && formData["Cigarettes"].ValueKind == System.Text.Json.JsonValueKind.Number)
                    model.Cigarettes = formData["Cigarettes"].GetDecimal();
                if (formData.ContainsKey("BetelNut") && formData["BetelNut"].ValueKind == System.Text.Json.JsonValueKind.Number)
                    model.BetelNut = formData["BetelNut"].GetDecimal();
                if (formData.ContainsKey("BloodSugar") && formData["BloodSugar"].ValueKind == System.Text.Json.JsonValueKind.Number)
                    model.BloodSugar = formData["BloodSugar"].GetDecimal();

                // 顯示確認頁面
                return View("Confirm", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "處理表單資料失敗");
                return RedirectToAction("Create");
            }
        }

        // ========================================
        // 📊 查看個案填寫紀錄 - 管理員專用 
        // ========================================
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult GetPatientRecords(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber))
            {
                TempData["ErrorMessage"] = "請提供個案的身分證字號。";
                return RedirectToAction("Index");
            }

            // 直接返回 AdminSearch 頁面,並透過 ViewBag 傳遞身分證字號
            ViewBag.AutoSearchIdNumber = idNumber;
            return View("AdminSearch");
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
         ""BP_Morning_NotMeasured"", ""BP_Evening_NotMeasured"",  -- 🆕 新增欄位
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
         @BP_Morning_NotMeasured, @BP_Evening_NotMeasured,     -- 🆕 新增參數
         @Meals_Breakfast::jsonb, @Meals_Lunch::jsonb, @Meals_Dinner::jsonb,
         @ExerciseType, @ExerciseDuration,
         @WaterIntake, @Beverage, @Cigarettes,
         @BetelNut, @BloodSugar)";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", model.UserId);
            cmd.Parameters.AddWithValue("@RecordDate", model.RecordDate);
            cmd.Parameters.AddWithValue("@RecordTime", model.RecordTime ?? (object)DBNull.Value);

            // 血壓 - 8個欄位 (不變)
            cmd.Parameters.AddWithValue("@BP_First_1_Systolic", model.BP_First_1_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_1_Diastolic", model.BP_First_1_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_2_Systolic", model.BP_First_2_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_First_2_Diastolic", model.BP_First_2_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_1_Systolic", model.BP_Second_1_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_1_Diastolic", model.BP_Second_1_Diastolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_2_Systolic", model.BP_Second_2_Systolic ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Second_2_Diastolic", model.BP_Second_2_Diastolic ?? (object)DBNull.Value);

            // 🎯 修正點：新增血壓狀態參數
            cmd.Parameters.AddWithValue("@BP_Morning_NotMeasured", model.BP_Morning_NotMeasured ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BP_Evening_NotMeasured", model.BP_Evening_NotMeasured ?? (object)DBNull.Value);

            // ⚠️ 三餐 JSON (不變)
            cmd.Parameters.AddWithValue("@Meals_Breakfast",
                model.Meals_Breakfast != null ? JsonSerializer.Serialize(model.Meals_Breakfast) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals_Lunch",
                model.Meals_Lunch != null ? JsonSerializer.Serialize(model.Meals_Lunch) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Meals_Dinner",
                model.Meals_Dinner != null ? JsonSerializer.Serialize(model.Meals_Dinner) : (object)DBNull.Value);

            // 其他 (不變)
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

        private async Task<PatientBasicInfo> GetPatientByIdNumberAsync(string idNumber)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // 🆕 從 Users 和 CaseManagement 表聯合查詢
            var query = @"
        SELECT 
            u.""Id"", 
            u.""FullName"", 
            u.""IDNumber"",
            cm.""Gender"",
            cm.""BirthDate""
        FROM ""Users"" u
        LEFT JOIN ""CaseManagement"" cm ON u.""IDNumber"" = cm.""IDNumber""
        WHERE u.""IDNumber"" = @IDNumber AND u.""IsActive"" = true
        LIMIT 1";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@IDNumber", idNumber);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PatientBasicInfo
                {
                    Id = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    IDNumber = reader.GetString(2),
                    Gender = reader.IsDBNull(3) ? null : reader.GetString(3),
                    BirthDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4)
                };
            }

            return null;
        }

        // 🆕 新增 PatientBasicInfo 類別（放在 SearchRequest 下方）
        public class PatientBasicInfo
        {
            public int Id { get; set; }
            public string FullName { get; set; }
            public string IDNumber { get; set; }
            public string Gender { get; set; }
            public DateTime? BirthDate { get; set; }
        }

        // ========================================
        // 🧠 資料庫讀取 - MapFromReader 
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
                BP_Morning_NotMeasured = reader.IsDBNull(reader.GetOrdinal("BP_Morning_NotMeasured"))
                    ? null : reader.GetBoolean(reader.GetOrdinal("BP_Morning_NotMeasured")),
                BP_Evening_NotMeasured = reader.IsDBNull(reader.GetOrdinal("BP_Evening_NotMeasured"))
                    ? null : reader.GetBoolean(reader.GetOrdinal("BP_Evening_NotMeasured")),

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
        private async Task SendLineNotification(int userId, FeedbackViewModel feedback, DateTime recordDate, bool isToday)
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
                messages.Add("📊 【代謝症候群追蹤與管理系統】");

                // 🆕 根據是否為今日決定標題訊息
                if (isToday)
                {
                    messages.Add("今日健康資訊已記錄成功!");
                }
                else
                {
                    messages.Add($"您在 {recordDate:MM/dd} 的資訊已更新成功");
                }

                messages.Add("═══════════════");

                bool hasWarning = false;

                // 血壓警示
                if (!string.IsNullOrEmpty(feedback.BloodPressureMessage) &&
                    feedback.BloodPressureStatus == "danger")
                {
                    messages.Add(feedback.BloodPressureMessage);
                    hasWarning = true;
                }

                // 血糖警示
                if (!string.IsNullOrEmpty(feedback.BloodSugarMessage) &&
                    feedback.BloodSugarStatus == "danger")
                {
                    messages.Add(feedback.BloodSugarMessage);
                    hasWarning = true;
                }

                // 🆕 水分提醒（warning 也要顯示）
                if (!string.IsNullOrEmpty(feedback.WaterMessage))
                {
                    messages.Add(feedback.WaterMessage);
                }

                // 🆕 運動提醒（warning 也要顯示）
                if (!string.IsNullOrEmpty(feedback.ExerciseMessage))
                {
                    messages.Add(feedback.ExerciseMessage);
                }

                // 🆕 抽菸提醒（所有狀態都要顯示）
                if (!string.IsNullOrEmpty(feedback.CigaretteMessage))
                {
                    messages.Add(feedback.CigaretteMessage);
                }

                // 🆕 結尾訊息
                if (!hasWarning)
                {
                    // 沒有危險警示
                    messages.Add("");
                    messages.Add("✅ 各項指標都在正常範圍內!");
                    messages.Add("請繼續保持良好的生活習慣 💪");
                }
                else
                {
                    // 有危險警示
                    messages.Add("");
                    if (isToday)
                    {
                        messages.Add("⚠️ 請注意上述異常項目");
                        //messages.Add("建議諮詢您的醫療團隊");
                    }
                    else
                    {
                        messages.Add("⚠️ 請注意上述異常項目");
                        messages.Add("加油!");
                    }
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
                    _logger.LogInformation($"✅ LINE 通知發送成功 - UserId: {userId}, Date: {recordDate:yyyy-MM-dd}, IsToday: {isToday}");
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
            public string startDate { get; set; }
            public string endDate { get; set; }
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
        // 🆕 新增:使用當日總計產生 Feedback(新增日期和是否為今日參數)
        private FeedbackViewModel GenerateFeedbackWithDailyTotal(List<HealthRecordViewModel> records, DateTime recordDate, bool isToday)
        {
            var feedback = new FeedbackViewModel();

            // 🆕 儲存日期資訊
            feedback.RecordDate = recordDate;
            feedback.IsToday = isToday;

            // 計算總計
            var totalWater = records.Sum(r => r.WaterIntake ?? 0);
            var totalExercise = records.Sum(r => r.ExerciseDuration ?? 0);
            var totalCigarettes = records.Sum(r => r.Cigarettes ?? 0);

            // 血壓平均
            var allSystolic = records
                .SelectMany(r => new[] { r.BP_First_1_Systolic, r.BP_First_2_Systolic,
                         r.BP_Second_1_Systolic, r.BP_Second_2_Systolic })
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .ToList();

            var allDiastolic = records
                .SelectMany(r => new[] { r.BP_First_1_Diastolic, r.BP_First_2_Diastolic,
                         r.BP_Second_1_Diastolic, r.BP_Second_2_Diastolic })
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .ToList();

            var avgSystolic = allSystolic.Any() ? allSystolic.Average() : (decimal?)null;
            var avgDiastolic = allDiastolic.Any() ? allDiastolic.Average() : (decimal?)null;

            // 血糖平均
            var bloodSugars = records.Where(r => r.BloodSugar.HasValue).Select(r => r.BloodSugar.Value).ToList();
            var avgBloodSugar = bloodSugars.Any() ? bloodSugars.Average() : (decimal?)null;
            
            // 檢查該日是否有任何有效的血壓紀錄（有填數值）
            bool hasAnyActualBPReading = records.Any(r => r.AvgSystolicBP.HasValue || r.AvgDiastolicBP.HasValue);
            bool hasAnyNotMeasured = records.Any(r => r.BP_Morning_NotMeasured == true || r.BP_Evening_NotMeasured == true);

            // 🆕 根據是否為今日設定訊息前綴
            string prefix = isToday ? "今日" : "";

            // 💧 水分攝取
            if (totalWater > 0)
            {
                if (totalWater < HealthRecordViewModel.WATER_STANDARD)
                {
                    var diff = HealthRecordViewModel.WATER_STANDARD - (int)totalWater;
                    feedback.WaterMessage = $"💧 {prefix}總水分攝取 {totalWater:0} ml，還差 {diff} ml 達到建議量！";
                    feedback.WaterStatus = "warning";
                }
                else
                {
                    feedback.WaterMessage = $"💧 太棒了！{prefix}總水分攝取 {totalWater:0} ml，已達標準！";
                    feedback.WaterStatus = "success";
                }
            }

            // 🏃 運動時間
            if (totalExercise > 0)
            {
                if (totalExercise < HealthRecordViewModel.EXERCISE_STANDARD)
                {
                    var diff = HealthRecordViewModel.EXERCISE_STANDARD - (int)totalExercise;
                    feedback.ExerciseMessage = $"🏃 {prefix}總運動時間 {totalExercise:0} 分鐘，可再增加 {diff} 分鐘達到建議量！";
                    feedback.ExerciseStatus = "warning";
                }
                else
                {
                    feedback.ExerciseMessage = $"🏃 很棒！{prefix}總運動時間 {totalExercise:0} 分鐘，已達標準！";
                    feedback.ExerciseStatus = "success";
                }
            }

            // 🚬 抽菸
            if (totalCigarettes > 0)
            {
                if (totalCigarettes < 3)
                {
                    feedback.CigaretteMessage = $"🚭 {prefix}抽菸 {totalCigarettes:0} 支，量很少！繼續努力戒菸！";
                    feedback.CigaretteStatus = "success";
                }
                else if (totalCigarettes <= 7)
                {
                    feedback.CigaretteMessage = $"🚭 {prefix}抽菸 {totalCigarettes:0} 支，加油！抽得越少身體越健康！";
                    feedback.CigaretteStatus = "info";
                }
                else
                {
                    feedback.CigaretteMessage = $"⚠️ {prefix}抽菸量 {totalCigarettes:0} 支較多，建議尋求戒菸協助！";
                    feedback.CigaretteStatus = "danger";
                }
            }

            // ❤️ 血壓
            if (avgSystolic.HasValue && avgSystolic.Value > 120)
            {
                feedback.BloodPressureMessage = $"⚠️ {prefix}平均收縮壓 {avgSystolic.Value:0} mmHg 偏高（>120），建議注意飲食與作息！";
                feedback.BloodPressureStatus = "danger";
            }
            else if (avgDiastolic.HasValue && avgDiastolic.Value > 80)
            {
                feedback.BloodPressureMessage = $"⚠️ {prefix}平均舒張壓 {avgDiastolic.Value:0} mmHg 偏高（>80），建議注意飲食與作息！";
                feedback.BloodPressureStatus = "danger";
            }
            else if (avgSystolic.HasValue || avgDiastolic.HasValue)
            {
                feedback.BloodPressureMessage = $"✅ {prefix}血壓 {avgSystolic:0}/{avgDiastolic:0} mmHg 正常，繼續保持！";
                feedback.BloodPressureStatus = "success";
            }

            // 🩸 血糖
            if (avgBloodSugar.HasValue && avgBloodSugar.Value > 99)
            {
                feedback.BloodSugarMessage = $"⚠️ {prefix}平均血糖 {avgBloodSugar.Value:0.0} mg/dL 偏高（>99），建議控制飲食！";
                feedback.BloodSugarStatus = "danger";
            }
            else if (avgBloodSugar.HasValue)
            {
                feedback.BloodSugarMessage = $"✅ {prefix}血糖 {avgBloodSugar.Value:0.0} mg/dL 正常，繼續維持！";
                feedback.BloodSugarStatus = "success";
            }

            return feedback;
        }
    }
}