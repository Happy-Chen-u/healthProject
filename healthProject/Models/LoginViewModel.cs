using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace healthProject.Models
{
    // 登入畫面（共用給個案與管理者）
    public class LoginViewModel
    {
        [Required(ErrorMessage = "請輸入帳號")]
        [Display(Name = "帳號")]
        public string Username { get; set; }

        [Required(ErrorMessage = "請輸入密碼")]
        [DataType(DataType.Password)]
        [Display(Name = "密碼")]
        public string Password { get; set; }

        
    }
}
