using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    // 顯示個案上傳健康資訊（和DB內容一樣 還須更改）
    public class HealthRecordViewModel : Controller
    {
        public IActionResult Index()
        {
            return View();

        }

        public DateTime RecordDate { get; set; }
        public decimal BodyTemperature { get; set; }
        public int SystolicBP { get; set; }
        public int DiastolicBP { get; set; }
        public decimal BloodSugar { get; set; }
        public string Remark { get; set; }
    }
}
