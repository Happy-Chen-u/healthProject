using Microsoft.AspNetCore.Mvc;

namespace healthProject.Controllers
{
    // 帳號管理
    public class AccountController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
