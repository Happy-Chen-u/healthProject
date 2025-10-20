using healthProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        userId = patient.Id,
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
                       ""AssessmentDate"", ""FollowUpDate""
                FROM ""CaseManagement""
                ORDER BY ""AssessmentDate"" DESC
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
                    AssessmentDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    FollowUpDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                });
            }

            return records;
        }


        // 根據 ID 取得紀錄
        private async Task<CaseManagementViewModel> GetRecordByIdAsync(int id)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // 這裡需要查詢所有欄位,因為欄位太多,先只查基本欄位
            var query = @"
                SELECT ""Id"", ""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"",
                       ""Height"", ""Weight"", ""BMI"", ""BMI_Value"",
                       ""AssessmentDate"", ""FollowUpDate""
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
                    FollowUpDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11)
                };
            }

            return null;
        }


        // 儲存新紀錄 (簡化版 - 只儲存基本欄位)
        private async Task SaveRecordAsync(CaseManagementViewModel model)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                INSERT INTO ""CaseManagement"" 
                (""UserId"", ""IDNumber"", ""Name"", ""Gender"", ""BirthDate"", 
                 ""Height"", ""Weight"", ""BMI"", ""BMI_Value"", 
                 ""AssessmentDate"", ""FollowUpDate"")
                VALUES 
                (@UserId, @IDNumber, @Name, @Gender, @BirthDate,
                 @Height, @Weight, @BMI, @BMI_Value,
                 @AssessmentDate, @FollowUpDate)";

            await using var command = new NpgsqlCommand(query, connection);
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

            await command.ExecuteNonQueryAsync();
        }

        // 更新紀錄
        private async Task UpdateRecordAsync(CaseManagementViewModel model)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                UPDATE ""CaseManagement""
                SET ""Name"" = @Name, ""Gender"" = @Gender, ""BirthDate"" = @BirthDate,
                    ""Height"" = @Height, ""Weight"" = @Weight, 
                    ""BMI"" = @BMI, ""BMI_Value"" = @BMI_Value,
                    ""AssessmentDate"" = @AssessmentDate, ""FollowUpDate"" = @FollowUpDate
                WHERE ""Id"" = @Id";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", model.Id);
            command.Parameters.AddWithValue("@Name", model.Name);
            command.Parameters.AddWithValue("@Gender", model.Gender);
            command.Parameters.AddWithValue("@BirthDate", model.BirthDate);
            command.Parameters.AddWithValue("@Height", model.Height);
            command.Parameters.AddWithValue("@Weight", model.Weight);
            command.Parameters.AddWithValue("@BMI", model.BMI);
            command.Parameters.AddWithValue("@BMI_Value", model.BMI_Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@AssessmentDate", model.AssessmentDate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@FollowUpDate", model.FollowUpDate ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
    }
}

