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
        [StringLength(100, MinimumLength = 6, ErrorMessage = "密碼長度必須至少 6 個字元")]
        [DataType(DataType.Password)]
        [Display(Name = "新密碼")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "請確認新密碼")]
        [DataType(DataType.Password)]
        [Display(Name = "確認新密碼")]
        [Compare("NewPassword", ErrorMessage = "新密碼與確認密碼不符")]
        public string ConfirmPassword { get; set; }
    }
}
