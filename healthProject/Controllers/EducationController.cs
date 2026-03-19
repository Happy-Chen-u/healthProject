using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace healthProject.Controllers
{
    [Authorize(Roles = "Admin")]
    public class EducationController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EducationController> _logger;

        public EducationController(IConfiguration configuration, ILogger<EducationController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // ========================================
        // 列表（含分類篩選）
        // ========================================
        public async Task<IActionResult> Index(string category = "")
        {
            var items = await GetEducationListAsync(category);
            ViewBag.Category = category;
            ViewBag.Categories = await GetCategoriesAsync();
            return View(items);
        }

        // ========================================
        // 新增頁面
        // ========================================
        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = await GetCategoriesAsync();
            return View(new HealthEducationModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HealthEducationModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await GetCategoriesAsync();
                return View(model);
            }

            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // ✅ 自動計算該分類的下一個排序號
                var sortCmd = new NpgsqlCommand(@"
            SELECT COALESCE(MAX(""SortOrder""), 0) + 1
            FROM ""HealthEducation""
            WHERE ""Category"" = @Category", conn);
                sortCmd.Parameters.AddWithValue("@Category", model.Category);
                var nextSort = Convert.ToInt32(await sortCmd.ExecuteScalarAsync());

                var cmd = new NpgsqlCommand(@"
            INSERT INTO ""HealthEducation"" (""Title"", ""Content"", ""Category"", ""SortOrder"", ""IsActive"")
            VALUES (@Title, @Content, @Category, @SortOrder, @IsActive)", conn);

                cmd.Parameters.AddWithValue("@Title", model.Title);
                cmd.Parameters.AddWithValue("@Content", model.Content);
                cmd.Parameters.AddWithValue("@Category", model.Category);
                cmd.Parameters.AddWithValue("@SortOrder", nextSort);  // ✅ 自動排序
                cmd.Parameters.AddWithValue("@IsActive", model.IsActive);

                await cmd.ExecuteNonQueryAsync();

                TempData["Success"] = "衛教內容已新增成功！";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "新增衛教內容失敗");
                ModelState.AddModelError("", "新增失敗，請稍後再試。");
                ViewBag.Categories = await GetCategoriesAsync();
                return View(model);
            }
        }

        // ========================================
        // 編輯頁面
        // ========================================
        public async Task<IActionResult> Edit(int id)
        {
            var item = await GetEducationByIdAsync(id);
            if (item == null) return NotFound();
            ViewBag.Categories = await GetCategoriesAsync();
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(HealthEducationModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await GetCategoriesAsync();
                return View(model);
            }

            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var cmd = new NpgsqlCommand(@"
                    UPDATE ""HealthEducation""
                    SET ""Title"" = @Title,
                        ""Content"" = @Content,
                        ""Category"" = @Category,
                        ""SortOrder"" = @SortOrder,
                        ""IsActive"" = @IsActive,
                        ""UpdatedAt"" = NOW()
                    WHERE ""Id"" = @Id", conn);

                cmd.Parameters.AddWithValue("@Id", model.Id);
                cmd.Parameters.AddWithValue("@Title", model.Title);
                cmd.Parameters.AddWithValue("@Content", model.Content);
                cmd.Parameters.AddWithValue("@Category", model.Category);
                cmd.Parameters.AddWithValue("@SortOrder", model.SortOrder);
                cmd.Parameters.AddWithValue("@IsActive", model.IsActive);

                await cmd.ExecuteNonQueryAsync();

                TempData["Success"] = "衛教內容已更新成功！";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新衛教內容失敗");
                ModelState.AddModelError("", "更新失敗，請稍後再試。");
                ViewBag.Categories = await GetCategoriesAsync();
                return View(model);
            }
        }

        // ========================================
        // 啟用 / 停用（AJAX）
        // ========================================
        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var cmd = new NpgsqlCommand(@"
                    UPDATE ""HealthEducation""
                    SET ""IsActive"" = NOT ""IsActive"", ""UpdatedAt"" = NOW()
                    WHERE ""Id"" = @Id
                    RETURNING ""IsActive""", conn);

                cmd.Parameters.AddWithValue("@Id", id);
                var result = await cmd.ExecuteScalarAsync();

                return Json(new { success = true, isActive = (bool)result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切換啟用狀態失敗");
                return Json(new { success = false });
            }
        }

        // ========================================
        // 刪除（AJAX）
        // ========================================
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // 先刪推播紀錄，再刪內容
                var delLog = new NpgsqlCommand(@"DELETE FROM ""EducationSentLog"" WHERE ""EducationId"" = @Id", conn);
                delLog.Parameters.AddWithValue("@Id", id);
                await delLog.ExecuteNonQueryAsync();

                var delEdu = new NpgsqlCommand(@"DELETE FROM ""HealthEducation"" WHERE ""Id"" = @Id", conn);
                delEdu.Parameters.AddWithValue("@Id", id);
                await delEdu.ExecuteNonQueryAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除衛教內容失敗");
                return Json(new { success = false });
            }
        }

        // ========================================
        // 統計資訊 API（各分類則數）
        // ========================================
        [HttpGet]
        public async Task<IActionResult> Stats()
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var cmd = new NpgsqlCommand(@"
                    SELECT ""Category"",
                           COUNT(*) AS total,
                           SUM(CASE WHEN ""IsActive"" THEN 1 ELSE 0 END) AS active
                    FROM ""HealthEducation""
                    GROUP BY ""Category""
                    ORDER BY ""Category""", conn);

                var stats = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    stats.Add(new
                    {
                        category = reader.GetString(0),
                        total = reader.GetInt64(1),
                        active = reader.GetInt64(2)
                    });
                }

                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得統計失敗");
                return Json(new List<object>());
            }
        }

        // ========================================
        // 資料庫輔助方法
        // ========================================
        private async Task<List<HealthEducationModel>> GetEducationListAsync(string category)
        {
            var list = new List<HealthEducationModel>();
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            var sql = string.IsNullOrEmpty(category)
                ? @"SELECT * FROM ""HealthEducation"" ORDER BY ""Category"" ASC, ""SortOrder"" ASC"
                : @"SELECT * FROM ""HealthEducation"" WHERE ""Category"" = @Category ORDER BY ""SortOrder"" ASC";

            await using var cmd = new NpgsqlCommand(sql, conn);
            if (!string.IsNullOrEmpty(category))
                cmd.Parameters.AddWithValue("@Category", category);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new HealthEducationModel
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Content = reader.GetString(reader.GetOrdinal("Content")),
                    Category = reader.GetString(reader.GetOrdinal("Category")),
                    SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                });
            }

            return list;
        }

        private async Task<HealthEducationModel> GetEducationByIdAsync(int id)
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT * FROM ""HealthEducation"" WHERE ""Id"" = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new HealthEducationModel
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Content = reader.GetString(reader.GetOrdinal("Content")),
                    Category = reader.GetString(reader.GetOrdinal("Category")),
                    SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
                };
            }

            return null;
        }

        private async Task<List<string>> GetCategoriesAsync()
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT DISTINCT ""Category"" FROM ""HealthEducation"" ORDER BY ""Category""", conn);

            var categories = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                categories.Add(reader.GetString(0));

            return categories;
        }
    }

    // ========================================
    // Model（加到 Models 資料夾）
    // ========================================
    public class HealthEducationModel
    {
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "請輸入標題")]
        [System.ComponentModel.DataAnnotations.MaxLength(100)]
        public string Title { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "請輸入內容")]
        public string Content { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "請選擇分類")]
        public string Category { get; set; }

        public int SortOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}