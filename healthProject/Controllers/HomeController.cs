using healthProject.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;


namespace healthProject.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
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
