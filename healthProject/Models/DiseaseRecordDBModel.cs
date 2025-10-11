using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    // 疾病管理紀錄（醫師/管理者/醫院端使用）
    public class DiseaseRecordDBModel : Controller
    {
        public IActionResult Index()
        {
            return View();
        }


        public int RecordId { get; set; }
        public int UserId { get; set; }
        public DateTime RecordDate { get; set; }
        public string DiseaseName { get; set; }
        public string Symptoms { get; set; }
        public string Treatment { get; set; }
        public string Note { get; set; }

        public virtual UserDBModel User { get; set; }
    }
}
