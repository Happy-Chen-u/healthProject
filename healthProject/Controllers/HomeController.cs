using healthProject.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration; // �Ψ�Ū�� appsettings.json
using Npgsql; // ? PostgreSQL ���s�u�M��

namespace healthProject.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration; // �ΨӦs���s�u�r��

        // �z�L�غc�l�`�J ILogger �M IConfiguration
        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // �����]���n�J�ϥΪ̬ݨ쪺�����^
        public IActionResult Index()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Dashboard");
            }

            return View();
        }

        // Dashboard�]�n�J��D�e���^
        [Authorize]
        public IActionResult Dashboard()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                // ? �ϥ� PostgreSQL �s�u
                using (var connection = new NpgsqlConnection(connectionString))
                {
                    connection.Open();

                    // ?? �o�̥i�H����ơA�Ҧp�G
                    // string query = "SELECT COUNT(*) FROM CaseManagement";
                    // using (var command = new NpgsqlCommand(query, connection))
                    // {
                    //     var count = (long)command.ExecuteScalar();
                    //     ViewBag.CaseCount = count;
                    // }

                    _logger.LogInformation("���\�s�u�� PostgreSQL ��Ʈw");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�s�u PostgreSQL ��Ʈw�ɵo�Ϳ��~");
            }

            return View();
        }

        // ���p����
        public IActionResult Privacy()
        {
            return View();
        }

        // ���~����
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
