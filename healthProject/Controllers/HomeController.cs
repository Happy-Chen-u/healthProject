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
                    // ✅ 加入 log 查看 Session
                    var lastViewed = HttpContext.Session.GetString("LastViewedMissedRecords");
                    _logger.LogInformation($"📌 Dashboard - LastViewedMissedRecords = {lastViewed ?? "NULL"}");

                    ViewBag.HasMissedRecords = await CheckHasNewMissedRecordsAsync();

                    _logger.LogInformation($"📌 Dashboard - HasMissedRecords = {ViewBag.HasMissedRecords}");
                }

                _logger.LogInformation("成功載入 Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "載入 Dashboard 時發生錯誤");
            }

            return View();
        }


        // 檢查是否有新的未填寫記錄(管理者未查看過的)
        private async Task<bool> CheckHasNewMissedRecordsAsync()
        {
            try
            {
                var connStr = _configuration.GetConnectionString("DefaultConnection");
                using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // 取得管理者最後查看時間
                DateTime? lastViewedTime = null;

                if (HttpContext.Session.Keys.Contains("LastViewedMissedRecords"))
                {
                    var timeStr = HttpContext.Session.GetString("LastViewedMissedRecords");
                    if (DateTime.TryParse(timeStr, out DateTime parsed))
                    {
                        lastViewedTime = parsed;
                        _logger.LogInformation($"✅ 管理者上次查看時間: {lastViewedTime:yyyy-MM-dd HH:mm:ss}");
                    }
                }

                // ✅ 如果管理者「今天」已經查看過,就不顯示紅點
                if (lastViewedTime.HasValue && lastViewedTime.Value.Date == DateTime.Today)
                {
                    _logger.LogInformation("✅ 管理者今天已查看過,不顯示紅點");
                    return false;
                }

                // ✅ 否則,只要有未填寫超過兩天的記錄,就顯示紅點
                string sql = @"
            WITH LastRecords AS (
                SELECT 
                    ""UserId"",
                    MAX(""RecordDate"") AS lastrecorddate
                FROM ""Today""
                WHERE ""IsReminderRecord"" = FALSE
                GROUP BY ""UserId""
            )
            SELECT COUNT(*)
            FROM ""Users"" u
            LEFT JOIN LastRecords lr ON u.""Id"" = lr.""UserId""
            WHERE 
                u.""Role"" = 'Patient'
                AND u.""IsActive"" = TRUE
                AND (
                    lr.lastrecorddate IS NULL 
                    OR lr.lastrecorddate <= @TwoDaysAgo
                );
        ";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@TwoDaysAgo", DateTime.Today.AddDays(-2));

                var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

                bool hasRecords = count > 0;
                _logger.LogInformation($"📌 未填寫超過2天的個案數: {count}, 顯示紅點: {hasRecords}");

                return hasRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "檢查未讀未填寫記錄錯誤");
                return false;
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