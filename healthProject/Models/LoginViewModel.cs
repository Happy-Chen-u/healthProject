using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    // 登入畫面（共用給個案與管理者）
    public class LoginViewModel : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public string IdNumber { get; set; }
        public string Password { get; set; }

    }
}
