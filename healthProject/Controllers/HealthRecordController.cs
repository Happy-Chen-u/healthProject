using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System.Data;

namespace healthProject.Controllers
{
    public class HealthRecordController : Controller
    {
        private readonly IConfiguration _configuration;

        // 透過建構子注入 appsettings.json 的設定
        public HealthRecordController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

           

            return View();
        }
    }
}

