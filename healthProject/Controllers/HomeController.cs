using healthProject.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient; // �� �[�o�ӡA�ΨӶ}��Ʈw�s�u
using Microsoft.Extensions.Configuration; // �� �[�o�ӡA�Ψ�Ū���]�w��

namespace healthProject.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration; // �� �s�W IConfiguration ���

        // �z�L�غc�l�`�J IConfiguration
        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration; // �� �x�s�]�w�ɪ���
        }

        // ���� - ���n�J�ϥΪ̬ݨ쪺����
        public IActionResult Index()
        {
            // �p�G�w�g�n�J�A�����ɦV Dashboard
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Dashboard");
            }

            return View();
        }

        // Dashboard - �n�J�᪺�D�n�\����
        [Authorize]
        public IActionResult Dashboard()
        {
            // ?? �d�ҡG�p�G�A���ӭn�q��Ʈw���F��A�i�H�o�˼g�G
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // �o�̥i�H����ơA�Ҧp SELECT COUNT(*) FROM CaseManagement
                // �Ȯɯd�šA�ܽd�p��إ߳s�u�Y�i
            }

            return View();
        }

        // Privacy �����]�p�G�ݭn�^
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
