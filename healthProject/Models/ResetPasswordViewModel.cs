using System.ComponentModel.DataAnnotations;

namespace healthProject.Models
{
    public class ResetPasswordViewModel
    {
        [Required]
        [Display(Name = "帳號")]
        public string IDNumber { get; set; }

        [Required(ErrorMessage = "請輸入新密碼")]
        [DataType(DataType.Password)]
        [Display(Name = "新密碼")]
        public string Password { get; set; }

        [Required(ErrorMessage = "請再次輸入新密碼")]
        [DataType(DataType.Password)]
        [Display(Name = "確認新密碼")]
        [Compare("Password", ErrorMessage = "兩次輸入的密碼不一致")]
        public string ConfirmPassword { get; set; }
    }
}
