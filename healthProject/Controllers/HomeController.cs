using healthProject.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration; // 用來讀取 appsettings.json
using Npgsql; // ? PostgreSQL 的連線套件

namespace healthProject.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration; // 用來存取連線字串

        // 透過建構子注入 ILogger 和 IConfiguration
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
        public IActionResult Dashboard()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                // ? 使用 PostgreSQL 連線
                using (var connection = new NpgsqlConnection(connectionString))
                {
                    connection.Open();

                    // ?? 這裡可以撈資料，例如：
                    // string query = "SELECT COUNT(*) FROM CaseManagement";
                    // using (var command = new NpgsqlCommand(query, connection))
                    // {
                    //     var count = (long)command.ExecuteScalar();
                    //     ViewBag.CaseCount = count;
                    // }

                    _logger.LogInformation("成功連線到 PostgreSQL 資料庫");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "連線 PostgreSQL 資料庫時發生錯誤");
            }

            return View();
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
