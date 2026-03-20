using System.ComponentModel.DataAnnotations;

namespace healthProject.Models
{
    public class ResetPasswordViewModel
    {
        [Required]
        [Display(Name = "帳號")]
        public string IDNumber { get; set; }

        [Required(ErrorMessage = "請輸入新密碼")]
        [MinLength(7, ErrorMessage = "密碼至少需要 7 個字元")]
        public string Password { get; set; }

        [Required(ErrorMessage = "請確認新密碼")]
        [Compare("Password", ErrorMessage = "新密碼與確認密碼不一致")]
        public string ConfirmPassword { get; set; }
    }
}
