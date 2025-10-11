using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    public class UserViewModel : Controller
    {
        // 顯示個案資訊
        public IActionResult Index()
        {
            return View();
        }

        public int UserId { get; set; }
        public string IdNumber { get; set; }  // 身分證字號
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Role { get; set; } // "Case" 或 "Admin"

        public virtual ICollection<DiseaseRecordDBModel> DiseaseRecords { get; set; }
        public virtual ICollection<HealthRecordDBModel> HealthRecords { get; set; }
    }
}
