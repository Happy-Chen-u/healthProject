using healthProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System;
using System.Collections.Generic;

namespace healthProject.Controllers
{
    [Authorize] //需要登入才能存取
    public class CaseManagementController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CaseManagementController> _logger;

        public CaseManagementController(IConfiguration configuration, ILogger<CaseManagementController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }




        // 新增個案頁面 (管理者)
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }


        // 處理提交
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(UserViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "輸入資料格式錯誤,請重新檢查。";
                return View(viewModel);
            }

            // ✅ 驗證身分證字號格式(第一碼為英文大寫,第二碼為1或2,總共10碼)
            if (!System.Text.RegularExpressions.Regex.IsMatch(viewModel.IDNumber, @"^[A-Z][12]\d{8}$"))
            {
                ModelState.AddModelError("IDNumber", "身分證格式有誤:第一碼為英文大寫,第二碼為1或2,總共10碼");
                return View(viewModel);
            }

            // ✅ 驗證電話號碼格式(必須是10碼數字)
            if (!System.Text.RegularExpressions.Regex.IsMatch(viewModel.PhoneNumber, @"^\d{10}$"))
            {
                ModelState.AddModelError("PhoneNumber", "電話號碼格式有誤:請輸入10碼數字");
                return View(viewModel);
            }

            try
            {
                // 組合密碼:特殊符號 + 身分證字號
                string defaultPassword = viewModel.SpecialSymbol + viewModel.IDNumber;

                var model = new UserDBModel
                {
                    SpecialSymbol = viewModel.SpecialSymbol,
                    IDNumber = viewModel.IDNumber,
                    Username = viewModel.IDNumber,
                    PasswordHash = defaultPassword,
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
        (""SpecialSymbol"", ""IDNumber"", ""Username"", ""PasswordHash"", ""Role"", ""FullName"", 
         ""CreatedDate"", ""IsActive"", ""PhoneNumber"", ""IsFirstLogin"", ""LineUserId"")
        VALUES 
        (@specialsymbol, @idnumber, @username, @passwordhash, @role, @fullname, 
         @createddate, @isactive, @phonenumber, @isfirstlogin, @lineuserid);";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@specialsymbol", model.SpecialSymbol);
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

                TempData["SuccessMessage"] = $"✅ 已成功新增個案:{model.FullName}(身分證字號:{model.IDNumber})!預設密碼為 {defaultPassword}。";
                return RedirectToAction("Create");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ 發生錯誤:{ex.Message}";
                return View(viewModel);
            }
        }

        // 取得病患詳細資料（供表單使用）
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> GetPatientInfo(string idNumber)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection")
                    + ";SSL Mode=Require;Trust Server Certificate=True;";

                using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                string sql = @"
            SELECT ""Id"", ""FullName"", ""IDNumber"", ""PhoneNumber""
            FROM public.""Users""
            WHERE ""IDNumber"" = @idNumber;
        ";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@idNumber", idNumber);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return Json(new
                    {
                        success = true,
                        data = new
                        {
                            id = reader.GetInt32(0),
                            name = reader.GetString(1),
                            idNumber = reader.GetString(2),
                            gender = "男", // 如果 Users 表有性別欄位，請替換
                            birthDate = "", // 如果 Users 表有生日欄位，請替換
                            birthDateDisplay = "--"
                        }
                    });
                }

                return Json(new { success = false, message = "查無資料" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得病患資料失敗");
                return Json(new { success = false, message = "系統錯誤" });
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
                var connStr = _configuration.GetConnectionString("DefaultConnection")
                    + ";SSL Mode=Require;Trust Server Certificate=True;";

                using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // 查詢病患基本資料
                string sqlPatient = @"
            SELECT ""Id"", ""FullName"", ""IDNumber"", ""Username""
            FROM public.""Users""
            WHERE ""IDNumber"" = @idNumber;
        ";

                PatientData patient = null;
                using (var cmd = new NpgsqlCommand(sqlPatient, conn))
                {
                    cmd.Parameters.AddWithValue("@idNumber", request.idNumber);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        patient = new PatientData
                        {
                            Id = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            IDNumber = reader.GetString(2),
                            Username = reader.GetString(3)
                        };
                    }
                }

                if (patient == null)
                    return Json(new { success = false, message = "查無此病患資料" });

                // 查詢歷史紀錄數量與最新評估日期
                string sqlRecord = @"
            SELECT COUNT(*) AS RecordCount, MAX(""AssessmentDate"") AS LastRecordDate
            FROM public.""CaseManagement""
            WHERE ""IDNumber"" = @idNumber;
        ";

                int recordCount = 0;
                string lastRecordDate = "--";

                using (var cmd = new NpgsqlCommand(sqlRecord, conn))
                {
                    cmd.Parameters.AddWithValue("@idNumber", request.idNumber);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        recordCount = reader["RecordCount"] != DBNull.Value ? Convert.ToInt32(reader["RecordCount"]) : 0;
                        if (reader["LastRecordDate"] != DBNull.Value)
                            lastRecordDate = Convert.ToDateTime(reader["LastRecordDate"]).ToString("yyyy-MM-dd");
                    }
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        id = patient.Id,
                        name = patient.FullName,
                        idNumber = patient.IDNumber,
                        username = patient.Username,
                        recordCount = recordCount,
                        lastRecordDate = lastRecordDate
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

        public class PatientData
        {
            public int Id { get; set; }
            public string FullName { get; set; }
            public string IDNumber { get; set; }
            public string Username { get; set; }
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
        // 📋 查看個案目標值是否達標(ViewTargets/ViewDetails)
        // ========================================

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> ViewTargets(string idNumber = null)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection")
                + ";SSL Mode=Require;Trust Server Certificate=True;";

            var list = new List<TargetSummaryViewModel>();

            await using (var conn = new NpgsqlConnection(connStr))
            {
                await conn.OpenAsync();

                // 取每個個案最新一筆紀錄
                string sql = @"
    SELECT DISTINCT ON (""IDNumber"")
        ""Name"", ""IDNumber"",
        ""Weight"", ""WeightTarget_Value"",
        ""CurrentWaist_Value"", ""WaistTarget_Value"",
        ""FastingGlucose_Value"", ""FastingGlucoseTarget_Value"",
        ""HbA1c_Value"", ""HbA1cTarget_Value"",
        ""Triglycerides_Value"", ""TriglyceridesTarget_Value"",
        ""HDL_Value"", ""HDL_CholesterolTarget_Value"",
        ""LDL_Value"", ""LDL_CholesterolTarget_Value"",
        ""SmokingNone"",""SmokingUsually"",""SmokingUnder10"",""SmokingOver10"",
        ""BetelNutNone"",""BetelNutUsually"",""BetelNutAlways"",
        ""AssessmentDate""
    FROM public.""CaseManagement""
    WHERE (@idNumber IS NULL OR ""IDNumber"" ILIKE '%' || @idNumber || '%')
    ORDER BY ""IDNumber"", ""AssessmentDate"" DESC;
";

                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@idNumber", NpgsqlTypes.NpgsqlDbType.Text).Value = (object?)idNumber ?? DBNull.Value;

                    await using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int achievedCount = 0;
                            int total = 9; // 原本7項 + 抽菸 + 嚼檳榔 = 9項

                            // 體重
                            decimal? weight = reader["Weight"] as decimal?;
                            decimal? weightTarget = reader["WeightTarget_Value"] as decimal?;
                            if (CheckAchievement(weight, weightTarget, "weight")) achievedCount++;

                            // 腰圍
                            decimal? waist = reader["CurrentWaist_Value"] as decimal?;
                            decimal? waistTarget = reader["WaistTarget_Value"] as decimal?;
                            if (CheckAchievement(waist, waistTarget, "waist")) achievedCount++;

                            // 空腹血糖
                            decimal? glucose = reader["FastingGlucose_Value"] as decimal?;
                            decimal? glucoseTarget = reader["FastingGlucoseTarget_Value"] as decimal?;
                            if (CheckAchievement(glucose, glucoseTarget, "glucose")) achievedCount++;

                            // HbA1c
                            decimal? hba1c = reader["HbA1c_Value"] as decimal?;
                            decimal? hba1cTarget = reader["HbA1cTarget_Value"] as decimal?;
                            if (CheckAchievement(hba1c, hba1cTarget, "hba1c")) achievedCount++;

                            // 三酸甘油脂
                            decimal? triglycerides = reader["Triglycerides_Value"] as decimal?;
                            decimal? triglyceridesTarget = reader["TriglyceridesTarget_Value"] as decimal?;
                            if (CheckAchievement(triglycerides, triglyceridesTarget, "triglycerides")) achievedCount++;

                            // HDL
                            decimal? hdl = reader["HDL_Value"] as decimal?;
                            decimal? hdlTarget = reader["HDL_CholesterolTarget_Value"] as decimal?;
                            if (CheckAchievement(hdl, hdlTarget, "hdl")) achievedCount++;

                            // LDL
                            decimal? ldl = reader["LDL_Value"] as decimal?;
                            decimal? ldlTarget = reader["LDL_CholesterolTarget_Value"] as decimal?;
                            if (CheckAchievement(ldl, ldlTarget, "ldl")) achievedCount++;

                            // 抽菸 - 目標是 SmokingNone = true
                            bool smokingNone = reader["SmokingNone"] as bool? ?? false;
                            if (smokingNone) achievedCount++;

                            // 嚼檳榔 - 目標是 BetelNutNone = true
                            bool betelNutNone = reader["BetelNutNone"] as bool? ?? false;
                            if (betelNutNone) achievedCount++;

                            list.Add(new TargetSummaryViewModel
                            {
                                Name = reader["Name"].ToString(),
                                IDNumber = reader["IDNumber"].ToString(),
                                AchievedCount = achievedCount,
                                UnachievedCount = total - achievedCount
                            });
                        }
                    }
                }
            }

            ViewBag.SearchIdNumber = idNumber;
            return View(list);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> TargetDetails(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber))
            {
                TempData["ErrorMessage"] = "缺少身分證字號參數";
                return RedirectToAction("ViewTargets");
            }

            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection")
                    + ";SSL Mode=Require;Trust Server Certificate=True;";

                var viewModel = new CaseManagementViewModel
                {
                    EvaluationRecords = new List<EvaluationRecord>()
                };

                await using (var conn = new NpgsqlConnection(connStr))
                {
                    await conn.OpenAsync();

                    // 查詢該身分證字號的所有評估記錄
                    string sql = @"
        SELECT ""Id"", ""Name"", ""IDNumber"", ""Gender"", ""BirthDate"",""AssessmentDate"", ""AnnualAssessment_Date"",
               ""Weight"", ""WeightTarget_Value"",
               ""CurrentWaist_Value"", ""WaistTarget_Value"",
               ""FastingGlucose_Value"", ""FastingGlucoseTarget_Value"",
               ""HbA1c_Value"", ""HbA1cTarget_Value"",
               ""Triglycerides_Value"", ""TriglyceridesTarget_Value"",
               ""HDL_Value"", ""HDL_CholesterolTarget_Value"",
               ""LDL_Value"", ""LDL_CholesterolTarget_Value"",
               ""SmokingNone"",""SmokingUsually"",""SmokingUnder10"",""SmokingOver10"",
               ""BetelNutNone"",""BetelNutUsually"",""BetelNutAlways""
        FROM public.""CaseManagement""
        WHERE ""IDNumber"" = @idNumber
        ORDER BY COALESCE(""AssessmentDate"", ""AnnualAssessment_Date"") DESC";

                    await using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@idNumber", idNumber);

                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            bool isFirstRecord = true;

                            while (await reader.ReadAsync())
                            {
                                // 設定個案基本資料 (只需要設定一次)
                                if (isFirstRecord)
                                {
                                    viewModel.Name = reader["Name"].ToString();
                                    viewModel.IDNumber = reader["IDNumber"].ToString();
                                    viewModel.Gender = reader["Gender"].ToString();
                                    viewModel.BirthDate = reader["BirthDate"] as DateTime? ?? DateTime.MinValue;
                                    isFirstRecord = false;
                                }

                                // 決定評估日期 (優先使用 AssessmentDate,其次 AnnualAssessment_Date)
                                DateTime? evalDate = reader["AssessmentDate"] as DateTime?
                                    ?? reader["AnnualAssessment_Date"] as DateTime?;

                                // 如果沒有評估日期,跳過這筆記錄
                                if (evalDate == null)
                                    continue;

                                // 讀取各項數值
                                decimal? weight = reader["Weight"] as decimal?;
                                decimal? weightTarget = reader["WeightTarget_Value"] as decimal?;

                                decimal? waist = reader["CurrentWaist_Value"] as decimal?;
                                decimal? waistTarget = reader["WaistTarget_Value"] as decimal?;

                                decimal? glucose = reader["FastingGlucose_Value"] as decimal?;
                                decimal? glucoseTarget = reader["FastingGlucoseTarget_Value"] as decimal?;

                                decimal? hba1c = reader["HbA1c_Value"] as decimal?;
                                decimal? hba1cTarget = reader["HbA1cTarget_Value"] as decimal?;

                                decimal? triglycerides = reader["Triglycerides_Value"] as decimal?;
                                decimal? triglyceridesTarget = reader["TriglyceridesTarget_Value"] as decimal?;

                                decimal? hdl = reader["HDL_Value"] as decimal?;
                                decimal? hdlTarget = reader["HDL_CholesterolTarget_Value"] as decimal?;

                                decimal? ldl = reader["LDL_Value"] as decimal?;
                                decimal? ldlTarget = reader["LDL_CholesterolTarget_Value"] as decimal?;

                                // 讀取抽菸相關欄位
                                bool smokingNone = reader["SmokingNone"] as bool? ?? false;
                                bool smokingUsually = reader["SmokingUsually"] as bool? ?? false;
                                bool smokingUnder10 = reader["SmokingUnder10"] as bool? ?? false;
                                bool smokingOver10 = reader["SmokingOver10"] as bool? ?? false;

                                // 讀取嚼檳榔相關欄位
                                bool betelNutNone = reader["BetelNutNone"] as bool? ?? false;
                                bool betelNutUsually = reader["BetelNutUsually"] as bool? ?? false;
                                bool betelNutAlways = reader["BetelNutAlways"] as bool? ?? false;

                                // 建立評估記錄
                                var evaluationRecord = new EvaluationRecord
                                {
                                    CaseId = reader.GetInt32(0),
                                    EvaluationDate = evalDate.Value,

                                    // 腰圍
                                    WaistTarget_Value = waistTarget?.ToString("0.0") ?? "-",
                                    WaistCurrent_Value = waist?.ToString("0.0") ?? "-",
                                    WaistAchievement = CheckAchievement(waist, waistTarget, "waist"),

                                    // 體重
                                    WeightTarget_Value = weightTarget?.ToString("0.0") ?? "-",
                                    WeightCurrent_Value = weight?.ToString("0.0") ?? "-",
                                    WeightAchievement = CheckAchievement(weight, weightTarget, "weight"),

                                    // 空腹血糖
                                    FastingGlucoseTarget_Value = glucoseTarget?.ToString("0") ?? "-",
                                    FastingGlucoseCurrent_Value = glucose?.ToString("0") ?? "-",
                                    FastingGlucoseAchievement = CheckAchievement(glucose, glucoseTarget, "glucose"),

                                    // HbA1c
                                    HbA1cTarget_Value = hba1cTarget?.ToString("0.0") ?? "-",
                                    HbA1cCurrent_Value = hba1c?.ToString("0.0") ?? "-",
                                    HbA1cAchievement = CheckAchievement(hba1c, hba1cTarget, "hba1c"),

                                    // 三酸甘油脂
                                    TriglyceridesTarget_Value = triglyceridesTarget?.ToString("0") ?? "-",
                                    TriglyceridesCurrent_Value = triglycerides?.ToString("0") ?? "-",
                                    TriglyceridesAchievement = CheckAchievement(triglycerides, triglyceridesTarget, "triglycerides"),

                                    // HDL
                                    HDL_CholesterolTarget_Value = hdlTarget?.ToString("0") ?? "-",
                                    HDL_CholesterolCurrent_Value = hdl?.ToString("0") ?? "-",
                                    HDL_CholesterolAchievement = CheckAchievement(hdl, hdlTarget, "hdl"),

                                    // LDL
                                    LDL_CholesterolTarget_Value = ldlTarget?.ToString("0") ?? "-",
                                    LDL_CholesterolCurrent_Value = ldl?.ToString("0") ?? "-",
                                    LDL_CholesterolAchievement = CheckAchievement(ldl, ldlTarget, "ldl"),

                                    // 抽菸
                                    SmokingNone = smokingNone,
                                    SmokingUsually = smokingUsually,
                                    SmokingUnder10 = smokingUnder10,
                                    SmokingOver10 = smokingOver10,

                                    // 嚼檳榔
                                    BetelNutNone = betelNutNone,
                                    BetelNutUsually = betelNutUsually,
                                    BetelNutAlways = betelNutAlways
                                };

                                viewModel.EvaluationRecords.Add(evaluationRecord);
                            }
                        }
                    }
                }

                if (!viewModel.EvaluationRecords.Any())
                {
                    TempData["ErrorMessage"] = "找不到該個案的評估記錄";
                    return RedirectToAction("ViewTargets");
                }

                return View("TargetDetails", viewModel);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢個案目標詳細內容失敗 - IDNumber: {IDNumber}", idNumber);
                TempData["ErrorMessage"] = $"查詢失敗: {ex.Message}";
                return RedirectToAction("ViewTargets");
            }
        }

        // ========================================
        // 🔍 輔助方法:判斷是否達成目標
        // ========================================

        /// <summary>
        /// 判斷指標是否達成目標
        /// </summary>
        /// <param name="currentValue">當前值</param>
        /// <param name="targetValue">目標值</param>
        /// <param name="type">指標類型 (waist, weight, glucose, hba1c, triglycerides, hdl, ldl)</param>
        /// <returns>是否達成</returns>
        private bool CheckAchievement(decimal? currentValue, decimal? targetValue, string type)
        {
            // 如果任一值為 null,視為未達成
            if (currentValue == null || targetValue == null)
                return false;

            switch (type.ToLower())
            {
                case "waist":
                case "weight":
                case "glucose":
                case "hba1c":
                case "triglycerides":
                case "ldl":
                    // 這些指標是"越低越好",當前值要小於等於目標值
                    return currentValue <= targetValue;

                case "hdl":
                    // HDL 是"越高越好",當前值要大於等於目標值
                    return currentValue >= targetValue;

                default:
                    return false;
            }
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
                // 搜尋特定病患的最新一筆紀錄
                records = await GetLatestRecordsByIdNumberAsync(searchIdNumber);
                ViewBag.SearchIdNumber = searchIdNumber;
            }
            else
            {
                // 顯示所有個案的最新一筆紀錄
                records = await GetLatestRecordsForAllPatientsAsync();
            }

            return View(records);
        }

        // ========================================
        // 📋 PatientHistory - 顯示個案所有歷史記錄 + 日期篩選
        // ========================================

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PatientHistory(string idNumber, int? year = null, int? month = null)
        {
            if (string.IsNullOrEmpty(idNumber))
            {
                TempData["ErrorMessage"] = "缺少身分證字號參數";
                return RedirectToAction("ViewAllRecords");
            }

            try
            {
                var records = await GetPatientHistoryAsync(idNumber, year, month);

                // 🔧 無論有沒有記錄,都先取得病患基本資訊
                ViewBag.PatientIdNumber = idNumber;

                if (!records.Any())
                {
                    // 檢查這個病患是否存在(查詢所有記錄,不帶日期篩選)
                    var allRecords = await GetPatientHistoryAsync(idNumber, null, null);

                    if (!allRecords.Any())
                    {
                        // 如果完全沒有記錄,才重定向
                        TempData["ErrorMessage"] = "查無此個案的評估記錄";
                        return RedirectToAction("ViewAllRecords");
                    }

                    // 🆕 從所有記錄中取得病患基本資訊
                    var firstRecord = allRecords.FirstOrDefault();
                    if (firstRecord != null)
                    {
                        ViewBag.PatientName = firstRecord.Name;
                        ViewBag.PatientGender = firstRecord.Gender;
                        ViewBag.PatientBirthDate = firstRecord.BirthDate;
                    }

                    ViewBag.SelectedYear = year;
                    ViewBag.SelectedMonth = month;
                    ViewBag.HasFilterApplied = (year != null || month != null);

                    return View(new List<CaseManagementViewModel>());
                }

                ViewBag.SelectedYear = year;
                ViewBag.SelectedMonth = month;
                ViewBag.HasFilterApplied = (year != null || month != null);

                return View(records);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢個案歷史記錄失敗 - IDNumber: {IDNumber}", idNumber);
                TempData["ErrorMessage"] = $"查詢失敗: {ex.Message}";
                return RedirectToAction("ViewAllRecords");
            }
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
        // 在 CaseManagementController.cs 加入
        // 在 CaseManagementController.cs 的 GetLatestPatientData 方法中修改
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> GetLatestPatientData(string idNumber)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection")
                    + ";SSL Mode=Require;Trust Server Certificate=True;";

                using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // 先查 Users 表取得基本資料
                string sqlUser = @"
            SELECT ""Id"", ""FullName"", ""IDNumber""
            FROM public.""Users""
            WHERE ""IDNumber"" = @idNumber;
        ";

                int userId = 0;
                string fullName = "";

                using (var cmd = new NpgsqlCommand(sqlUser, conn))
                {
                    cmd.Parameters.AddWithValue("@idNumber", idNumber);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        userId = reader.GetInt32(0);
                        fullName = reader.GetString(1);
                    }
                }

                if (userId == 0)
                    return Json(new { success = false, message = "查無此病患" });

                // 🆕 自動從身分證字號判斷性別
                string gender = "";
                if (!string.IsNullOrEmpty(idNumber) && idNumber.Length >= 2)
                {
                    char secondChar = idNumber[1];
                    gender = secondChar == '1' ? "男" : secondChar == '2' ? "女" : "";
                }

                // 查詢最新一筆紀錄的生日
                string sqlLatest = @"
            SELECT ""BirthDate""
            FROM public.""CaseManagement""
            WHERE ""IDNumber"" = @idNumber
            ORDER BY ""Id"" DESC
            LIMIT 1;
        ";

                string birthDate = "";

                using (var cmd = new NpgsqlCommand(sqlLatest, conn))
                {
                    cmd.Parameters.AddWithValue("@idNumber", idNumber);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        birthDate = reader.IsDBNull(0) ? "" : reader.GetDateTime(0).ToString("yyyy-MM-dd");
                    }
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        userId = userId,
                        name = fullName,
                        idNumber = idNumber,
                        gender = gender,
                        birthDate = birthDate
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得病患資料失敗");
                return Json(new { success = false, message = "系統錯誤" });
            }
        }

        // 查看個案填寫狀況
        // 查看個案填寫狀況
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> MissedRecordsStatus(string searchIdNumber = null, string tab = null, DateTime? checkDate = null)
        {
            var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HttpContext.Session.SetString("LastViewedMissedRecords", currentTime);

            try
            {
                List<MissedRecordViewModel> allRecordsData;
                DateTime dateToCheck = checkDate?.Date ?? DateTime.Today.Date;

                const string CacheKey = "AllMissedRecordsData";
                bool isSearching = !string.IsNullOrEmpty(searchIdNumber) || checkDate.HasValue;

                if (!isSearching && HttpContext.Session.TryGetValue(CacheKey, out byte[] cachedBytes))
                {
                    string json = Encoding.UTF8.GetString(cachedBytes);
                    allRecordsData = JsonSerializer.Deserialize<List<MissedRecordViewModel>>(json);
                }
                else
                {
                    allRecordsData = await GetMissedRecordsAndCaseInfoAsync(searchIdNumber, dateToCheck);

                    if (!isSearching && dateToCheck == DateTime.Today.Date)
                    {
                        string json = JsonSerializer.Serialize(allRecordsData);
                        HttpContext.Session.Set(CacheKey, Encoding.UTF8.GetBytes(json));
                    }
                }

                var trackingCandidates = allRecordsData.Where(r => r.Is722Tracking).ToList();
                var trackingList = await Get722TrackingListAsync(trackingCandidates, dateToCheck);
                var allMissedDaysRecords = allRecordsData.Where(r => r.MissedDays >= 2 || r.MissedDays == 999).ToList();

                ViewBag.AllMissedDaysRecords = allMissedDaysRecords;

                // 🎯 新增：查詢結果（獨立於 tab，只要有查詢就顯示）
                MissedRecordViewModel searchResult = null;
                if (!string.IsNullOrEmpty(searchIdNumber))
                {
                    // 從所有資料中尋找該個案（不受 MissedDays 限制）
                    searchResult = allRecordsData.FirstOrDefault(m =>
                        m.IDNumber.Equals(searchIdNumber, StringComparison.OrdinalIgnoreCase));

                    // 如果在 allRecordsData 找不到，從 Users 表直接查詢
                    if (searchResult == null)
                    {
                        searchResult = await GetPatientBasicInfoAsync(searchIdNumber);
                    }
                }
                ViewBag.SearchResult = searchResult;

                // 如果沒有指定 tab，預設為 days2
                if (string.IsNullOrEmpty(tab))
                {
                    tab = "days2";
                }

                // 🎯 根據 tab 篩選要顯示在下方表格的資料
                List<MissedRecordViewModel> recordsToShow;

                switch (tab.ToLower())
                {
                    case "722":
                        recordsToShow = trackingList;
                        break;
                    case "days2":
                        recordsToShow = allMissedDaysRecords.Where(m => m.MissedDays == 2).ToList();
                        break;
                    case "days3":
                        recordsToShow = allMissedDaysRecords.Where(m => m.MissedDays == 3).ToList();
                        break;
                    case "days4":
                        recordsToShow = allMissedDaysRecords.Where(m => m.MissedDays == 4).ToList();
                        break;
                    case "days5plus":
                        recordsToShow = allMissedDaysRecords.Where(m => m.MissedDays >= 5 && m.MissedDays < 999).ToList();
                        break;
                    case "never":
                        recordsToShow = allMissedDaysRecords.Where(m => m.MissedDays >= 999).ToList();
                        break;
                    default:
                        recordsToShow = allMissedDaysRecords.Where(m => m.MissedDays == 2).ToList();
                        break;
                }

                ViewBag.SearchIdNumber = searchIdNumber;
                ViewBag.TrackingList = trackingList;
                ViewBag.ActiveTab = tab;
                ViewBag.CheckDate = dateToCheck;

                return View(recordsToShow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢未填寫狀況失敗");
                TempData["ErrorMessage"] = $"查詢失敗: {ex.Message}";
                return View(new List<MissedRecordViewModel>());
            }
        }

        // 🆕 新增：取得個案基本資訊（用於查詢結果顯示）
        private async Task<MissedRecordViewModel> GetPatientBasicInfoAsync(string idNumber)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection")
                + ";SSL Mode=Require;Trust Server Certificate=True;";

            using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            string sql = @"
        SELECT 
            u.""Id"" as UserId, u.""IDNumber"", u.""FullName"", u.""PhoneNumber"",
            MAX(t.""RecordDate"") FILTER (WHERE t.""IsReminderRecord"" = FALSE) as LastRecordDate,
            c.""Gender"", c.""BirthDate"", c.""BloodPressureGuidance722""
        FROM public.""Users"" u
        LEFT JOIN public.""Today"" t ON u.""Id"" = t.""UserId"" 
            AND t.""IsReminderRecord"" = FALSE
        LEFT JOIN (
            SELECT DISTINCT ON (""IDNumber"") *
            FROM public.""CaseManagement""
            ORDER BY ""IDNumber"", ""AssessmentDate"" DESC
        ) c ON u.""IDNumber"" = c.""IDNumber""
        WHERE u.""IDNumber"" = @idNumber
            AND u.""Role"" = 'Patient'
            AND u.""IsActive"" = TRUE
        GROUP BY u.""Id"", u.""IDNumber"", u.""FullName"", u.""PhoneNumber"", 
                 c.""Gender"", c.""BirthDate"", c.""BloodPressureGuidance722""
    ";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@idNumber", idNumber);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int userId = reader.GetInt32(0);
                string fullName = reader.GetString(2);
                string phoneNumber = reader.IsDBNull(3) ? "" : reader.GetString(3);
                DateTime? lastRecordDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
                string gender = reader.IsDBNull(5) ? "" : reader.GetString(5);
                DateTime birthDate = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6);
                bool is722Tracking = reader.IsDBNull(7) ? false : reader.GetBoolean(7);

                // 補充性別判斷
                if (string.IsNullOrEmpty(gender) && idNumber.Length >= 2)
                {
                    char secondChar = idNumber[1];
                    gender = secondChar == '1' ? "男" : secondChar == '2' ? "女" : "";
                }

                int missedDays = 0;
                if (lastRecordDate.HasValue)
                {
                    missedDays = (DateTime.Today.Date - lastRecordDate.Value.Date).Days;
                }
                else
                {
                    missedDays = 999;
                }

                string missedReason = await GetLatestMissedReasonAsync(userId, connStr);

                return new MissedRecordViewModel
                {
                    UserId = userId,
                    IDNumber = idNumber,
                    FullName = fullName,
                    PhoneNumber = phoneNumber,
                    Gender = gender,
                    BirthDate = birthDate,
                    LastRecordDate = lastRecordDate,
                    MissedDays = missedDays,
                    MissedReason = missedReason,
                    Is722Tracking = is722Tracking
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
               ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"", ""HbA1c"", ""HbA1c_Value"",
               ""HDL"", ""HDL_Value"", ""LDL"", ""LDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
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

                    // 腰圍/血糖/脂質 (索引 19-30)
                    CurrentWaist = reader.GetBoolean(19),
                    CurrentWaist_Value = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                    FastingGlucose = reader.GetBoolean(21),
                    FastingGlucose_Value = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                    HbA1c = reader.GetBoolean(23),
                    HbA1c_Value = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                    HDL = reader.GetBoolean(25),
                    HDL_Value = reader.IsDBNull(26) ? null : reader.GetDecimal(26),
                    LDL = reader.GetBoolean(27),
                    LDL_Value = reader.IsDBNull(28) ? null : reader.GetDecimal(28),
                    Triglycerides = reader.GetBoolean(29),
                    Triglycerides_Value = reader.IsDBNull(30) ? null : reader.GetDecimal(30),

                    // 生活型態 - 運動 (索引 31-33)
                    ExerciseNone = reader.GetBoolean(31),
                    ExerciseUsually = reader.GetBoolean(32),
                    ExerciseAlways = reader.GetBoolean(33),

                    // 生活型態 - 抽菸 (索引 34-37)
                    SmokingNone = reader.GetBoolean(34),
                    SmokingUsually = reader.GetBoolean(35),
                    SmokingUnder10 = reader.GetBoolean(36),
                    SmokingOver10 = reader.GetBoolean(37),

                    // 生活型態 - 檳榔 (索引 38-40)
                    BetelNutNone = reader.GetBoolean(38),
                    BetelNutUsually = reader.GetBoolean(39),
                    BetelNutAlways = reader.GetBoolean(40),

                    // 疾病風險評估 - 冠心病 (索引 41-44)
                    CoronaryHigh = reader.GetBoolean(41),
                    CoronaryMedium = reader.GetBoolean(42),
                    CoronaryLow = reader.GetBoolean(43),
                    CoronaryNotApplicable = reader.GetBoolean(44),

                    // 疾病風險評估 - 糖尿病 (索引 45-48)
                    DiabetesHigh = reader.GetBoolean(45),
                    DiabetesMedium = reader.GetBoolean(46),
                    DiabetesLow = reader.GetBoolean(47),
                    DiabetesNotApplicabe = reader.GetBoolean(48),

                    // 疾病風險評估 - 高血壓 (索引 49-52)
                    HypertensionHigh = reader.GetBoolean(49),
                    HypertensionMedium = reader.GetBoolean(50),
                    HypertensionLow = reader.GetBoolean(51),
                    HypertensionNotApplicable = reader.GetBoolean(52),

                    // 疾病風險評估 - 腦中風 (索引 53-56)
                    StrokeHigh = reader.GetBoolean(53),
                    StrokeMedium = reader.GetBoolean(54),
                    StrokeLow = reader.GetBoolean(55),
                    StrokeNotApplicable = reader.GetBoolean(56),

                    // 疾病風險評估 - 心血管 (索引 57-60)
                    CardiovascularHigh = reader.GetBoolean(57),
                    CardiovascularMedium = reader.GetBoolean(58),
                    CardiovascularLow = reader.GetBoolean(59),
                    CardiovascularNotApplicable = reader.GetBoolean(60),

                    // 戒菸服務 (索引 61-65)
                    SmokingService = reader.GetBoolean(61),
                    SmokingServiceType1 = reader.GetBoolean(62),
                    SmokingServiceType2 = reader.GetBoolean(63),
                    SmokingServiceType2_Provide = reader.GetBoolean(64),
                    SmokingServiceType2_Referral = reader.GetBoolean(65),

                    // 戒檳服務 (索引 66-70)
                    BetelNutService = reader.GetBoolean(66),
                    BetelQuitGoal = reader.GetBoolean(67),
                    BetelQuitYear = reader.IsDBNull(68) ? null : reader.GetInt32(68),
                    BetelQuitMonth = reader.IsDBNull(69) ? null : reader.GetInt32(69),
                    BetelQuitDay = reader.IsDBNull(70) ? null : reader.GetInt32(70),

                    // 口腔檢查 (索引 71-73)
                    OralExam = reader.GetBoolean(71),
                    OralExamYear = reader.IsDBNull(72) ? null : reader.GetInt32(72),
                    OralExamMonth = reader.IsDBNull(73) ? null : reader.GetInt32(73),

                    // 飲食管理 - 每日建議攝取熱量 (索引 74-80)
                    DietManagement = reader.GetBoolean(74),
                    DailyCalories1200 = reader.GetBoolean(75),
                    DailyCalories1500 = reader.GetBoolean(76),
                    DailyCalories1800 = reader.GetBoolean(77),
                    DailyCalories2000 = reader.GetBoolean(78),
                    DailyCaloriesOther = reader.GetBoolean(79),
                    DailyCaloriesOtherValue = reader.IsDBNull(80) ? null : reader.GetString(80),

                    // 飲食管理 - 盡量減少 (索引 81-86)
                    ReduceFriedFood = reader.GetBoolean(81),
                    ReduceSweetFood = reader.GetBoolean(82),
                    ReduceSalt = reader.GetBoolean(83),
                    ReduceSugaryDrinks = reader.GetBoolean(84),
                    ReduceOther = reader.GetBoolean(85),
                    ReduceOtherValue = reader.IsDBNull(86) ? null : reader.GetString(86),

                    // 運動建議與資源 (索引 87-90)
                    ExerciseRecommendation = reader.GetBoolean(87),
                    ExerciseGuidance = reader.GetBoolean(88),
                    SocialExerciseResources = reader.GetBoolean(89),
                    SocialExerciseResources_Text = reader.IsDBNull(90) ? null : reader.GetString(90),

                    // 目標設定 (索引 91-93)
                    Achievement = reader.GetBoolean(91),
                    WaistTarget_Value = reader.IsDBNull(92) ? null : reader.GetDecimal(92),
                    WeightTarget_Value = reader.IsDBNull(93) ? null : reader.GetDecimal(93),

                    // 其他叮嚀/目標值 (索引 94-104)
                    OtherReminders = reader.GetBoolean(94),
                    FastingGlucoseTarget = reader.GetBoolean(95),
                    FastingGlucoseTarget_Value = reader.IsDBNull(96) ? null : reader.GetDecimal(96),
                    HbA1cTarget = reader.GetBoolean(97),
                    HbA1cTarget_Value = reader.IsDBNull(98) ? null : reader.GetDecimal(98),
                    TriglyceridesTarget = reader.GetBoolean(99),
                    TriglyceridesTarget_Value = reader.IsDBNull(100) ? null : reader.GetDecimal(100),
                    HDL_CholesterolTarget = reader.GetBoolean(101),
                    HDL_CholesterolTarget_Value = reader.IsDBNull(102) ? null : reader.GetDecimal(102),
                    LDL_CholesterolTarget = reader.GetBoolean(103),
                    LDL_CholesterolTarget_Value = reader.IsDBNull(104) ? null : reader.GetDecimal(104),

                    // 備註 (索引 105)
                    Notes = reader.IsDBNull(105) ? null : reader.GetString(105)
                });
            }

            return records;
        }

        /// <summary>
        /// 取得所有個案的最新一筆紀錄
        /// </summary>
        private async Task<List<CaseManagementViewModel>> GetLatestRecordsForAllPatientsAsync()
        {
            var records = new List<CaseManagementViewModel>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // 使用 DISTINCT ON 取得每個身分證字號的最新一筆
            var query = @"
        SELECT DISTINCT ON (""IDNumber"")
            ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"", 
            ""Height"", ""Weight"", ""BMI_Value"", ""CurrentWaist_Value"",
            ""AssessmentDate"", ""AnnualAssessment_Date"", ""FollowUpDate"", ""AnnualAssessment""
        FROM ""CaseManagement""
        ORDER BY ""IDNumber"", 
                 COALESCE(""AssessmentDate"", ""AnnualAssessment_Date"") DESC NULLS LAST, 
                 ""Id"" DESC
        LIMIT 100";

            await using var command = new NpgsqlCommand(query, connection);
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
                    Height = reader.GetDecimal(6),
                    Weight = reader.GetDecimal(7),
                    BMI_Value = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    CurrentWaist_Value = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    AssessmentDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    AnnualAssessment_Date = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    FollowUpDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                    AnnualAssessment = reader.GetBoolean(13)
                });
            }

            return records;
        }

        /// <summary>
        /// 根據身分證查詢該個案的最新一筆紀錄
        /// </summary>
        private async Task<List<CaseManagementViewModel>> GetLatestRecordsByIdNumberAsync(string idNumber)
        {
            var records = new List<CaseManagementViewModel>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
        SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"", 
               ""Height"", ""Weight"", ""BMI_Value"", ""CurrentWaist_Value"",
               ""AssessmentDate"", ""AnnualAssessment_Date"", ""FollowUpDate"", ""AnnualAssessment""
        FROM ""CaseManagement""
        WHERE ""IDNumber"" = @IDNumber
        ORDER BY COALESCE(""AssessmentDate"", ""AnnualAssessment_Date"") DESC NULLS LAST, ""Id"" DESC
        LIMIT 1";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@IDNumber", idNumber);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                records.Add(new CaseManagementViewModel
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    IDNumber = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    BirthDate = reader.GetDateTime(5),
                    Height = reader.GetDecimal(6),
                    Weight = reader.GetDecimal(7),
                    BMI_Value = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    CurrentWaist_Value = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    AssessmentDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    AnnualAssessment_Date = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    FollowUpDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                    AnnualAssessment = reader.GetBoolean(13)
                });
            }

            return records;
        }

        /// <summary>
        /// 取得個案所有歷史記錄 (支援年月篩選) - 完整版
        /// </summary>

        private async Task<List<CaseManagementViewModel>> GetPatientHistoryAsync(string idNumber, int? year, int? month)
        {
            var records = new List<CaseManagementViewModel>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // 先取得該個案的第一筆紀錄 ID (按評估日期排序)
            int? firstRecordId = null;
            var queryFirstRecord = @"
        SELECT ""Id""
        FROM ""CaseManagement""
        WHERE ""IDNumber"" = @IDNumber
        ORDER BY COALESCE(""AssessmentDate"", ""AnnualAssessment_Date"") ASC NULLS LAST, ""Id"" ASC
        LIMIT 1";

            await using (var cmdFirst = new NpgsqlCommand(queryFirstRecord, connection))
            {
                cmdFirst.Parameters.AddWithValue("@IDNumber", idNumber);
                var result = await cmdFirst.ExecuteScalarAsync();
                if (result != null)
                    firstRecordId = Convert.ToInt32(result);
            }

            // 建立查詢語句 (包含年月篩選) - 抓取所有欄位
            var queryBuilder = new System.Text.StringBuilder(@"
        SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"",
               ""Height"", ""Weight"", ""BMI"", ""BMI_Value"",
               ""AssessmentDate"", ""FollowUpDate"",
               ""AnnualAssessment"", ""AnnualAssessment_Date"",
               ""SystolicBP"", ""SystolicBP_Value"", ""DiastolicBP"", ""DiastolicBP_Value"",
               ""BloodPressureGuidance722"",
               ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"", ""HbA1c"", ""HbA1c_Value"",
               ""HDL"", ""HDL_Value"", ""LDL"", ""LDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
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
        WHERE ""IDNumber"" = @IDNumber");

            if (year.HasValue)
            {
                queryBuilder.Append(@" AND EXTRACT(YEAR FROM COALESCE(""AssessmentDate"", ""AnnualAssessment_Date"")) = @Year");
            }

            if (month.HasValue)
            {
                queryBuilder.Append(@" AND EXTRACT(MONTH FROM COALESCE(""AssessmentDate"", ""AnnualAssessment_Date"")) = @Month");
            }

            queryBuilder.Append(@" ORDER BY COALESCE(""AssessmentDate"", ""AnnualAssessment_Date"") DESC NULLS LAST, ""Id"" DESC");

            await using var command = new NpgsqlCommand(queryBuilder.ToString(), connection);
            command.Parameters.AddWithValue("@IDNumber", idNumber);

            if (year.HasValue)
                command.Parameters.AddWithValue("@Year", year.Value);

            if (month.HasValue)
                command.Parameters.AddWithValue("@Month", month.Value);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var record = new CaseManagementViewModel
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
                    HbA1c = reader.GetBoolean(23),
                    HbA1c_Value = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                    HDL = reader.GetBoolean(25),
                    HDL_Value = reader.IsDBNull(26) ? null : reader.GetDecimal(26),
                    LDL = reader.GetBoolean(27),
                    LDL_Value = reader.IsDBNull(28) ? null : reader.GetDecimal(28),
                    Triglycerides = reader.GetBoolean(29),
                    Triglycerides_Value = reader.IsDBNull(30) ? null : reader.GetDecimal(30),
                    ExerciseNone = reader.GetBoolean(31),
                    ExerciseUsually = reader.GetBoolean(32),
                    ExerciseAlways = reader.GetBoolean(33),
                    SmokingNone = reader.GetBoolean(34),
                    SmokingUsually = reader.GetBoolean(35),
                    SmokingUnder10 = reader.GetBoolean(36),
                    SmokingOver10 = reader.GetBoolean(37),
                    BetelNutNone = reader.GetBoolean(38),
                    BetelNutUsually = reader.GetBoolean(39),
                    BetelNutAlways = reader.GetBoolean(40),
                    CoronaryHigh = reader.GetBoolean(41),
                    CoronaryMedium = reader.GetBoolean(42),
                    CoronaryLow = reader.GetBoolean(43),
                    CoronaryNotApplicable = reader.GetBoolean(44),
                    DiabetesHigh = reader.GetBoolean(45),
                    DiabetesMedium = reader.GetBoolean(46),
                    DiabetesLow = reader.GetBoolean(47),
                    DiabetesNotApplicabe = reader.GetBoolean(48),
                    HypertensionHigh = reader.GetBoolean(49),
                    HypertensionMedium = reader.GetBoolean(50),
                    HypertensionLow = reader.GetBoolean(51),
                    HypertensionNotApplicable = reader.GetBoolean(52),
                    StrokeHigh = reader.GetBoolean(53),
                    StrokeMedium = reader.GetBoolean(54),
                    StrokeLow = reader.GetBoolean(55),
                    StrokeNotApplicable = reader.GetBoolean(56),
                    CardiovascularHigh = reader.GetBoolean(57),
                    CardiovascularMedium = reader.GetBoolean(58),
                    CardiovascularLow = reader.GetBoolean(59),
                    CardiovascularNotApplicable = reader.GetBoolean(60),
                    SmokingService = reader.GetBoolean(61),
                    SmokingServiceType1 = reader.GetBoolean(62),
                    SmokingServiceType2 = reader.GetBoolean(63),
                    SmokingServiceType2_Provide = reader.GetBoolean(64),
                    SmokingServiceType2_Referral = reader.GetBoolean(65),
                    BetelNutService = reader.GetBoolean(66),
                    BetelQuitGoal = reader.GetBoolean(67),
                    BetelQuitYear = reader.IsDBNull(68) ? null : reader.GetInt32(68),
                    BetelQuitMonth = reader.IsDBNull(69) ? null : reader.GetInt32(69),
                    BetelQuitDay = reader.IsDBNull(70) ? null : reader.GetInt32(70),
                    OralExam = reader.GetBoolean(71),
                    OralExamYear = reader.IsDBNull(72) ? null : reader.GetInt32(72),
                    OralExamMonth = reader.IsDBNull(73) ? null : reader.GetInt32(73),
                    DietManagement = reader.GetBoolean(74),
                    DailyCalories1200 = reader.GetBoolean(75),
                    DailyCalories1500 = reader.GetBoolean(76),
                    DailyCalories1800 = reader.GetBoolean(77),
                    DailyCalories2000 = reader.GetBoolean(78),
                    DailyCaloriesOther = reader.GetBoolean(79),
                    DailyCaloriesOtherValue = reader.IsDBNull(80) ? null : reader.GetString(80),
                    ReduceFriedFood = reader.GetBoolean(81),
                    ReduceSweetFood = reader.GetBoolean(82),
                    ReduceSalt = reader.GetBoolean(83),
                    ReduceSugaryDrinks = reader.GetBoolean(84),
                    ReduceOther = reader.GetBoolean(85),
                    ReduceOtherValue = reader.IsDBNull(86) ? null : reader.GetString(86),
                    ExerciseRecommendation = reader.GetBoolean(87),
                    ExerciseGuidance = reader.GetBoolean(88),
                    SocialExerciseResources = reader.GetBoolean(89),
                    SocialExerciseResources_Text = reader.IsDBNull(90) ? null : reader.GetString(90),
                    Achievement = reader.GetBoolean(91),
                    WaistTarget_Value = reader.IsDBNull(92) ? null : reader.GetDecimal(92),
                    WeightTarget_Value = reader.IsDBNull(93) ? null : reader.GetDecimal(93),
                    OtherReminders = reader.GetBoolean(94),
                    FastingGlucoseTarget = reader.GetBoolean(95),
                    FastingGlucoseTarget_Value = reader.IsDBNull(96) ? null : reader.GetDecimal(96),
                    HbA1cTarget = reader.GetBoolean(97),
                    HbA1cTarget_Value = reader.IsDBNull(98) ? null : reader.GetDecimal(98),
                    TriglyceridesTarget = reader.GetBoolean(99),
                    TriglyceridesTarget_Value = reader.IsDBNull(100) ? null : reader.GetDecimal(100),
                    HDL_CholesterolTarget = reader.GetBoolean(101),
                    HDL_CholesterolTarget_Value = reader.IsDBNull(102) ? null : reader.GetDecimal(102),
                    LDL_CholesterolTarget = reader.GetBoolean(103),
                    LDL_CholesterolTarget_Value = reader.IsDBNull(104) ? null : reader.GetDecimal(104),
                    Notes = reader.IsDBNull(105) ? null : reader.GetString(105)
                };

                // ⭐ 關鍵邏輯：如果這筆紀錄的 Id 等於第一筆的 Id,強制設為收案評估
                if (firstRecordId.HasValue && record.Id == firstRecordId.Value)
                {
                    record.AnnualAssessment = false; // 強制設為收案評估
                }

                records.Add(record);
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
               ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"", ""HbA1c"", ""HbA1c_Value"",
               ""HDL"", ""HDL_Value"", ""LDL"", ""LDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
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

                    // 腰圍/血糖/脂質 (索引 19-30)
                    CurrentWaist = reader.GetBoolean(19),
                    CurrentWaist_Value = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                    FastingGlucose = reader.GetBoolean(21),
                    FastingGlucose_Value = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                    HbA1c = reader.GetBoolean(23),
                    HbA1c_Value = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                    HDL = reader.GetBoolean(25),
                    HDL_Value = reader.IsDBNull(26) ? null : reader.GetDecimal(26),
                    LDL = reader.GetBoolean(27),
                    LDL_Value = reader.IsDBNull(28) ? null : reader.GetDecimal(28),
                    Triglycerides = reader.GetBoolean(29),
                    Triglycerides_Value = reader.IsDBNull(30) ? null : reader.GetDecimal(30),

                    // 生活型態 - 運動 (索引 31-33)
                    ExerciseNone = reader.GetBoolean(31),
                    ExerciseUsually = reader.GetBoolean(32),
                    ExerciseAlways = reader.GetBoolean(33),

                    // 生活型態 - 抽菸 (索引 34-37)
                    SmokingNone = reader.GetBoolean(34),
                    SmokingUsually = reader.GetBoolean(35),
                    SmokingUnder10 = reader.GetBoolean(36),
                    SmokingOver10 = reader.GetBoolean(37),

                    // 生活型態 - 檳榔 (索引 38-40)
                    BetelNutNone = reader.GetBoolean(38),
                    BetelNutUsually = reader.GetBoolean(39),
                    BetelNutAlways = reader.GetBoolean(40),

                    // 疾病風險評估 - 冠心病 (索引 41-44)
                    CoronaryHigh = reader.GetBoolean(41),
                    CoronaryMedium = reader.GetBoolean(42),
                    CoronaryLow = reader.GetBoolean(43),
                    CoronaryNotApplicable = reader.GetBoolean(44),

                    // 疾病風險評估 - 糖尿病 (索引 45-48)
                    DiabetesHigh = reader.GetBoolean(45),
                    DiabetesMedium = reader.GetBoolean(46),
                    DiabetesLow = reader.GetBoolean(47),
                    DiabetesNotApplicabe = reader.GetBoolean(48),

                    // 疾病風險評估 - 高血壓 (索引 49-52)
                    HypertensionHigh = reader.GetBoolean(49),
                    HypertensionMedium = reader.GetBoolean(50),
                    HypertensionLow = reader.GetBoolean(51),
                    HypertensionNotApplicable = reader.GetBoolean(52),

                    // 疾病風險評估 - 腦中風 (索引 53-56)
                    StrokeHigh = reader.GetBoolean(53),
                    StrokeMedium = reader.GetBoolean(54),
                    StrokeLow = reader.GetBoolean(55),
                    StrokeNotApplicable = reader.GetBoolean(56),

                    // 疾病風險評估 - 心血管 (索引 57-60)
                    CardiovascularHigh = reader.GetBoolean(57),
                    CardiovascularMedium = reader.GetBoolean(58),
                    CardiovascularLow = reader.GetBoolean(59),
                    CardiovascularNotApplicable = reader.GetBoolean(60),

                    // 戒菸服務 (索引 61-65)
                    SmokingService = reader.GetBoolean(61),
                    SmokingServiceType1 = reader.GetBoolean(62),
                    SmokingServiceType2 = reader.GetBoolean(63),
                    SmokingServiceType2_Provide = reader.GetBoolean(64),
                    SmokingServiceType2_Referral = reader.GetBoolean(65),

                    // 戒檳服務 (索引 66-70)
                    BetelNutService = reader.GetBoolean(66),
                    BetelQuitGoal = reader.GetBoolean(67),
                    BetelQuitYear = reader.IsDBNull(68) ? null : reader.GetInt32(68),
                    BetelQuitMonth = reader.IsDBNull(69) ? null : reader.GetInt32(69),
                    BetelQuitDay = reader.IsDBNull(70) ? null : reader.GetInt32(70),

                    // 口腔檢查 (索引 71-73)
                    OralExam = reader.GetBoolean(71),
                    OralExamYear = reader.IsDBNull(72) ? null : reader.GetInt32(72),
                    OralExamMonth = reader.IsDBNull(73) ? null : reader.GetInt32(73),

                    // 飲食管理 - 每日建議攝取熱量 (索引 74-80)
                    DietManagement = reader.GetBoolean(74),
                    DailyCalories1200 = reader.GetBoolean(75),
                    DailyCalories1500 = reader.GetBoolean(76),
                    DailyCalories1800 = reader.GetBoolean(77),
                    DailyCalories2000 = reader.GetBoolean(78),
                    DailyCaloriesOther = reader.GetBoolean(79),
                    DailyCaloriesOtherValue = reader.IsDBNull(80) ? null : reader.GetString(80),

                    // 飲食管理 - 盡量減少 (索引 81-86)
                    ReduceFriedFood = reader.GetBoolean(81),
                    ReduceSweetFood = reader.GetBoolean(82),
                    ReduceSalt = reader.GetBoolean(83),
                    ReduceSugaryDrinks = reader.GetBoolean(84),
                    ReduceOther = reader.GetBoolean(85),
                    ReduceOtherValue = reader.IsDBNull(86) ? null : reader.GetString(86),

                    // 運動建議與資源 (索引 87-90)
                    ExerciseRecommendation = reader.GetBoolean(87),
                    ExerciseGuidance = reader.GetBoolean(88),
                    SocialExerciseResources = reader.GetBoolean(89),
                    SocialExerciseResources_Text = reader.IsDBNull(90) ? null : reader.GetString(90),

                    // 目標設定 (索引 91-93)
                    Achievement = reader.GetBoolean(91),
                    WaistTarget_Value = reader.IsDBNull(92) ? null : reader.GetDecimal(92),
                    WeightTarget_Value = reader.IsDBNull(93) ? null : reader.GetDecimal(93),

                    // 其他叮嚀/目標值 (索引 94-104)
                    OtherReminders = reader.GetBoolean(94),
                    FastingGlucoseTarget = reader.GetBoolean(95),
                    FastingGlucoseTarget_Value = reader.IsDBNull(96) ? null : reader.GetDecimal(96),
                    HbA1cTarget = reader.GetBoolean(97),
                    HbA1cTarget_Value = reader.IsDBNull(98) ? null : reader.GetDecimal(98),
                    TriglyceridesTarget = reader.GetBoolean(99),
                    TriglyceridesTarget_Value = reader.IsDBNull(100) ? null : reader.GetDecimal(100),
                    HDL_CholesterolTarget = reader.GetBoolean(101),
                    HDL_CholesterolTarget_Value = reader.IsDBNull(102) ? null : reader.GetDecimal(102),
                    LDL_CholesterolTarget = reader.GetBoolean(103),
                    LDL_CholesterolTarget_Value = reader.IsDBNull(104) ? null : reader.GetDecimal(104),

                    // 備註 (索引 105)
                    Notes = reader.IsDBNull(105) ? null : reader.GetString(105)
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
               ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"", ""HbA1c"", ""HbA1c_Value"",
               ""HDL"", ""HDL_Value"", ""LDL"", ""LDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
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

                    // 腰圍/血糖/脂質 (索引 19-30)
                    CurrentWaist = reader.GetBoolean(19),
                    CurrentWaist_Value = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                    FastingGlucose = reader.GetBoolean(21),
                    FastingGlucose_Value = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                    HbA1c = reader.GetBoolean(23),
                    HbA1c_Value = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                    HDL = reader.GetBoolean(25),
                    HDL_Value = reader.IsDBNull(26) ? null : reader.GetDecimal(26),
                    LDL = reader.GetBoolean(27),
                    LDL_Value = reader.IsDBNull(28) ? null : reader.GetDecimal(28),
                    Triglycerides = reader.GetBoolean(29),
                    Triglycerides_Value = reader.IsDBNull(30) ? null : reader.GetDecimal(30),

                    // 生活型態 - 運動 (索引 31-33)
                    ExerciseNone = reader.GetBoolean(31),
                    ExerciseUsually = reader.GetBoolean(32),
                    ExerciseAlways = reader.GetBoolean(33),

                    // 生活型態 - 抽菸 (索引 34-37)
                    SmokingNone = reader.GetBoolean(34),
                    SmokingUsually = reader.GetBoolean(35),
                    SmokingUnder10 = reader.GetBoolean(36),
                    SmokingOver10 = reader.GetBoolean(37),

                    // 生活型態 - 檳榔 (索引 38-40)
                    BetelNutNone = reader.GetBoolean(38),
                    BetelNutUsually = reader.GetBoolean(39),
                    BetelNutAlways = reader.GetBoolean(40),

                    // 疾病風險評估 - 冠心病 (索引 41-44)
                    CoronaryHigh = reader.GetBoolean(41),
                    CoronaryMedium = reader.GetBoolean(42),
                    CoronaryLow = reader.GetBoolean(43),
                    CoronaryNotApplicable = reader.GetBoolean(44),

                    // 疾病風險評估 - 糖尿病 (索引 45-48)
                    DiabetesHigh = reader.GetBoolean(45),
                    DiabetesMedium = reader.GetBoolean(46),
                    DiabetesLow = reader.GetBoolean(47),
                    DiabetesNotApplicabe = reader.GetBoolean(48),

                    // 疾病風險評估 - 高血壓 (索引 49-52)
                    HypertensionHigh = reader.GetBoolean(49),
                    HypertensionMedium = reader.GetBoolean(50),
                    HypertensionLow = reader.GetBoolean(51),
                    HypertensionNotApplicable = reader.GetBoolean(52),

                    // 疾病風險評估 - 腦中風 (索引 53-56)
                    StrokeHigh = reader.GetBoolean(53),
                    StrokeMedium = reader.GetBoolean(54),
                    StrokeLow = reader.GetBoolean(55),
                    StrokeNotApplicable = reader.GetBoolean(56),

                    // 疾病風險評估 - 心血管 (索引 57-60)
                    CardiovascularHigh = reader.GetBoolean(57),
                    CardiovascularMedium = reader.GetBoolean(58),
                    CardiovascularLow = reader.GetBoolean(59),
                    CardiovascularNotApplicable = reader.GetBoolean(60),

                    // 戒菸服務 (索引 61-65)
                    SmokingService = reader.GetBoolean(61),
                    SmokingServiceType1 = reader.GetBoolean(62),
                    SmokingServiceType2 = reader.GetBoolean(63),
                    SmokingServiceType2_Provide = reader.GetBoolean(64),
                    SmokingServiceType2_Referral = reader.GetBoolean(65),

                    // 戒檳服務 (索引 66-70)
                    BetelNutService = reader.GetBoolean(66),
                    BetelQuitGoal = reader.GetBoolean(67),
                    BetelQuitYear = reader.IsDBNull(68) ? null : reader.GetInt32(68),
                    BetelQuitMonth = reader.IsDBNull(69) ? null : reader.GetInt32(69),
                    BetelQuitDay = reader.IsDBNull(70) ? null : reader.GetInt32(70),

                    // 口腔檢查 (索引 71-73)
                    OralExam = reader.GetBoolean(71),
                    OralExamYear = reader.IsDBNull(72) ? null : reader.GetInt32(72),
                    OralExamMonth = reader.IsDBNull(73) ? null : reader.GetInt32(73),

                    // 飲食管理 - 每日建議攝取熱量 (索引 74-80)
                    DietManagement = reader.GetBoolean(74),
                    DailyCalories1200 = reader.GetBoolean(75),
                    DailyCalories1500 = reader.GetBoolean(76),
                    DailyCalories1800 = reader.GetBoolean(77),
                    DailyCalories2000 = reader.GetBoolean(78),
                    DailyCaloriesOther = reader.GetBoolean(79),
                    DailyCaloriesOtherValue = reader.IsDBNull(80) ? null : reader.GetString(80),

                    // 飲食管理 - 盡量減少 (索引 81-86)
                    ReduceFriedFood = reader.GetBoolean(81),
                    ReduceSweetFood = reader.GetBoolean(82),
                    ReduceSalt = reader.GetBoolean(83),
                    ReduceSugaryDrinks = reader.GetBoolean(84),
                    ReduceOther = reader.GetBoolean(85),
                    ReduceOtherValue = reader.IsDBNull(86) ? null : reader.GetString(86),

                    // 運動建議與資源 (索引 87-90)
                    ExerciseRecommendation = reader.GetBoolean(87),
                    ExerciseGuidance = reader.GetBoolean(88),
                    SocialExerciseResources = reader.GetBoolean(89),
                    SocialExerciseResources_Text = reader.IsDBNull(90) ? null : reader.GetString(90),

                    // 目標設定 (索引 91-93)
                    Achievement = reader.GetBoolean(91),
                    WaistTarget_Value = reader.IsDBNull(92) ? null : reader.GetDecimal(92),
                    WeightTarget_Value = reader.IsDBNull(93) ? null : reader.GetDecimal(93),

                    // 其他叮嚀/目標值 (索引 94-104)
                    OtherReminders = reader.GetBoolean(94),
                    FastingGlucoseTarget = reader.GetBoolean(95),
                    FastingGlucoseTarget_Value = reader.IsDBNull(96) ? null : reader.GetDecimal(96),
                    HbA1cTarget = reader.GetBoolean(97),
                    HbA1cTarget_Value = reader.IsDBNull(98) ? null : reader.GetDecimal(98),
                    TriglyceridesTarget = reader.GetBoolean(99),
                    TriglyceridesTarget_Value = reader.IsDBNull(100) ? null : reader.GetDecimal(100),
                    HDL_CholesterolTarget = reader.GetBoolean(101),
                    HDL_CholesterolTarget_Value = reader.IsDBNull(102) ? null : reader.GetDecimal(102),
                    LDL_CholesterolTarget = reader.GetBoolean(103),
                    LDL_CholesterolTarget_Value = reader.IsDBNull(104) ? null : reader.GetDecimal(104),

                    // 備註 (索引 105)
                    Notes = reader.IsDBNull(105) ? null : reader.GetString(105)
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
                ""CurrentWaist"", ""CurrentWaist_Value"", ""FastingGlucose"", ""FastingGlucose_Value"", ""HbA1c"", ""HbA1c_Value"",
                ""HDL"", ""HDL_Value"", ""LDL"", ""LDL_Value"", ""Triglycerides"", ""Triglycerides_Value"",
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
                @CurrentWaist, @CurrentWaist_Value, @FastingGlucose, @FastingGlucose_Value, @HbA1c, @HbA1c_Value,
                @HDL, @HDL_Value, @LDL, @LDL_Value, @Triglycerides, @Triglycerides_Value,
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
                command.Parameters.AddWithValue("@HbA1c", model.HbA1c);
                command.Parameters.AddWithValue("@HbA1c_Value", model.HbA1c_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@HDL", model.HDL);
                command.Parameters.AddWithValue("@HDL_Value", model.HDL_Value ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@LDL", model.LDL);
                command.Parameters.AddWithValue("@LDL_Value", model.LDL_Value ?? (object)DBNull.Value);
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
                    ""HbA1c"" = @HbA1c, ""HbA1c_Value"" = @HbA1c_Value,
                    ""HDL"" = @HDL, ""HDL_Value"" = @HDL_Value,
                    ""LDL"" = @LDL, ""LDL_Value"" = @LDL_Value,
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
            command.Parameters.AddWithValue("@HbA1c", model.HbA1c);
            command.Parameters.AddWithValue("@HbA1c_Value", model.HbA1c_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@HDL", model.HDL);
            command.Parameters.AddWithValue("@HDL_Value", model.HDL_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@LDL", model.LDL);
            command.Parameters.AddWithValue("@LDL_Value", model.LDL_Value ?? (object)DBNull.Value);
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

        /// <summary>
        /// 🎯 修正後的 Helper: 取得所有符合條件的未填寫紀錄，並包含 CaseManagement 中的性別/生日/722狀態。
        /// </summary>
        private async Task<List<MissedRecordViewModel>> GetMissedRecordsAndCaseInfoAsync(string searchIdNumber = null, DateTime? dateToCheck = null)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection")
                + ";SSL Mode=Require;Trust Server Certificate=True;";

            var allMissedRecords = new List<MissedRecordViewModel>();

            // 🎯 修正 1: 定義計算基準日
            DateTime endDate = dateToCheck?.Date ?? DateTime.Today.Date;

            using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            // 修正 SQL: 合併 Users, Today 和 CaseManagement 的最新紀錄
            string sql = @"
                SELECT 
                    u.""Id"" as UserId, u.""IDNumber"", u.""FullName"", u.""PhoneNumber"",
                    MAX(t.""RecordDate"") FILTER (WHERE t.""IsReminderRecord"" = FALSE) as LastRecordDate,
                    c.""Gender"", c.""BirthDate"", c.""BloodPressureGuidance722""
                FROM public.""Users"" u
                LEFT JOIN public.""Today"" t ON u.""Id"" = t.""UserId"" 
                    AND t.""IsReminderRecord"" = FALSE
                LEFT JOIN (
                    SELECT DISTINCT ON (""IDNumber"") *
                    FROM public.""CaseManagement""
                    ORDER BY ""IDNumber"", ""AssessmentDate"" DESC
                ) c ON u.""IDNumber"" = c.""IDNumber""
                WHERE u.""Role"" = 'Patient' 
                    AND u.""IsActive"" = TRUE
                    AND (@searchIdNumber IS NULL OR u.""IDNumber"" ILIKE '%' || @searchIdNumber || '%')
                GROUP BY u.""Id"", u.""IDNumber"", u.""FullName"", u.""PhoneNumber"", c.""Gender"", c.""BirthDate"", c.""BloodPressureGuidance722""
                ORDER BY u.""Id""
            ";

            using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@searchIdNumber", NpgsqlTypes.NpgsqlDbType.Text).Value = (object)searchIdNumber ?? DBNull.Value;
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    int userId = reader.GetInt32(0);
                    string idNumber = reader.GetString(1);
                    string fullName = reader.GetString(2);
                    string phoneNumber = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    DateTime? lastRecordDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
                    string gender = reader.IsDBNull(5) ? "" : reader.GetString(5);
                    DateTime birthDate = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6);
                    bool is722Tracking = reader.IsDBNull(7) ? false : reader.GetBoolean(7);

                    int missedDays = 0;
                    if (lastRecordDate.HasValue)
                    {
                        // 🎯 修正 2: 使用 endDate (檢查日) 計算 MissedDays
                        missedDays = (endDate - lastRecordDate.Value.Date).Days;
                    }
                    else
                    {
                        missedDays = 999;
                    }

                    // 檢查身分證字號性別 (用於補足 CaseManagement 缺失的情況)
                    if (string.IsNullOrEmpty(gender) && idNumber.Length >= 2)
                    {
                        char secondChar = idNumber[1];
                        gender = secondChar == '1' ? "男" : secondChar == '2' ? "女" : "";
                    }

                    // 獲取最近一次未填寫原因 (與 MissedDays 計算無關，獨立查詢)
                    string missedReason = await GetLatestMissedReasonAsync(userId, connStr);

                    allMissedRecords.Add(new MissedRecordViewModel
                    {
                        UserId = userId,
                        IDNumber = idNumber,
                        FullName = fullName,
                        PhoneNumber = phoneNumber,
                        Gender = gender,
                        BirthDate = birthDate,
                        LastRecordDate = lastRecordDate,
                        MissedDays = missedDays,
                        MissedReason = missedReason,
                        Is722Tracking = is722Tracking
                    });
                }
            }

            // 返回 MissedDays >= 2 或正在追蹤 722 的個案
            return allMissedRecords.Where(r => r.MissedDays >= 2 || r.Is722Tracking).ToList();
        }

        /// <summary>
        /// 取得所有需要 722 追蹤的個案，並檢查其當天的血壓填寫狀況
        /// </summary>
        private async Task<List<MissedRecordViewModel>> Get722TrackingListAsync(List<MissedRecordViewModel> trackingCandidates, DateTime checkDate)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection")
                + ";SSL Mode=Require;Trust Server Certificate=True;";

            var userIds = trackingCandidates.Select(x => x.UserId).ToArray();
            if (!userIds.Any()) return new List<MissedRecordViewModel>();

            // 🎯 修改查詢：除了檢查是否有記錄，還要取得實際血壓值
            string todaySql = @"
        SELECT 
            ""UserId"",
            BOOL_OR(""BP_First_1_Systolic"" IS NOT NULL OR ""BP_First_2_Systolic"" IS NOT NULL OR ""BP_Morning_NotMeasured"" = TRUE) AS HasMorningRecord,
            BOOL_OR(""BP_Second_1_Systolic"" IS NOT NULL OR ""BP_Second_2_Systolic"" IS NOT NULL OR ""BP_Evening_NotMeasured"" = TRUE) AS HasEveningRecord,
            MAX(""BP_First_1_Systolic"") FILTER (WHERE ""BP_First_1_Systolic"" IS NOT NULL) AS MorningSystolic1,
            MAX(""BP_First_1_Diastolic"") FILTER (WHERE ""BP_First_1_Diastolic"" IS NOT NULL) AS MorningDiastolic1,
            MAX(""BP_First_2_Systolic"") FILTER (WHERE ""BP_First_2_Systolic"" IS NOT NULL) AS MorningSystolic2,
            MAX(""BP_First_2_Diastolic"") FILTER (WHERE ""BP_First_2_Diastolic"" IS NOT NULL) AS MorningDiastolic2,
            MAX(""BP_Second_1_Systolic"") FILTER (WHERE ""BP_Second_1_Systolic"" IS NOT NULL) AS EveningSystolic1,
            MAX(""BP_Second_1_Diastolic"") FILTER (WHERE ""BP_Second_1_Diastolic"" IS NOT NULL) AS EveningDiastolic1,
            MAX(""BP_Second_2_Systolic"") FILTER (WHERE ""BP_Second_2_Systolic"" IS NOT NULL) AS EveningSystolic2,
            MAX(""BP_Second_2_Diastolic"") FILTER (WHERE ""BP_Second_2_Diastolic"" IS NOT NULL) AS EveningDiastolic2
        FROM public.""Today""
        WHERE ""UserId"" = ANY(@UserIds) AND ""RecordDate"" = @Today
        GROUP BY ""UserId"";
    ";

            var todayStatus = new Dictionary<int, (bool HasMorning, bool HasEvening,
                decimal? MorningSys1, decimal? MorningDia1, decimal? MorningSys2, decimal? MorningDia2,
                decimal? EveningSys1, decimal? EveningDia1, decimal? EveningSys2, decimal? EveningDia2)>();

            using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            using var todayCmd = new NpgsqlCommand(todaySql, conn);
            todayCmd.Parameters.AddWithValue("@UserIds", userIds);
            todayCmd.Parameters.AddWithValue("@Today", checkDate);

            using (var todayReader = await todayCmd.ExecuteReaderAsync())
            {
                while (await todayReader.ReadAsync())
                {
                    int userId = todayReader.GetInt32(0);

                    // 安全地讀取 boolean 值
                    bool hasMorning = false;
                    bool hasEvening = false;

                    if (!todayReader.IsDBNull(1))
                        hasMorning = todayReader.GetBoolean(1);

                    if (!todayReader.IsDBNull(2))
                        hasEvening = todayReader.GetBoolean(2);

                    // 讀取血壓值
                    decimal? morningSys1 = todayReader.IsDBNull(3) ? null : todayReader.GetDecimal(3);
                    decimal? morningDia1 = todayReader.IsDBNull(4) ? null : todayReader.GetDecimal(4);
                    decimal? morningSys2 = todayReader.IsDBNull(5) ? null : todayReader.GetDecimal(5);
                    decimal? morningDia2 = todayReader.IsDBNull(6) ? null : todayReader.GetDecimal(6);
                    decimal? eveningSys1 = todayReader.IsDBNull(7) ? null : todayReader.GetDecimal(7);
                    decimal? eveningDia1 = todayReader.IsDBNull(8) ? null : todayReader.GetDecimal(8);
                    decimal? eveningSys2 = todayReader.IsDBNull(9) ? null : todayReader.GetDecimal(9);
                    decimal? eveningDia2 = todayReader.IsDBNull(10) ? null : todayReader.GetDecimal(10);

                    todayStatus.Add(userId, (
                        HasMorning: hasMorning,
                        HasEvening: hasEvening,
                        MorningSys1: morningSys1,
                        MorningDia1: morningDia1,
                        MorningSys2: morningSys2,
                        MorningDia2: morningDia2,
                        EveningSys1: eveningSys1,
                        EveningDia1: eveningDia1,
                        EveningSys2: eveningSys2,
                        EveningDia2: eveningDia2
                    ));
                }
            }

            // 更新缺失狀態和血壓值
            foreach (var item in trackingCandidates)
            {
                if (todayStatus.TryGetValue(item.UserId, out var status))
                {
                    // 設定缺失狀態
                    item.IsMorningMissing = !status.HasMorning;
                    item.IsEveningMissing = !status.HasEvening;

                    // 🆕 設定血壓值
                    item.MorningSystolic1 = status.MorningSys1;
                    item.MorningDiastolic1 = status.MorningDia1;
                    item.MorningSystolic2 = status.MorningSys2;
                    item.MorningDiastolic2 = status.MorningDia2;
                    item.EveningSystolic1 = status.EveningSys1;
                    item.EveningDiastolic1 = status.EveningDia1;
                    item.EveningSystolic2 = status.EveningSys2;
                    item.EveningDiastolic2 = status.EveningDia2;
                }
                else
                {
                    // 沒有紀錄 -> 標記為完全缺失
                    item.IsMorningMissing = true;
                    item.IsEveningMissing = true;
                }
                item.IsBothMissing = item.IsMorningMissing && item.IsEveningMissing;
            }

            return trackingCandidates.Where(r => r.Is722Tracking).ToList();
        }

        /// <summary>
        /// 取得最近一次未填寫原因 (從 Today 表)
        /// </summary>
        private async Task<string> GetLatestMissedReasonAsync(int userId, string connStr)
        {
            string reasonSql = @"
                SELECT ""MissedReason""
                FROM public.""Today""
                WHERE ""UserId"" = @userId 
                    AND ""IsReminderRecord"" = TRUE
                    AND ""MissedReason"" IS NOT NULL
                    AND ""MissedReason"" != ''
                ORDER BY ""RecordDate"" DESC
                LIMIT 1
            ";

            using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            using var cmd = new NpgsqlCommand(reasonSql, conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            var result = await cmd.ExecuteScalarAsync();

            return result != null && result != DBNull.Value ? result.ToString() : "";
        }

    }
}