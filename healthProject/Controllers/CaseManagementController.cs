using healthProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace healthProject.Controllers
{
    [Authorize]
    public class CaseManagementController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CaseManagementController> _logger;

        public CaseManagementController(IConfiguration configuration, ILogger<CaseManagementController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // ========================================
        // ✅ 建立新個案帳號（Users）
        // ========================================
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(UserViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "輸入資料格式錯誤，請重新檢查。";
                return View(viewModel);
            }

            try
            {
                var model = new UserDBModel
                {
                    IDNumber = viewModel.IDNumber,
                    Username = viewModel.IDNumber,
                    PasswordHash = viewModel.IDNumber, // 預設密碼 = 身分證號
                    Role = "Patient",
                    FullName = viewModel.FullName,
                    PhoneNumber = viewModel.PhoneNumber,
                    CreatedDate = DateTime.Now,
                    IsActive = true,
                    IsFirstLogin = true,
                    LineUserId = null
                };

                string connString = _configuration.GetConnectionString("DefaultConnection")
                    + ";SSL Mode=Require;Trust Server Certificate=True;";

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string sql = @"
                        INSERT INTO public.""Users"" 
                        (""IDNumber"", ""Username"", ""PasswordHash"", ""Role"", ""FullName"", 
                         ""CreatedDate"", ""IsActive"", ""PhoneNumber"", ""IsFirstLogin"", ""LineUserId"")
                        VALUES 
                        (@idnumber, @username, @passwordhash, @role, @fullname, 
                         @createddate, @isactive, @phonenumber, @isfirstlogin, @lineuserid);";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@idnumber", model.IDNumber);
                        cmd.Parameters.AddWithValue("@username", model.Username);
                        cmd.Parameters.AddWithValue("@passwordhash", model.PasswordHash);
                        cmd.Parameters.AddWithValue("@role", model.Role);
                        cmd.Parameters.AddWithValue("@fullname", model.FullName);
                        cmd.Parameters.AddWithValue("@createddate", model.CreatedDate);
                        cmd.Parameters.AddWithValue("@isactive", model.IsActive);
                        cmd.Parameters.AddWithValue("@phonenumber", model.PhoneNumber);
                        cmd.Parameters.AddWithValue("@isfirstlogin", model.IsFirstLogin);
                        cmd.Parameters.AddWithValue("@lineuserid", (object)DBNull.Value);

                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = $"✅ 已成功新增個案：{model.FullName}（身分證字號：{model.IDNumber}）！預設密碼為身分證字號。";
                return RedirectToAction("Create");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ 發生錯誤：{ex.Message}";
                return View(viewModel);
            }
        }

        // 在 CaseManagementController 裡加入這個 action（供病患查看自己的紀錄列表）
        [HttpGet]
        public async Task<IActionResult> PatientRecords()
        {
            // 確認使用者已登入
            if (!User.Identity.IsAuthenticated)
            {
                // 導到登入或回傳 401
                return Challenge(); // 或 RedirectToAction("Login", "Account");
            }

            // 取得登入使用者 Id（你的系統是在 Claims 裡放 NameIdentifier）
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                // 若取不到 Id，回傳錯誤或導回首頁
                return Forbid();
            }

            // 使用你已寫好的方法抓取該病患的紀錄
            var records = await GetUserRecordsAsync(userId);

            // 回傳 view（確保 Views/CaseManagement/PatientRecords.cshtml 存在）
            return View("PatientRecords", records);
        }


        // ========================================
        // 🏠 Index
        // ========================================
        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("Admin"))
            {
                return View("AdminMenu");
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var records = await GetUserRecordsAsync(userId);
            return View("PatientRecords", records);
        }

        // ========================================
        // ➕ 新增個案紀錄
        // ========================================
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult CreateRecord()
        {
            return View();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRecord(CaseManagementViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                await SaveRecordAsync(model);
                TempData["SuccessMessage"] = "紀錄新增成功!";
                return RedirectToAction("ViewAllRecords");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "新增紀錄失敗");
                ModelState.AddModelError("", "新增紀錄失敗，請稍後再試");
                return View(model);
            }
        }


        // ========================================
        // 🔍 查詢病患 
        // ========================================
       
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> SearchPatient([FromBody] SearchRequest request)
        {
            try
            {
                var patient = await GetPatientByIdNumberAsync(request.idNumber);

                if (patient == null)
                {
                    return Json(new { success = false, message = "查無此病患資料" });
                }

                // ⭐ 不再檢查是否已有紀錄，直接回傳病患資訊
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        id = patient.Id,
                        name = patient.FullName,
                        idNumber = patient.IDNumber,
                        username = patient.Username
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜尋病患失敗");
                return Json(new { success = false, message = "系統錯誤" });
            }
        }

        public class SearchRequest
        {
            public string idNumber { get; set; }
        }




        // ========================================
        // 🗑️ 刪除紀錄
        // ========================================
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await DeleteRecordAsync(id);
                TempData["SuccessMessage"] = "紀錄已成功刪除!";
                return RedirectToAction("ViewAllRecords");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除紀錄失敗");
                TempData["ErrorMessage"] = "刪除紀錄失敗，請稍後再試";
                return RedirectToAction("ViewAllRecords");
            }
        }


        // ========================================
        // 📋 查看個案目標值是否達標（ViewGoals）
        // ========================================

        public IActionResult ViewGoals()
        {
            return View();
        }




        // ========================================
        // 📋 紀錄管理區塊（ViewAll / Details / Edit）
        // ========================================

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ViewAllRecords(string searchIdNumber = null)
        {
            List<CaseManagementViewModel> records;

            if (!string.IsNullOrEmpty(searchIdNumber))
            {
                // 搜尋特定病患的紀錄
                records = await GetRecordsByIdNumberAsync(searchIdNumber);
                ViewBag.SearchIdNumber = searchIdNumber;
            }
            else
            {
                // 顯示所有紀錄
                records = await GetAllRecordsAsync();
            }

            return View(records);
        }



        public async Task<IActionResult> Details(int id)
        {
            var record = await GetRecordByIdAsync(id);
            if (record == null) return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (!User.IsInRole("Admin") && record.UserId != userId)
                return Forbid();

            return View(record);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var record = await GetRecordByIdAsync(id);
            if (record == null) return NotFound();
            return View(record);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CaseManagementViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                await UpdateRecordAsync(model);
                TempData["SuccessMessage"] = "紀錄更新成功!";
                return RedirectToAction("ViewAllRecords");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新紀錄失敗");
                ModelState.AddModelError("", "更新紀錄失敗，請稍後再試");
                return View(model);
            }
        }

        // ========================================
        // 🧠 資料庫操作區
        // ========================================
        private async Task<UserDBModel> GetPatientByIdNumberAsync(string idNumber)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var query = @"
                SELECT ""Id"", ""Username"", ""FullName"", ""IDNumber"", ""Role""
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
                    Username = reader.GetString(1),
                    FullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IDNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Role = reader.GetString(4)
                };
            }

            return null;
        }

        // 取得病患的所有紀錄
        private async Task<List<CaseManagementViewModel>> GetUserRecordsAsync(int userId)
        {
            var records = new List<CaseManagementViewModel>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"", 
                       ""AssessmentDate"", ""FollowUpDate""
                FROM ""CaseManagement""
                WHERE ""UserId"" = @UserId
                ORDER BY ""AssessmentDate"" DESC";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                records.Add(new CaseManagementViewModel
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    IDNumber = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    BirthDate = reader.GetDateTime(5),
                    AssessmentDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    FollowUpDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                });
            }

            return records;
        }


        // 根據身分證查詢紀錄
        private async Task<List<CaseManagementViewModel>> GetRecordsByIdNumberAsync(string idNumber)
        {
            var records = new List<CaseManagementViewModel>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"", 
                       ""AssessmentDate"", ""FollowUpDate""
                FROM ""CaseManagement""
                WHERE ""IDNumber"" = @IDNumber
                ORDER BY ""AssessmentDate"" DESC";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@IDNumber", idNumber);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                records.Add(new CaseManagementViewModel
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    IDNumber = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    BirthDate = reader.GetDateTime(5),
                    AssessmentDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    FollowUpDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                });
            }

            return records;
        }


        // 取得所有紀錄
        private async Task<List<CaseManagementViewModel>> GetAllRecordsAsync()
        {
            var records = new List<CaseManagementViewModel>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
        SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"",
               ""Height"", ""Weight"", ""BMI"", ""BMI_Value"",
               ""AssessmentDate"", ""FollowUpDate"",
               ""AnnualAssessment"", ""AnnualAssessment_Date"",
               ""SystolicBP"", ""SystolicBP_Value"", ""DiastolicBP"", ""DiastolicBP_Value"",
               ""BloodPressureGuidance722"",
               ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"",
               ""HDL"", ""HDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
               ""ExerciseNone"", ""ExerciseUsually"", ""ExerciseAlways"",
               ""SmokingNone"", ""SmokingUsually"", ""SmokingUnder10"", ""SmokingOver10"",
               ""BetelNutNone"", ""BetelNutUsually"", ""BetelNutAlways"",
               ""CoronaryHigh"", ""CoronaryMedium"", ""CoronaryLow"", ""CoronaryNotApplicable"",
               ""DiabetesHigh"", ""DiabetesMedium"", ""DiabetesLow"", ""DiabetesNotApplicabe"",
               ""HypertensionHigh"", ""HypertensionMedium"", ""HypertensionLow"", ""HypertensionNotApplicable"",
               ""StrokeHigh"", ""StrokeMedium"", ""StrokeLow"", ""StrokeNotApplicable"",
               ""CardiovascularHigh"", ""CardiovascularMedium"", ""CardiovascularLow"", ""CardiovascularNotApplicable"",
               ""SmokingService"", ""SmokingServiceType1"", ""SmokingServiceType2"",
               ""SmokingServiceType2_Provide"", ""SmokingServiceType2_Referral"",
               ""BetelNutService"", ""BetelQuitGoal"", ""BetelQuitYear"", ""BetelQuitMonth"", ""BetelQuitDay"",
               ""OralExam"", ""OralExamYear"", ""OralExamMonth"",
               ""DietManagement"", ""DailyCalories1200"", ""DailyCalories1500"", ""DailyCalories1800"",
               ""DailyCalories2000"", ""DailyCaloriesOther"", ""DailyCaloriesOtherValue"",
               ""ReduceFriedFood"", ""ReduceSweetFood"", ""ReduceSalt"", ""ReduceSugaryDrinks"",
               ""ReduceOther"", ""ReduceOtherValue"",
               ""ExerciseRecommendation"", ""ExerciseGuidance"", ""SocialExerciseResources"",
               ""SocialExerciseResources_Text"",
               ""Achievement"", ""WaistTarget_Value"", ""WeightTarget_Value"",
               ""OtherReminders"", ""FastingGlucoseTarget"", ""FastingGlucoseTarget_Value"",
               ""HbA1cTarget"", ""HbA1cTarget_Value"", ""TriglyceridesTarget"", ""TriglyceridesTarget_Value"",
               ""HDL_CholesterolTarget"", ""HDL_CholesterolTarget_Value"",
               ""LDL_CholesterolTarget"", ""LDL_CholesterolTarget_Value"",
               ""Notes""
        FROM ""CaseManagement""
                ORDER BY ""AssessmentDate"" DESC
                LIMIT 100";

            await using var command = new NpgsqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                records.Add(new CaseManagementViewModel
                {
                    // 基本資料 (索引 0-11)
                    Id = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    IDNumber = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    BirthDate = reader.GetDateTime(5),
                    Height = reader.GetDecimal(6),
                    Weight = reader.GetDecimal(7),
                    BMI = reader.GetBoolean(8),
                    BMI_Value = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    AssessmentDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    FollowUpDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),

                    // 年度評估 (索引 12-13)
                    AnnualAssessment = reader.GetBoolean(12),
                    AnnualAssessment_Date = reader.IsDBNull(13) ? null : reader.GetDateTime(13),

                    // 血壓 (索引 14-18)
                    SystolicBP = reader.GetBoolean(14),
                    SystolicBP_Value = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                    DiastolicBP = reader.GetBoolean(16),
                    DiastolicBP_Value = reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                    BloodPressureGuidance722 = reader.GetBoolean(18),

                    // 腰圍/血糖/脂質 (索引 19-26)
                    CurrentWaist = reader.GetBoolean(19),
                    CurrentWaist_Value = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                    FastingGlucose = reader.GetBoolean(21),
                    FastingGlucose_Value = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                    HDL = reader.GetBoolean(23),
                    HDL_Value = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                    Triglycerides = reader.GetBoolean(25),
                    Triglycerides_Value = reader.IsDBNull(26) ? null : reader.GetDecimal(26),

                    // 生活型態 - 運動 (索引 27-29)
                    ExerciseNone = reader.GetBoolean(27),
                    ExerciseUsually = reader.GetBoolean(28),
                    ExerciseAlways = reader.GetBoolean(29),

                    // 生活型態 - 抽菸 (索引 30-33)
                    SmokingNone = reader.GetBoolean(30),
                    SmokingUsually = reader.GetBoolean(31),
                    SmokingUnder10 = reader.GetBoolean(32),
                    SmokingOver10 = reader.GetBoolean(33),

                    // 生活型態 - 檳榔 (索引 34-36)
                    BetelNutNone = reader.GetBoolean(34),
                    BetelNutUsually = reader.GetBoolean(35),
                    BetelNutAlways = reader.GetBoolean(36),

                    // 疾病風險評估 - 冠心病 (索引 37-40)
                    CoronaryHigh = reader.GetBoolean(37),
                    CoronaryMedium = reader.GetBoolean(38),
                    CoronaryLow = reader.GetBoolean(39),
                    CoronaryNotApplicable = reader.GetBoolean(40),

                    // 疾病風險評估 - 糖尿病 (索引 41-44)
                    DiabetesHigh = reader.GetBoolean(41),
                    DiabetesMedium = reader.GetBoolean(42),
                    DiabetesLow = reader.GetBoolean(43),
                    DiabetesNotApplicabe = reader.GetBoolean(44),

                    // 疾病風險評估 - 高血壓 (索引 45-48)
                    HypertensionHigh = reader.GetBoolean(45),
                    HypertensionMedium = reader.GetBoolean(46),
                    HypertensionLow = reader.GetBoolean(47),
                    HypertensionNotApplicable = reader.GetBoolean(48),

                    // 疾病風險評估 - 腦中風 (索引 49-52)
                    StrokeHigh = reader.GetBoolean(49),
                    StrokeMedium = reader.GetBoolean(50),
                    StrokeLow = reader.GetBoolean(51),
                    StrokeNotApplicable = reader.GetBoolean(52),

                    // 疾病風險評估 - 心血管 (索引 53-56)
                    CardiovascularHigh = reader.GetBoolean(53),
                    CardiovascularMedium = reader.GetBoolean(54),
                    CardiovascularLow = reader.GetBoolean(55),
                    CardiovascularNotApplicable = reader.GetBoolean(56),

                    // 戒菸服務 (索引 57-61)
                    SmokingService = reader.GetBoolean(57),
                    SmokingServiceType1 = reader.GetBoolean(58),
                    SmokingServiceType2 = reader.GetBoolean(59),
                    SmokingServiceType2_Provide = reader.GetBoolean(60),
                    SmokingServiceType2_Referral = reader.GetBoolean(61),

                    // 戒檳服務 (索引 62-66)
                    BetelNutService = reader.GetBoolean(62),
                    BetelQuitGoal = reader.GetBoolean(63),
                    BetelQuitYear = reader.IsDBNull(64) ? null : reader.GetInt32(64),
                    BetelQuitMonth = reader.IsDBNull(65) ? null : reader.GetInt32(65),
                    BetelQuitDay = reader.IsDBNull(66) ? null : reader.GetInt32(66),

                    // 口腔檢查 (索引 67-69)
                    OralExam = reader.GetBoolean(67),
                    OralExamYear = reader.IsDBNull(68) ? null : reader.GetInt32(68),
                    OralExamMonth = reader.IsDBNull(69) ? null : reader.GetInt32(69),

                    // 飲食管理 - 每日建議攝取熱量 (索引 70-76)
                    DietManagement = reader.GetBoolean(70),
                    DailyCalories1200 = reader.GetBoolean(71),
                    DailyCalories1500 = reader.GetBoolean(72),
                    DailyCalories1800 = reader.GetBoolean(73),
                    DailyCalories2000 = reader.GetBoolean(74),
                    DailyCaloriesOther = reader.GetBoolean(75),
                    DailyCaloriesOtherValue = reader.IsDBNull(76) ? null : reader.GetString(76),

                    // 飲食管理 - 盡量減少 (索引 77-82)
                    ReduceFriedFood = reader.GetBoolean(77),
                    ReduceSweetFood = reader.GetBoolean(78),
                    ReduceSalt = reader.GetBoolean(79),
                    ReduceSugaryDrinks = reader.GetBoolean(80),
                    ReduceOther = reader.GetBoolean(81),
                    ReduceOtherValue = reader.IsDBNull(82) ? null : reader.GetString(82),

                    // 運動建議與資源 (索引 83-86)
                    ExerciseRecommendation = reader.GetBoolean(83),
                    ExerciseGuidance = reader.GetBoolean(84),
                    SocialExerciseResources = reader.GetBoolean(85),
                    SocialExerciseResources_Text = reader.IsDBNull(86) ? null : reader.GetString(86),

                    // 目標設定 (索引 87-89)
                    Achievement = reader.GetBoolean(87),
                    WaistTarget_Value = reader.IsDBNull(88) ? null : reader.GetDecimal(88),
                    WeightTarget_Value = reader.IsDBNull(89) ? null : reader.GetDecimal(89),

                    // 其他叮嚀/目標值 (索引 90-100)
                    OtherReminders = reader.GetBoolean(90),
                    FastingGlucoseTarget = reader.GetBoolean(91),
                    FastingGlucoseTarget_Value = reader.IsDBNull(92) ? null : reader.GetDecimal(92),
                    HbA1cTarget = reader.GetBoolean(93),
                    HbA1cTarget_Value = reader.IsDBNull(94) ? null : reader.GetDecimal(94),
                    TriglyceridesTarget = reader.GetBoolean(95),
                    TriglyceridesTarget_Value = reader.IsDBNull(96) ? null : reader.GetDecimal(96),
                    HDL_CholesterolTarget = reader.GetBoolean(97),
                    HDL_CholesterolTarget_Value = reader.IsDBNull(98) ? null : reader.GetDecimal(98),
                    LDL_CholesterolTarget = reader.GetBoolean(99),
                    LDL_CholesterolTarget_Value = reader.IsDBNull(100) ? null : reader.GetDecimal(100),

                    // 備註 (索引 101)
                    Notes = reader.IsDBNull(101) ? null : reader.GetString(101)
                });
            }

            return records;
        }


        // 根據 ID 取得紀錄 (完整版 - 包含所有欄位)
        private async Task<CaseManagementViewModel> GetRecordByIdAsync(int id)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
        SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"",
               ""Height"", ""Weight"", ""BMI"", ""BMI_Value"",
               ""AssessmentDate"", ""FollowUpDate"",
               ""AnnualAssessment"", ""AnnualAssessment_Date"",
               ""SystolicBP"", ""SystolicBP_Value"", ""DiastolicBP"", ""DiastolicBP_Value"",
               ""BloodPressureGuidance722"",
               ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"",
               ""HDL"", ""HDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
               ""ExerciseNone"", ""ExerciseUsually"", ""ExerciseAlways"",
               ""SmokingNone"", ""SmokingUsually"", ""SmokingUnder10"", ""SmokingOver10"",
               ""BetelNutNone"", ""BetelNutUsually"", ""BetelNutAlways"",
               ""CoronaryHigh"", ""CoronaryMedium"", ""CoronaryLow"", ""CoronaryNotApplicable"",
               ""DiabetesHigh"", ""DiabetesMedium"", ""DiabetesLow"", ""DiabetesNotApplicabe"",
               ""HypertensionHigh"", ""HypertensionMedium"", ""HypertensionLow"", ""HypertensionNotApplicable"",
               ""StrokeHigh"", ""StrokeMedium"", ""StrokeLow"", ""StrokeNotApplicable"",
               ""CardiovascularHigh"", ""CardiovascularMedium"", ""CardiovascularLow"", ""CardiovascularNotApplicable"",
               ""SmokingService"", ""SmokingServiceType1"", ""SmokingServiceType2"",
               ""SmokingServiceType2_Provide"", ""SmokingServiceType2_Referral"",
               ""BetelNutService"", ""BetelQuitGoal"", ""BetelQuitYear"", ""BetelQuitMonth"", ""BetelQuitDay"",
               ""OralExam"", ""OralExamYear"", ""OralExamMonth"",
               ""DietManagement"", ""DailyCalories1200"", ""DailyCalories1500"", ""DailyCalories1800"",
               ""DailyCalories2000"", ""DailyCaloriesOther"", ""DailyCaloriesOtherValue"",
               ""ReduceFriedFood"", ""ReduceSweetFood"", ""ReduceSalt"", ""ReduceSugaryDrinks"",
               ""ReduceOther"", ""ReduceOtherValue"",
               ""ExerciseRecommendation"", ""ExerciseGuidance"", ""SocialExerciseResources"",
               ""SocialExerciseResources_Text"",
               ""Achievement"", ""WaistTarget_Value"", ""WeightTarget_Value"",
               ""OtherReminders"", ""FastingGlucoseTarget"", ""FastingGlucoseTarget_Value"",
               ""HbA1cTarget"", ""HbA1cTarget_Value"", ""TriglyceridesTarget"", ""TriglyceridesTarget_Value"",
               ""HDL_CholesterolTarget"", ""HDL_CholesterolTarget_Value"",
               ""LDL_CholesterolTarget"", ""LDL_CholesterolTarget_Value"",
               ""Notes""
        FROM ""CaseManagement""
        WHERE ""Id"" = @Id
        LIMIT 1";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new CaseManagementViewModel
                {
                    // 基本資料 (索引 0-11)
                    Id = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    IDNumber = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    BirthDate = reader.GetDateTime(5),
                    Height = reader.GetDecimal(6),
                    Weight = reader.GetDecimal(7),
                    BMI = reader.GetBoolean(8),
                    BMI_Value = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    AssessmentDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    FollowUpDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),

                    // 年度評估 (索引 12-13)
                    AnnualAssessment = reader.GetBoolean(12),
                    AnnualAssessment_Date = reader.IsDBNull(13) ? null : reader.GetDateTime(13),

                    // 血壓 (索引 14-18)
                    SystolicBP = reader.GetBoolean(14),
                    SystolicBP_Value = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                    DiastolicBP = reader.GetBoolean(16),
                    DiastolicBP_Value = reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                    BloodPressureGuidance722 = reader.GetBoolean(18),

                    // 腰圍/血糖/脂質 (索引 19-26)
                    CurrentWaist = reader.GetBoolean(19),
                    CurrentWaist_Value = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                    FastingGlucose = reader.GetBoolean(21),
                    FastingGlucose_Value = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                    HDL = reader.GetBoolean(23),
                    HDL_Value = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                    Triglycerides = reader.GetBoolean(25),
                    Triglycerides_Value = reader.IsDBNull(26) ? null : reader.GetDecimal(26),

                    // 生活型態 - 運動 (索引 27-29)
                    ExerciseNone = reader.GetBoolean(27),
                    ExerciseUsually = reader.GetBoolean(28),
                    ExerciseAlways = reader.GetBoolean(29),

                    // 生活型態 - 抽菸 (索引 30-33)
                    SmokingNone = reader.GetBoolean(30),
                    SmokingUsually = reader.GetBoolean(31),
                    SmokingUnder10 = reader.GetBoolean(32),
                    SmokingOver10 = reader.GetBoolean(33),

                    // 生活型態 - 檳榔 (索引 34-36)
                    BetelNutNone = reader.GetBoolean(34),
                    BetelNutUsually = reader.GetBoolean(35),
                    BetelNutAlways = reader.GetBoolean(36),

                    // 疾病風險評估 - 冠心病 (索引 37-40)
                    CoronaryHigh = reader.GetBoolean(37),
                    CoronaryMedium = reader.GetBoolean(38),
                    CoronaryLow = reader.GetBoolean(39),
                    CoronaryNotApplicable = reader.GetBoolean(40),

                    // 疾病風險評估 - 糖尿病 (索引 41-44)
                    DiabetesHigh = reader.GetBoolean(41),
                    DiabetesMedium = reader.GetBoolean(42),
                    DiabetesLow = reader.GetBoolean(43),
                    DiabetesNotApplicabe = reader.GetBoolean(44),

                    // 疾病風險評估 - 高血壓 (索引 45-48)
                    HypertensionHigh = reader.GetBoolean(45),
                    HypertensionMedium = reader.GetBoolean(46),
                    HypertensionLow = reader.GetBoolean(47),
                    HypertensionNotApplicable = reader.GetBoolean(48),

                    // 疾病風險評估 - 腦中風 (索引 49-52)
                    StrokeHigh = reader.GetBoolean(49),
                    StrokeMedium = reader.GetBoolean(50),
                    StrokeLow = reader.GetBoolean(51),
                    StrokeNotApplicable = reader.GetBoolean(52),

                    // 疾病風險評估 - 心血管 (索引 53-56)
                    CardiovascularHigh = reader.GetBoolean(53),
                    CardiovascularMedium = reader.GetBoolean(54),
                    CardiovascularLow = reader.GetBoolean(55),
                    CardiovascularNotApplicable = reader.GetBoolean(56),

                    // 戒菸服務 (索引 57-61)
                    SmokingService = reader.GetBoolean(57),
                    SmokingServiceType1 = reader.GetBoolean(58),
                    SmokingServiceType2 = reader.GetBoolean(59),
                    SmokingServiceType2_Provide = reader.GetBoolean(60),
                    SmokingServiceType2_Referral = reader.GetBoolean(61),

                    // 戒檳服務 (索引 62-66)
                    BetelNutService = reader.GetBoolean(62),
                    BetelQuitGoal = reader.GetBoolean(63),
                    BetelQuitYear = reader.IsDBNull(64) ? null : reader.GetInt32(64),
                    BetelQuitMonth = reader.IsDBNull(65) ? null : reader.GetInt32(65),
                    BetelQuitDay = reader.IsDBNull(66) ? null : reader.GetInt32(66),

                    // 口腔檢查 (索引 67-69)
                    OralExam = reader.GetBoolean(67),
                    OralExamYear = reader.IsDBNull(68) ? null : reader.GetInt32(68),
                    OralExamMonth = reader.IsDBNull(69) ? null : reader.GetInt32(69),

                    // 飲食管理 - 每日建議攝取熱量 (索引 70-76)
                    DietManagement = reader.GetBoolean(70),
                    DailyCalories1200 = reader.GetBoolean(71),
                    DailyCalories1500 = reader.GetBoolean(72),
                    DailyCalories1800 = reader.GetBoolean(73),
                    DailyCalories2000 = reader.GetBoolean(74),
                    DailyCaloriesOther = reader.GetBoolean(75),
                    DailyCaloriesOtherValue = reader.IsDBNull(76) ? null : reader.GetString(76),

                    // 飲食管理 - 盡量減少 (索引 77-82)
                    ReduceFriedFood = reader.GetBoolean(77),
                    ReduceSweetFood = reader.GetBoolean(78),
                    ReduceSalt = reader.GetBoolean(79),
                    ReduceSugaryDrinks = reader.GetBoolean(80),
                    ReduceOther = reader.GetBoolean(81),
                    ReduceOtherValue = reader.IsDBNull(82) ? null : reader.GetString(82),

                    // 運動建議與資源 (索引 83-86)
                    ExerciseRecommendation = reader.GetBoolean(83),
                    ExerciseGuidance = reader.GetBoolean(84),
                    SocialExerciseResources = reader.GetBoolean(85),
                    SocialExerciseResources_Text = reader.IsDBNull(86) ? null : reader.GetString(86),

                    // 目標設定 (索引 87-89)
                    Achievement = reader.GetBoolean(87),
                    WaistTarget_Value = reader.IsDBNull(88) ? null : reader.GetDecimal(88),
                    WeightTarget_Value = reader.IsDBNull(89) ? null : reader.GetDecimal(89),

                    // 其他叮嚀/目標值 (索引 90-100)
                    OtherReminders = reader.GetBoolean(90),
                    FastingGlucoseTarget = reader.GetBoolean(91),
                    FastingGlucoseTarget_Value = reader.IsDBNull(92) ? null : reader.GetDecimal(92),
                    HbA1cTarget = reader.GetBoolean(93),
                    HbA1cTarget_Value = reader.IsDBNull(94) ? null : reader.GetDecimal(94),
                    TriglyceridesTarget = reader.GetBoolean(95),
                    TriglyceridesTarget_Value = reader.IsDBNull(96) ? null : reader.GetDecimal(96),
                    HDL_CholesterolTarget = reader.GetBoolean(97),
                    HDL_CholesterolTarget_Value = reader.IsDBNull(98) ? null : reader.GetDecimal(98),
                    LDL_CholesterolTarget = reader.GetBoolean(99),
                    LDL_CholesterolTarget_Value = reader.IsDBNull(100) ? null : reader.GetDecimal(100),

                    // 備註 (索引 101)
                    Notes = reader.IsDBNull(101) ? null : reader.GetString(101)
                };
            }

            return null;
        }


        // ========================================
        // 🧠 資料庫操作區 - 新增方法
        // ========================================

        // 取得最新的紀錄 (依身分證)
        private async Task<CaseManagementViewModel> GetLatestRecordByIdNumberAsync(string idNumber)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
        SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"",
               ""Height"", ""Weight"", ""BMI"", ""BMI_Value"",
               ""AssessmentDate"", ""FollowUpDate"",
               ""AnnualAssessment"", ""AnnualAssessment_Date"",
               ""SystolicBP"", ""SystolicBP_Value"", ""DiastolicBP"", ""DiastolicBP_Value"",
               ""BloodPressureGuidance722"",
               ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"",
               ""HDL"", ""HDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
               ""ExerciseNone"", ""ExerciseUsually"", ""ExerciseAlways"",
               ""SmokingNone"", ""SmokingUsually"", ""SmokingUnder10"", ""SmokingOver10"",
               ""BetelNutNone"", ""BetelNutUsually"", ""BetelNutAlways"",
               ""CoronaryHigh"", ""CoronaryMedium"", ""CoronaryLow"", ""CoronaryNotApplicable"",
               ""DiabetesHigh"", ""DiabetesMedium"", ""DiabetesLow"", ""DiabetesNotApplicabe"",
               ""HypertensionHigh"", ""HypertensionMedium"", ""HypertensionLow"", ""HypertensionNotApplicable"",
               ""StrokeHigh"", ""StrokeMedium"", ""StrokeLow"", ""StrokeNotApplicable"",
               ""CardiovascularHigh"", ""CardiovascularMedium"", ""CardiovascularLow"", ""CardiovascularNotApplicable"",
               ""SmokingService"", ""SmokingServiceType1"", ""SmokingServiceType2"",
               ""SmokingServiceType2_Provide"", ""SmokingServiceType2_Referral"",
               ""BetelNutService"", ""BetelQuitGoal"", ""BetelQuitYear"", ""BetelQuitMonth"", ""BetelQuitDay"",
               ""OralExam"", ""OralExamYear"", ""OralExamMonth"",
               ""DietManagement"", ""DailyCalories1200"", ""DailyCalories1500"", ""DailyCalories1800"",
               ""DailyCalories2000"", ""DailyCaloriesOther"", ""DailyCaloriesOtherValue"",
               ""ReduceFriedFood"", ""ReduceSweetFood"", ""ReduceSalt"", ""ReduceSugaryDrinks"",
               ""ReduceOther"", ""ReduceOtherValue"",
               ""ExerciseRecommendation"", ""ExerciseGuidance"", ""SocialExerciseResources"",
               ""SocialExerciseResources_Text"",
               ""Achievement"", ""WaistTarget_Value"", ""WeightTarget_Value"",
               ""OtherReminders"", ""FastingGlucoseTarget"", ""FastingGlucoseTarget_Value"",
               ""HbA1cTarget"", ""HbA1cTarget_Value"", ""TriglyceridesTarget"", ""TriglyceridesTarget_Value"",
               ""HDL_CholesterolTarget"", ""HDL_CholesterolTarget_Value"",
               ""LDL_CholesterolTarget"", ""LDL_CholesterolTarget_Value"",
               ""Notes""
        FROM ""CaseManagement""
        WHERE ""IDNumber"" = @IDNumber
        ORDER BY ""Id"" DESC
        LIMIT 1";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@IDNumber", idNumber);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new CaseManagementViewModel
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    IDNumber = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    BirthDate = reader.GetDateTime(5),
                    Height = reader.GetDecimal(6),
                    Weight = reader.GetDecimal(7),
                    BMI = reader.GetBoolean(8),
                    BMI_Value = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    AssessmentDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    FollowUpDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),

                    AnnualAssessment = reader.GetBoolean(12),
                    AnnualAssessment_Date = reader.IsDBNull(13) ? null : reader.GetDateTime(13),

                    SystolicBP = reader.GetBoolean(14),
                    SystolicBP_Value = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                    DiastolicBP = reader.GetBoolean(16),
                    DiastolicBP_Value = reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                    BloodPressureGuidance722 = reader.GetBoolean(18),

                    CurrentWaist = reader.GetBoolean(19),
                    CurrentWaist_Value = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                    FastingGlucose = reader.GetBoolean(21),
                    FastingGlucose_Value = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                    HDL = reader.GetBoolean(23),
                    HDL_Value = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                    Triglycerides = reader.GetBoolean(25),
                    Triglycerides_Value = reader.IsDBNull(26) ? null : reader.GetDecimal(26),

                    ExerciseNone = reader.GetBoolean(27),
                    ExerciseUsually = reader.GetBoolean(28),
                    ExerciseAlways = reader.GetBoolean(29),

                    SmokingNone = reader.GetBoolean(30),
                    SmokingUsually = reader.GetBoolean(31),
                    SmokingUnder10 = reader.GetBoolean(32),
                    SmokingOver10 = reader.GetBoolean(33),

                    BetelNutNone = reader.GetBoolean(34),
                    BetelNutUsually = reader.GetBoolean(35),
                    BetelNutAlways = reader.GetBoolean(36),

                    CoronaryHigh = reader.GetBoolean(37),
                    CoronaryMedium = reader.GetBoolean(38),
                    CoronaryLow = reader.GetBoolean(39),
                    CoronaryNotApplicable = reader.GetBoolean(40),

                    DiabetesHigh = reader.GetBoolean(41),
                    DiabetesMedium = reader.GetBoolean(42),
                    DiabetesLow = reader.GetBoolean(43),
                    DiabetesNotApplicabe = reader.GetBoolean(44),

                    HypertensionHigh = reader.GetBoolean(45),
                    HypertensionMedium = reader.GetBoolean(46),
                    HypertensionLow = reader.GetBoolean(47),
                    HypertensionNotApplicable = reader.GetBoolean(48),

                    StrokeHigh = reader.GetBoolean(49),
                    StrokeMedium = reader.GetBoolean(50),
                    StrokeLow = reader.GetBoolean(51),
                    StrokeNotApplicable = reader.GetBoolean(52),

                    CardiovascularHigh = reader.GetBoolean(53),
                    CardiovascularMedium = reader.GetBoolean(54),
                    CardiovascularLow = reader.GetBoolean(55),
                    CardiovascularNotApplicable = reader.GetBoolean(56),

                    SmokingService = reader.GetBoolean(57),
                    SmokingServiceType1 = reader.GetBoolean(58),
                    SmokingServiceType2 = reader.GetBoolean(59),
                    SmokingServiceType2_Provide = reader.GetBoolean(60),
                    SmokingServiceType2_Referral = reader.GetBoolean(61),

                    BetelNutService = reader.GetBoolean(62),
                    BetelQuitGoal = reader.GetBoolean(63),
                    BetelQuitYear = reader.IsDBNull(64) ? null : reader.GetInt32(64),
                    BetelQuitMonth = reader.IsDBNull(65) ? null : reader.GetInt32(65),
                    BetelQuitDay = reader.IsDBNull(66) ? null : reader.GetInt32(66),

                    OralExam = reader.GetBoolean(67),
                    OralExamYear = reader.IsDBNull(68) ? null : reader.GetInt32(68),
                    OralExamMonth = reader.IsDBNull(69) ? null : reader.GetInt32(69),

                    DietManagement = reader.GetBoolean(70),
                    DailyCalories1200 = reader.GetBoolean(71),
                    DailyCalories1500 = reader.GetBoolean(72),
                    DailyCalories1800 = reader.GetBoolean(73),
                    DailyCalories2000 = reader.GetBoolean(74),
                    DailyCaloriesOther = reader.GetBoolean(75),
                    DailyCaloriesOtherValue = reader.IsDBNull(76) ? null : reader.GetString(76),

                    ReduceFriedFood = reader.GetBoolean(77),
                    ReduceSweetFood = reader.GetBoolean(78),
                    ReduceSalt = reader.GetBoolean(79),
                    ReduceSugaryDrinks = reader.GetBoolean(80),
                    ReduceOther = reader.GetBoolean(81),
                    ReduceOtherValue = reader.IsDBNull(82) ? null : reader.GetString(82),

                    ExerciseRecommendation = reader.GetBoolean(83),
                    ExerciseGuidance = reader.GetBoolean(84),
                    SocialExerciseResources = reader.GetBoolean(85),
                    SocialExerciseResources_Text = reader.IsDBNull(86) ? null : reader.GetString(86),

                    Achievement = reader.GetBoolean(87),
                    WaistTarget_Value = reader.IsDBNull(88) ? null : reader.GetDecimal(88),
                    WeightTarget_Value = reader.IsDBNull(89) ? null : reader.GetDecimal(89),

                    OtherReminders = reader.GetBoolean(90),
                    FastingGlucoseTarget = reader.GetBoolean(91),
                    FastingGlucoseTarget_Value = reader.IsDBNull(92) ? null : reader.GetDecimal(92),
                    HbA1cTarget = reader.GetBoolean(93),
                    HbA1cTarget_Value = reader.IsDBNull(94) ? null : reader.GetDecimal(94),
                    TriglyceridesTarget = reader.GetBoolean(95),
                    TriglyceridesTarget_Value = reader.IsDBNull(96) ? null : reader.GetDecimal(96),
                    HDL_CholesterolTarget = reader.GetBoolean(97),
                    HDL_CholesterolTarget_Value = reader.IsDBNull(98) ? null : reader.GetDecimal(98),
                    LDL_CholesterolTarget = reader.GetBoolean(99),
                    LDL_CholesterolTarget_Value = reader.IsDBNull(100) ? null : reader.GetDecimal(100),

                    Notes = reader.IsDBNull(101) ? null : reader.GetString(101)
                };
            }

            return null;
        }

        // 刪除紀錄
        private async Task DeleteRecordAsync(int id)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"DELETE FROM ""CaseManagement"" WHERE ""Id"" = @Id";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            await command.ExecuteNonQueryAsync();
        }

        // ========================================
        // 💾 儲存新紀錄 (完整版 - 支援所有欄位)
        // ========================================
        private async Task SaveRecordAsync(CaseManagementViewModel model)
        {
            try
            {
                _logger.LogInformation("開始儲存紀錄 - UserId: {UserId}, IDNumber: {IDNumber}", model.UserId, model.IDNumber);

                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                _logger.LogInformation("Connection String: {ConnectionString}", connectionString);

                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("資料庫連線成功");

                var query = @"
            INSERT INTO ""CaseManagement"" 
            (
                ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"", 
                ""Height"", ""Weight"", ""BMI"", ""BMI_Value"", 
                ""AssessmentDate"", ""FollowUpDate"",
                ""AnnualAssessment"", ""AnnualAssessment_Date"",
                ""SystolicBP"", ""SystolicBP_Value"", ""DiastolicBP"", ""DiastolicBP_Value"", ""BloodPressureGuidance722"",
                ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"",
                ""HDL"", ""HDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
                ""ExerciseNone"", ""ExerciseUsually"", ""ExerciseAlways"",
                ""SmokingNone"", ""SmokingUsually"", ""SmokingUnder10"", ""SmokingOver10"",
                ""BetelNutNone"", ""BetelNutUsually"", ""BetelNutAlways"",
                ""CoronaryHigh"", ""CoronaryMedium"", ""CoronaryLow"", ""CoronaryNotApplicable"",
                ""DiabetesHigh"", ""DiabetesMedium"", ""DiabetesLow"", ""DiabetesNotApplicabe"",
                ""HypertensionHigh"", ""HypertensionMedium"", ""HypertensionLow"", ""HypertensionNotApplicable"",
                ""StrokeHigh"", ""StrokeMedium"", ""StrokeLow"", ""StrokeNotApplicable"",
                ""CardiovascularHigh"", ""CardiovascularMedium"", ""CardiovascularLow"", ""CardiovascularNotApplicable"",
                ""SmokingService"", ""SmokingServiceType1"", ""SmokingServiceType2"", 
                ""SmokingServiceType2_Provide"", ""SmokingServiceType2_Referral"",
                ""BetelNutService"", ""BetelQuitGoal"", ""BetelQuitYear"", ""BetelQuitMonth"", ""BetelQuitDay"",
                ""OralExam"", ""OralExamYear"", ""OralExamMonth"",
                ""DietManagement"", ""DailyCalories1200"", ""DailyCalories1500"", ""DailyCalories1800"", 
                ""DailyCalories2000"", ""DailyCaloriesOther"", ""DailyCaloriesOtherValue"",
                ""ReduceFriedFood"", ""ReduceSweetFood"", ""ReduceSalt"", ""ReduceSugaryDrinks"", 
                ""ReduceOther"", ""ReduceOtherValue"",
                ""ExerciseRecommendation"", ""ExerciseGuidance"", ""SocialExerciseResources"", ""SocialExerciseResources_Text"",
                ""Achievement"", ""WaistTarget_Value"", ""WeightTarget_Value"",
                ""OtherReminders"", ""FastingGlucoseTarget"", ""FastingGlucoseTarget_Value"",
                ""HbA1cTarget"", ""HbA1cTarget_Value"", ""TriglyceridesTarget"", ""TriglyceridesTarget_Value"",
                ""HDL_CholesterolTarget"", ""HDL_CholesterolTarget_Value"", 
                ""LDL_CholesterolTarget"", ""LDL_CholesterolTarget_Value"",
                ""Notes""
            )
            VALUES 
            (
                @UserId, @IDNumber, @Name, @Gender, @BirthDate,
                @Height, @Weight, @BMI, @BMI_Value,
                @AssessmentDate, @FollowUpDate,
                @AnnualAssessment, @AnnualAssessment_Date,
                @SystolicBP, @SystolicBP_Value, @DiastolicBP, @DiastolicBP_Value, @BloodPressureGuidance722,
                @CurrentWaist, @CurrentWaist_Value, @FastingGlucose, @FastingGlucose_Value,
                @HDL, @HDL_Value, @Triglycerides, @Triglycerides_Value,
                @ExerciseNone, @ExerciseUsually, @ExerciseAlways,
                @SmokingNone, @SmokingUsually, @SmokingUnder10, @SmokingOver10,
                @BetelNutNone, @BetelNutUsually, @BetelNutAlways,
                @CoronaryHigh, @CoronaryMedium, @CoronaryLow, @CoronaryNotApplicable,
                @DiabetesHigh, @DiabetesMedium, @DiabetesLow, @DiabetesNotApplicabe,
                @HypertensionHigh, @HypertensionMedium, @HypertensionLow, @HypertensionNotApplicable,
                @StrokeHigh, @StrokeMedium, @StrokeLow, @StrokeNotApplicable,
                @CardiovascularHigh, @CardiovascularMedium, @CardiovascularLow, @CardiovascularNotApplicable,
                @SmokingService, @SmokingServiceType1, @SmokingServiceType2,
                @SmokingServiceType2_Provide, @SmokingServiceType2_Referral,
                @BetelNutService, @BetelQuitGoal, @BetelQuitYear, @BetelQuitMonth, @BetelQuitDay,
                @OralExam, @OralExamYear, @OralExamMonth,
                @DietManagement, @DailyCalories1200, @DailyCalories1500, @DailyCalories1800,
                @DailyCalories2000, @DailyCaloriesOther, @DailyCaloriesOtherValue,
                @ReduceFriedFood, @ReduceSweetFood, @ReduceSalt, @ReduceSugaryDrinks,
                @ReduceOther, @ReduceOtherValue,
                @ExerciseRecommendation, @ExerciseGuidance, @SocialExerciseResources, @SocialExerciseResources_Text,
                @Achievement, @WaistTarget_Value, @WeightTarget_Value,
                @OtherReminders, @FastingGlucoseTarget, @FastingGlucoseTarget_Value,
                @HbA1cTarget, @HbA1cTarget_Value, @TriglyceridesTarget, @TriglyceridesTarget_Value,
                @HDL_CholesterolTarget, @HDL_CholesterolTarget_Value,
                @LDL_CholesterolTarget, @LDL_CholesterolTarget_Value,
                @Notes
            )";

                await using var command = new NpgsqlCommand(query, connection);

                // 基本資料
                command.Parameters.AddWithValue("@UserId", model.UserId);
                command.Parameters.AddWithValue("@IDNumber", model.IDNumber);
                command.Parameters.AddWithValue("@Name", model.Name);
                command.Parameters.AddWithValue("@Gender", model.Gender);
                command.Parameters.AddWithValue("@BirthDate", model.BirthDate);
                command.Parameters.AddWithValue("@Height", model.Height);
                command.Parameters.AddWithValue("@Weight", model.Weight);
                command.Parameters.AddWithValue("@BMI", model.BMI);
                command.Parameters.AddWithValue("@BMI_Value", model.BMI_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@AssessmentDate", model.AssessmentDate ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@FollowUpDate", model.FollowUpDate ?? (object)DBNull.Value);

                // 年度評估
                command.Parameters.AddWithValue("@AnnualAssessment", model.AnnualAssessment);
                command.Parameters.AddWithValue("@AnnualAssessment_Date", model.AnnualAssessment_Date ?? (object)DBNull.Value);

                // 血壓
                command.Parameters.AddWithValue("@SystolicBP", model.SystolicBP);
                command.Parameters.AddWithValue("@SystolicBP_Value", model.SystolicBP_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@DiastolicBP", model.DiastolicBP);
                command.Parameters.AddWithValue("@DiastolicBP_Value", model.DiastolicBP_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@BloodPressureGuidance722", model.BloodPressureGuidance722);

                // 腰圍/血糖/脂質
                command.Parameters.AddWithValue("@CurrentWaist", model.CurrentWaist);
                command.Parameters.AddWithValue("@CurrentWaist_Value", model.CurrentWaist_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@FastingGlucose", model.FastingGlucose);
                command.Parameters.AddWithValue("@FastingGlucose_Value", model.FastingGlucose_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@HDL", model.HDL);
                command.Parameters.AddWithValue("@HDL_Value", model.HDL_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Triglycerides", model.Triglycerides);
                command.Parameters.AddWithValue("@Triglycerides_Value", model.Triglycerides_Value ?? (object)DBNull.Value);

                // 生活型態 - 運動
                command.Parameters.AddWithValue("@ExerciseNone", model.ExerciseNone);
                command.Parameters.AddWithValue("@ExerciseUsually", model.ExerciseUsually);
                command.Parameters.AddWithValue("@ExerciseAlways", model.ExerciseAlways);

                // 生活型態 - 抽菸
                command.Parameters.AddWithValue("@SmokingNone", model.SmokingNone);
                command.Parameters.AddWithValue("@SmokingUsually", model.SmokingUsually);
                command.Parameters.AddWithValue("@SmokingUnder10", model.SmokingUnder10);
                command.Parameters.AddWithValue("@SmokingOver10", model.SmokingOver10);

                // 生活型態 - 檳榔
                command.Parameters.AddWithValue("@BetelNutNone", model.BetelNutNone);
                command.Parameters.AddWithValue("@BetelNutUsually", model.BetelNutUsually);
                command.Parameters.AddWithValue("@BetelNutAlways", model.BetelNutAlways);

                // 疾病風險評估 - 冠心病
                command.Parameters.AddWithValue("@CoronaryHigh", model.CoronaryHigh);
                command.Parameters.AddWithValue("@CoronaryMedium", model.CoronaryMedium);
                command.Parameters.AddWithValue("@CoronaryLow", model.CoronaryLow);
                command.Parameters.AddWithValue("@CoronaryNotApplicable", model.CoronaryNotApplicable);

                // 疾病風險評估 - 糖尿病
                command.Parameters.AddWithValue("@DiabetesHigh", model.DiabetesHigh);
                command.Parameters.AddWithValue("@DiabetesMedium", model.DiabetesMedium);
                command.Parameters.AddWithValue("@DiabetesLow", model.DiabetesLow);
                command.Parameters.AddWithValue("@DiabetesNotApplicabe", model.DiabetesNotApplicabe);

                // 疾病風險評估 - 高血壓
                command.Parameters.AddWithValue("@HypertensionHigh", model.HypertensionHigh);
                command.Parameters.AddWithValue("@HypertensionMedium", model.HypertensionMedium);
                command.Parameters.AddWithValue("@HypertensionLow", model.HypertensionLow);
                command.Parameters.AddWithValue("@HypertensionNotApplicable", model.HypertensionNotApplicable);

                // 疾病風險評估 - 腦中風
                command.Parameters.AddWithValue("@StrokeHigh", model.StrokeHigh);
                command.Parameters.AddWithValue("@StrokeMedium", model.StrokeMedium);
                command.Parameters.AddWithValue("@StrokeLow", model.StrokeLow);
                command.Parameters.AddWithValue("@StrokeNotApplicable", model.StrokeNotApplicable);

                // 疾病風險評估 - 心血管
                command.Parameters.AddWithValue("@CardiovascularHigh", model.CardiovascularHigh);
                command.Parameters.AddWithValue("@CardiovascularMedium", model.CardiovascularMedium);
                command.Parameters.AddWithValue("@CardiovascularLow", model.CardiovascularLow);
                command.Parameters.AddWithValue("@CardiovascularNotApplicable", model.CardiovascularNotApplicable);

                // 戒菸服務
                command.Parameters.AddWithValue("@SmokingService", model.SmokingService);
                command.Parameters.AddWithValue("@SmokingServiceType1", model.SmokingServiceType1);
                command.Parameters.AddWithValue("@SmokingServiceType2", model.SmokingServiceType2);
                command.Parameters.AddWithValue("@SmokingServiceType2_Provide", model.SmokingServiceType2_Provide);
                command.Parameters.AddWithValue("@SmokingServiceType2_Referral", model.SmokingServiceType2_Referral);

                // 戒檳服務
                command.Parameters.AddWithValue("@BetelNutService", model.BetelNutService);
                command.Parameters.AddWithValue("@BetelQuitGoal", model.BetelQuitGoal);
                command.Parameters.AddWithValue("@BetelQuitYear", model.BetelQuitYear ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@BetelQuitMonth", model.BetelQuitMonth ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@BetelQuitDay", model.BetelQuitDay ?? (object)DBNull.Value);

                // 口腔檢查
                command.Parameters.AddWithValue("@OralExam", model.OralExam);
                command.Parameters.AddWithValue("@OralExamYear", model.OralExamYear ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@OralExamMonth", model.OralExamMonth ?? (object)DBNull.Value);

                // 飲食管理 - 每日建議攝取熱量
                command.Parameters.AddWithValue("@DietManagement", model.DietManagement);
                command.Parameters.AddWithValue("@DailyCalories1200", model.DailyCalories1200);
                command.Parameters.AddWithValue("@DailyCalories1500", model.DailyCalories1500);
                command.Parameters.AddWithValue("@DailyCalories1800", model.DailyCalories1800);
                command.Parameters.AddWithValue("@DailyCalories2000", model.DailyCalories2000);
                command.Parameters.AddWithValue("@DailyCaloriesOther", model.DailyCaloriesOther);
                command.Parameters.AddWithValue("@DailyCaloriesOtherValue", model.DailyCaloriesOtherValue ?? (object)DBNull.Value);

                // 飲食管理 - 盡量減少
                command.Parameters.AddWithValue("@ReduceFriedFood", model.ReduceFriedFood);
                command.Parameters.AddWithValue("@ReduceSweetFood", model.ReduceSweetFood);
                command.Parameters.AddWithValue("@ReduceSalt", model.ReduceSalt);
                command.Parameters.AddWithValue("@ReduceSugaryDrinks", model.ReduceSugaryDrinks);
                command.Parameters.AddWithValue("@ReduceOther", model.ReduceOther);
                command.Parameters.AddWithValue("@ReduceOtherValue", model.ReduceOtherValue ?? (object)DBNull.Value);

                // 運動建議與資源
                command.Parameters.AddWithValue("@ExerciseRecommendation", model.ExerciseRecommendation);
                command.Parameters.AddWithValue("@ExerciseGuidance", model.ExerciseGuidance);
                command.Parameters.AddWithValue("@SocialExerciseResources", model.SocialExerciseResources);
                command.Parameters.AddWithValue("@SocialExerciseResources_Text", model.SocialExerciseResources_Text ?? (object)DBNull.Value);

                // 目標設定
                command.Parameters.AddWithValue("@Achievement", model.Achievement);
                command.Parameters.AddWithValue("@WaistTarget_Value", model.WaistTarget_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@WeightTarget_Value", model.WeightTarget_Value ?? (object)DBNull.Value);

                // 其他叮嚀/目標值
                command.Parameters.AddWithValue("@OtherReminders", model.OtherReminders);
                command.Parameters.AddWithValue("@FastingGlucoseTarget", model.FastingGlucoseTarget);
                command.Parameters.AddWithValue("@FastingGlucoseTarget_Value", model.FastingGlucoseTarget_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@HbA1cTarget", model.HbA1cTarget);
                command.Parameters.AddWithValue("@HbA1cTarget_Value", model.HbA1cTarget_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@TriglyceridesTarget", model.TriglyceridesTarget);
                command.Parameters.AddWithValue("@TriglyceridesTarget_Value", model.TriglyceridesTarget_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@HDL_CholesterolTarget", model.HDL_CholesterolTarget);
                command.Parameters.AddWithValue("@HDL_CholesterolTarget_Value", model.HDL_CholesterolTarget_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@LDL_CholesterolTarget", model.LDL_CholesterolTarget);
                command.Parameters.AddWithValue("@LDL_CholesterolTarget_Value", model.LDL_CholesterolTarget_Value ?? (object)DBNull.Value);

                // 備註
                command.Parameters.AddWithValue("@Notes", model.Notes ?? (object)DBNull.Value);

                _logger.LogInformation("準備執行 SQL 命令");
                var rowsAffected = await command.ExecuteNonQueryAsync();
                _logger.LogInformation("SQL 執行完成,影響 {RowsAffected} 行", rowsAffected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存紀錄時發生錯誤");
                throw;
            }
        }

        // ========================================
        // 🔄 更新紀錄 (完整版)
        // ========================================
        private async Task UpdateRecordAsync(CaseManagementViewModel model)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                UPDATE ""CaseManagement""
                SET 
                    ""Name"" = @Name, ""Gender"" = @Gender, ""BirthDate"" = @BirthDate,
                    ""Height"" = @Height, ""Weight"" = @Weight, ""BMI"" = @BMI, ""BMI_Value"" = @BMI_Value,
                    ""AssessmentDate"" = @AssessmentDate, ""FollowUpDate"" = @FollowUpDate,
                    ""AnnualAssessment"" = @AnnualAssessment, ""AnnualAssessment_Date"" = @AnnualAssessment_Date,
                    ""SystolicBP"" = @SystolicBP, ""SystolicBP_Value"" = @SystolicBP_Value,
                    ""DiastolicBP"" = @DiastolicBP, ""DiastolicBP_Value"" = @DiastolicBP_Value,
                    ""BloodPressureGuidance722"" = @BloodPressureGuidance722,
                    ""CurrentWaist"" = @CurrentWaist, ""CurrentWaist_Value"" = @CurrentWaist_Value,
                    ""FastingGlucose"" = @FastingGlucose, ""FastingGlucose_Value"" = @FastingGlucose_Value,
                    ""HDL"" = @HDL, ""HDL_Value"" = @HDL_Value,
                    ""Triglycerides"" = @Triglycerides, ""Triglycerides_Value"" = @Triglycerides_Value,
                    ""ExerciseNone"" = @ExerciseNone, ""ExerciseUsually"" = @ExerciseUsually, ""ExerciseAlways"" = @ExerciseAlways,
                    ""SmokingNone"" = @SmokingNone, ""SmokingUsually"" = @SmokingUsually,
                    ""SmokingUnder10"" = @SmokingUnder10, ""SmokingOver10"" = @SmokingOver10,
                    ""BetelNutNone"" = @BetelNutNone, ""BetelNutUsually"" = @BetelNutUsually, ""BetelNutAlways"" = @BetelNutAlways,
                    ""CoronaryHigh"" = @CoronaryHigh, ""CoronaryMedium"" = @CoronaryMedium,
                    ""CoronaryLow"" = @CoronaryLow, ""CoronaryNotApplicable"" = @CoronaryNotApplicable,
                    ""DiabetesHigh"" = @DiabetesHigh, ""DiabetesMedium"" = @DiabetesMedium,
                    ""DiabetesLow"" = @DiabetesLow, ""DiabetesNotApplicabe"" = @DiabetesNotApplicabe,
                    ""HypertensionHigh"" = @HypertensionHigh, ""HypertensionMedium"" = @HypertensionMedium,
                    ""HypertensionLow"" = @HypertensionLow, ""HypertensionNotApplicable"" = @HypertensionNotApplicable,
                    ""StrokeHigh"" = @StrokeHigh, ""StrokeMedium"" = @StrokeMedium,
                    ""StrokeLow"" = @StrokeLow, ""StrokeNotApplicable"" = @StrokeNotApplicable,
                    ""CardiovascularHigh"" = @CardiovascularHigh, ""CardiovascularMedium"" = @CardiovascularMedium,
                    ""CardiovascularLow"" = @CardiovascularLow, ""CardiovascularNotApplicable"" = @CardiovascularNotApplicable,
                    ""SmokingService"" = @SmokingService, ""SmokingServiceType1"" = @SmokingServiceType1,
                    ""SmokingServiceType2"" = @SmokingServiceType2,
                    ""SmokingServiceType2_Provide"" = @SmokingServiceType2_Provide,
                    ""SmokingServiceType2_Referral"" = @SmokingServiceType2_Referral,
                    ""BetelNutService"" = @BetelNutService, ""BetelQuitGoal"" = @BetelQuitGoal,
                    ""BetelQuitYear"" = @BetelQuitYear, ""BetelQuitMonth"" = @BetelQuitMonth, ""BetelQuitDay"" = @BetelQuitDay,
                    ""OralExam"" = @OralExam, ""OralExamYear"" = @OralExamYear, ""OralExamMonth"" = @OralExamMonth,
                    ""DietManagement"" = @DietManagement,
                    ""DailyCalories1200"" = @DailyCalories1200, ""DailyCalories1500"" = @DailyCalories1500,
                    ""DailyCalories1800"" = @DailyCalories1800, ""DailyCalories2000"" = @DailyCalories2000,
                    ""DailyCaloriesOther"" = @DailyCaloriesOther, ""DailyCaloriesOtherValue"" = @DailyCaloriesOtherValue,
                    ""ReduceFriedFood"" = @ReduceFriedFood, ""ReduceSweetFood"" = @ReduceSweetFood,
                    ""ReduceSalt"" = @ReduceSalt, ""ReduceSugaryDrinks"" = @ReduceSugaryDrinks,
                    ""ReduceOther"" = @ReduceOther, ""ReduceOtherValue"" = @ReduceOtherValue,
                    ""ExerciseRecommendation"" = @ExerciseRecommendation, ""ExerciseGuidance"" = @ExerciseGuidance,
                    ""SocialExerciseResources"" = @SocialExerciseResources,
                    ""SocialExerciseResources_Text"" = @SocialExerciseResources_Text,
                    ""Achievement"" = @Achievement, ""WaistTarget_Value"" = @WaistTarget_Value,
                    ""WeightTarget_Value"" = @WeightTarget_Value,
                    ""OtherReminders"" = @OtherReminders,
                    ""FastingGlucoseTarget"" = @FastingGlucoseTarget, ""FastingGlucoseTarget_Value"" = @FastingGlucoseTarget_Value,
                    ""HbA1cTarget"" = @HbA1cTarget, ""HbA1cTarget_Value"" = @HbA1cTarget_Value,
                    ""TriglyceridesTarget"" = @TriglyceridesTarget, ""TriglyceridesTarget_Value"" = @TriglyceridesTarget_Value,
                    ""HDL_CholesterolTarget"" = @HDL_CholesterolTarget, ""HDL_CholesterolTarget_Value"" = @HDL_CholesterolTarget_Value,
                    ""LDL_CholesterolTarget"" = @LDL_CholesterolTarget, ""LDL_CholesterolTarget_Value"" = @LDL_CholesterolTarget_Value,
                    ""Notes"" = @Notes
                WHERE ""Id"" = @Id";

            await using var command = new NpgsqlCommand(query, connection);

            // ID
            command.Parameters.AddWithValue("@Id", model.Id);

            // 基本資料
            command.Parameters.AddWithValue("@Name", model.Name);
            command.Parameters.AddWithValue("@Gender", model.Gender);
            command.Parameters.AddWithValue("@BirthDate", model.BirthDate);
            command.Parameters.AddWithValue("@Height", model.Height);
            command.Parameters.AddWithValue("@Weight", model.Weight);
            command.Parameters.AddWithValue("@BMI", model.BMI);
            command.Parameters.AddWithValue("@BMI_Value", model.BMI_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@AssessmentDate", model.AssessmentDate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@FollowUpDate", model.FollowUpDate ?? (object)DBNull.Value);

            // 年度評估
            command.Parameters.AddWithValue("@AnnualAssessment", model.AnnualAssessment);
            command.Parameters.AddWithValue("@AnnualAssessment_Date", model.AnnualAssessment_Date ?? (object)DBNull.Value);

            // 血壓
            command.Parameters.AddWithValue("@SystolicBP", model.SystolicBP);
            command.Parameters.AddWithValue("@SystolicBP_Value", model.SystolicBP_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@DiastolicBP", model.DiastolicBP);
            command.Parameters.AddWithValue("@DiastolicBP_Value", model.DiastolicBP_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@BloodPressureGuidance722", model.BloodPressureGuidance722);

            // 腰圍/血糖/脂質
            command.Parameters.AddWithValue("@CurrentWaist", model.CurrentWaist);
            command.Parameters.AddWithValue("@CurrentWaist_Value", model.CurrentWaist_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@FastingGlucose", model.FastingGlucose);
            command.Parameters.AddWithValue("@FastingGlucose_Value", model.FastingGlucose_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@HDL", model.HDL);
            command.Parameters.AddWithValue("@HDL_Value", model.HDL_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Triglycerides", model.Triglycerides);
            command.Parameters.AddWithValue("@Triglycerides_Value", model.Triglycerides_Value ?? (object)DBNull.Value);

            // 生活型態 - 運動
            command.Parameters.AddWithValue("@ExerciseNone", model.ExerciseNone);
            command.Parameters.AddWithValue("@ExerciseUsually", model.ExerciseUsually);
            command.Parameters.AddWithValue("@ExerciseAlways", model.ExerciseAlways);

            // 生活型態 - 抽菸
            command.Parameters.AddWithValue("@SmokingNone", model.SmokingNone);
            command.Parameters.AddWithValue("@SmokingUsually", model.SmokingUsually);
            command.Parameters.AddWithValue("@SmokingUnder10", model.SmokingUnder10);
            command.Parameters.AddWithValue("@SmokingOver10", model.SmokingOver10);

            // 生活型態 - 檳榔
            command.Parameters.AddWithValue("@BetelNutNone", model.BetelNutNone);
            command.Parameters.AddWithValue("@BetelNutUsually", model.BetelNutUsually);
            command.Parameters.AddWithValue("@BetelNutAlways", model.BetelNutAlways);

            // 疾病風險評估 - 冠心病
            command.Parameters.AddWithValue("@CoronaryHigh", model.CoronaryHigh);
            command.Parameters.AddWithValue("@CoronaryMedium", model.CoronaryMedium);
            command.Parameters.AddWithValue("@CoronaryLow", model.CoronaryLow);
            command.Parameters.AddWithValue("@CoronaryNotApplicable", model.CoronaryNotApplicable);

            // 疾病風險評估 - 糖尿病
            command.Parameters.AddWithValue("@DiabetesHigh", model.DiabetesHigh);
            command.Parameters.AddWithValue("@DiabetesMedium", model.DiabetesMedium);
            command.Parameters.AddWithValue("@DiabetesLow", model.DiabetesLow);
            command.Parameters.AddWithValue("@DiabetesNotApplicabe", model.DiabetesNotApplicabe);

            // 疾病風險評估 - 高血壓
            command.Parameters.AddWithValue("@HypertensionHigh", model.HypertensionHigh);
            command.Parameters.AddWithValue("@HypertensionMedium", model.HypertensionMedium);
            command.Parameters.AddWithValue("@HypertensionLow", model.HypertensionLow);
            command.Parameters.AddWithValue("@HypertensionNotApplicable", model.HypertensionNotApplicable);

            // 疾病風險評估 - 腦中風
            command.Parameters.AddWithValue("@StrokeHigh", model.StrokeHigh);
            command.Parameters.AddWithValue("@StrokeMedium", model.StrokeMedium);
            command.Parameters.AddWithValue("@StrokeLow", model.StrokeLow);
            command.Parameters.AddWithValue("@StrokeNotApplicable", model.StrokeNotApplicable);

            // 疾病風險評估 - 心血管
            command.Parameters.AddWithValue("@CardiovascularHigh", model.CardiovascularHigh);
            command.Parameters.AddWithValue("@CardiovascularMedium", model.CardiovascularMedium);
            command.Parameters.AddWithValue("@CardiovascularLow", model.CardiovascularLow);
            command.Parameters.AddWithValue("@CardiovascularNotApplicable", model.CardiovascularNotApplicable);

            // 戒菸服務
            command.Parameters.AddWithValue("@SmokingService", model.SmokingService);
            command.Parameters.AddWithValue("@SmokingServiceType1", model.SmokingServiceType1);
            command.Parameters.AddWithValue("@SmokingServiceType2", model.SmokingServiceType2);
            command.Parameters.AddWithValue("@SmokingServiceType2_Provide", model.SmokingServiceType2_Provide);
            command.Parameters.AddWithValue("@SmokingServiceType2_Referral", model.SmokingServiceType2_Referral);

            // 戒檳服務
            command.Parameters.AddWithValue("@BetelNutService", model.BetelNutService);
            command.Parameters.AddWithValue("@BetelQuitGoal", model.BetelQuitGoal);
            command.Parameters.AddWithValue("@BetelQuitYear", model.BetelQuitYear ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@BetelQuitMonth", model.BetelQuitMonth ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@BetelQuitDay", model.BetelQuitDay ?? (object)DBNull.Value);

            // 口腔檢查
            command.Parameters.AddWithValue("@OralExam", model.OralExam);
            command.Parameters.AddWithValue("@OralExamYear", model.OralExamYear ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@OralExamMonth", model.OralExamMonth ?? (object)DBNull.Value);

            // 飲食管理 - 每日建議攝取熱量
            command.Parameters.AddWithValue("@DietManagement", model.DietManagement);
            command.Parameters.AddWithValue("@DailyCalories1200", model.DailyCalories1200);
            command.Parameters.AddWithValue("@DailyCalories1500", model.DailyCalories1500);
            command.Parameters.AddWithValue("@DailyCalories1800", model.DailyCalories1800);
            command.Parameters.AddWithValue("@DailyCalories2000", model.DailyCalories2000);
            command.Parameters.AddWithValue("@DailyCaloriesOther", model.DailyCaloriesOther);
            command.Parameters.AddWithValue("@DailyCaloriesOtherValue", model.DailyCaloriesOtherValue ?? (object)DBNull.Value);

            // 飲食管理 - 盡量減少
            command.Parameters.AddWithValue("@ReduceFriedFood", model.ReduceFriedFood);
            command.Parameters.AddWithValue("@ReduceSweetFood", model.ReduceSweetFood);
            command.Parameters.AddWithValue("@ReduceSalt", model.ReduceSalt);
            command.Parameters.AddWithValue("@ReduceSugaryDrinks", model.ReduceSugaryDrinks);
            command.Parameters.AddWithValue("@ReduceOther", model.ReduceOther);
            command.Parameters.AddWithValue("@ReduceOtherValue", model.ReduceOtherValue ?? (object)DBNull.Value);

            // 運動建議與資源
            command.Parameters.AddWithValue("@ExerciseRecommendation", model.ExerciseRecommendation);
            command.Parameters.AddWithValue("@ExerciseGuidance", model.ExerciseGuidance);
            command.Parameters.AddWithValue("@SocialExerciseResources", model.SocialExerciseResources);
            command.Parameters.AddWithValue("@SocialExerciseResources_Text", model.SocialExerciseResources_Text ?? (object)DBNull.Value);

            // 目標設定
            command.Parameters.AddWithValue("@Achievement", model.Achievement);
            command.Parameters.AddWithValue("@WaistTarget_Value", model.WaistTarget_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@WeightTarget_Value", model.WeightTarget_Value ?? (object)DBNull.Value);

            // 其他叮嚀/目標值
            command.Parameters.AddWithValue("@OtherReminders", model.OtherReminders);
            command.Parameters.AddWithValue("@FastingGlucoseTarget", model.FastingGlucoseTarget);
            command.Parameters.AddWithValue("@FastingGlucoseTarget_Value", model.FastingGlucoseTarget_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@HbA1cTarget", model.HbA1cTarget);
            command.Parameters.AddWithValue("@HbA1cTarget_Value", model.HbA1cTarget_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@TriglyceridesTarget", model.TriglyceridesTarget);
            command.Parameters.AddWithValue("@TriglyceridesTarget_Value", model.TriglyceridesTarget_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@HDL_CholesterolTarget", model.HDL_CholesterolTarget);
            command.Parameters.AddWithValue("@HDL_CholesterolTarget_Value", model.HDL_CholesterolTarget_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@LDL_CholesterolTarget", model.LDL_CholesterolTarget);
            command.Parameters.AddWithValue("@LDL_CholesterolTarget_Value", model.LDL_CholesterolTarget_Value ?? (object)DBNull.Value);

            // 備註
            command.Parameters.AddWithValue("@Notes", model.Notes ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
    }
}