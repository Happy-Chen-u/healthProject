using Microsoft.AspNetCore.Mvc;

namespace healthProject.Controllers
{
    // 疾病管理紀錄表
    public class DiseaseRecordController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
