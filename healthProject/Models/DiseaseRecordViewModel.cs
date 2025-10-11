using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    // 顯示疾病管理紀錄資訊
    public class DiseaseRecordViewModel : Controller
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
