using healthProject.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient; // ← 加這個，用來開資料庫連線
using Microsoft.Extensions.Configuration; // ← 加這個，用來讀取設定檔

namespace healthProject.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration; // ← 新增 IConfiguration 欄位

        // 透過建構子注入 IConfiguration
        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration; // ← 儲存設定檔物件
        }

        // 首頁 - 未登入使用者看到的頁面
        public IActionResult Index()
        {
            // 如果已經登入，直接導向 Dashboard
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Dashboard");
            }

            return View();
        }

        // Dashboard - 登入後的主要功能選單
        [Authorize]
        public IActionResult Dashboard()
        {
            // ?? 範例：如果你未來要從資料庫撈東西，可以這樣寫：
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // 這裡可以撈資料，例如 SELECT COUNT(*) FROM CaseManagement
                // 暫時留空，示範如何建立連線即可
            }

            return View();
        }

        // Privacy 頁面（如果需要）
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
