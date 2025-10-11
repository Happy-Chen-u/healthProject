using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    // 個案每日上傳健康資訊(內容待改)
    public class HealthRecordDBModel : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public int HealthRecordId { get; set; }
        public int UserId { get; set; }
        public DateTime RecordDate { get; set; }

        // 可依需求擴充
        public decimal BodyTemperature { get; set; }
        public int SystolicBP { get; set; }     // 收縮壓
        public int DiastolicBP { get; set; }    // 舒張壓
        public decimal BloodSugar { get; set; } // 血糖
        public string Remark { get; set; }

        public virtual UserDBModel User { get; set; }
    }
}
