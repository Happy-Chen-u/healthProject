using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace healthProject.Models
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "請輸入舊密碼")]
        [DataType(DataType.Password)]
        [Display(Name = "舊密碼")]
        public string OldPassword { get; set; }
        [Required(ErrorMessage = "請輸入新密碼")]
        [MinLength(7, ErrorMessage = "密碼至少需要 7 個字元")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "請確認新密碼")]
        [Compare("NewPassword", ErrorMessage = "新密碼與確認密碼不一致")]
        public string ConfirmPassword { get; set; }
    }
}
