using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System.Data;

namespace healthProject.Controllers
{
    // 疾病管理紀錄表
    public class CaseManagementController : Controller
    {
        private readonly IConfiguration _configuration;

        public CaseManagementController(IConfiguration configuration)
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
