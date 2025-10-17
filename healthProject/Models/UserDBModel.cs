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
        public string PhoneNumber{ get; set; }


        //是否第一次登入
        public bool IsFirstLogin { get; set; } = true;

        // LINE User ID，用來發推播
        public string LineUserId { get; set; }

        // 輔助屬性：判斷是否為管理者
        public bool IsAdmin => Role?.ToLower() == "admin";
    }
}
