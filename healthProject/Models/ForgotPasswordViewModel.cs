using System.ComponentModel.DataAnnotations;

namespace healthProject.Models
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "請輸入身分證字號")]
        [Display(Name = "身分證字號")]
        public string IDNumber { get; set; }

        [Required(ErrorMessage = "請輸入姓名")]
        [Display(Name = "姓名")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "請輸入電話號碼")]
        [Phone(ErrorMessage = "電話號碼格式不正確")]
        [Display(Name = "電話號碼")]
        public string PhoneNumber { get; set; }
    }
}
