using healthProject.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Security.Claims;

namespace healthProject.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // 首頁（未登入使用者看到的頁面）
        public IActionResult Index()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Dashboard");
            }

            return View();
        }

        // Dashboard（登入後主畫面）
        [Authorize]
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                // 🆕 如果是管理者,檢查是否有新的未填寫記錄
                if (User.IsInRole("Admin"))
                {
                    ViewBag.HasMissedRecords = await CheckHasNewMissedRecordsAsync();
                }

                _logger.LogInformation("成功載入 Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "載入 Dashboard 時發生錯誤");
            }

            return View();
        }

        // 🆕 檢查是否有新的未填寫記錄（管理者未查看過的）
        private async Task<bool> CheckHasNewMissedRecordsAsync()
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection")
                    + ";SSL Mode=Require;Trust Server Certificate=True;";

                using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // 取得管理者最後查看時間
                DateTime lastViewedTime = DateTime.MinValue;

                if (HttpContext.Session.Keys.Contains("LastViewedMissedRecords"))
                {
                    var timeStr = HttpContext.Session.GetString("LastViewedMissedRecords");
                    if (DateTime.TryParse(timeStr, out DateTime parsed))
                        lastViewedTime = parsed;
                }

                // 查詢「未填寫超過兩天」的個案的最後填寫日期
                string sql = @"
            WITH LastRecords AS (
                SELECT 
                    ""UserId"",
                    MAX(""RecordDate"") AS LastRecordDate
                FROM ""Today""
                WHERE ""IsReminderRecord"" = FALSE
                GROUP BY ""UserId""
            )
            SELECT 
                u.""Id"",
                lr.""LastRecordDate""
            FROM ""Users"" u
            LEFT JOIN LastRecords lr ON u.""Id"" = lr.""UserId""
            WHERE 
                u.""Role"" = 'Patient'
                AND u.""IsActive"" = TRUE
                AND (
                    lr.""LastRecordDate"" IS NULL 
                    OR lr.""LastRecordDate"" <= @TwoDaysAgo
                );
        ";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@TwoDaysAgo", DateTime.Today.AddDays(-2));

                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    DateTime? lastRecordDate = reader.IsDBNull(1) ? null : reader.GetDateTime(1);

                    // 如果該個案的「未填寫狀態」發生在管理者已讀時間之後 → 顯示紅點
                    if (lastRecordDate == null || lastRecordDate.Value < lastViewedTime)
                    {
                        return true;
                    }
                }

                return false; // 無新未填寫記錄
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "檢查未讀未填寫記錄錯誤");
                return false;
            }
        }


        // 🆕 清除未讀提醒（管理者點擊「個案填寫狀況」後呼叫）
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult ClearMissedRecordsAlert()
        {
            try
            {
                // 記錄管理者查看的時間
                HttpContext.Session.SetString("LastViewedMissedRecords", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("管理者已查看未填寫記錄頁面");
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除提醒狀態失敗");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // 隱私頁面
        public IActionResult Privacy()
        {
            return View();
        }

        // 錯誤頁面
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}