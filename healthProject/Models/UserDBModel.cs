using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    // 儲存帳號、角色、身分證字號等基本資料
    public class UserDBModel : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

      
            public int UserId { get; set; }
            public string IdNumber { get; set; }  // 身分證字號
            public string UserName { get; set; }
            public string Password { get; set; }
            public string Role { get; set; } // "Case" 或 "Admin"

            public virtual ICollection<CaseManagementDBModel> DiseaseRecords { get; set; }
            public virtual ICollection<HealthRecordDBModel> HealthRecords { get; set; }
        

    }
}
