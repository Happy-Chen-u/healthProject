using Microsoft.AspNetCore.Mvc;

namespace healthProject.Models
{
    // 儲存帳號、角色、身分證字號等基本資料
    public class UserDBModel
    {
        public int Id { get; set; }
        public string IDNumber { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }  // 'Patient' 或 'Admin'
        public string FullName { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsActive { get; set; }

        // 輔助屬性：判斷是否為管理者
        public bool IsAdmin => Role?.ToLower() == "admin";
    }
}
