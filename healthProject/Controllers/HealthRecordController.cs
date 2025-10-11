using Microsoft.AspNetCore.Mvc;

namespace healthProject.Controllers
{
    public class HealthRecordController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
