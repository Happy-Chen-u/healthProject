namespace healthProject.Models
{
    public class ForgotPasswordViewModel
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "請輸入帳號")]
        public string Username { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "請輸入 Email")]
        [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "Email 格式不正確")]
        public string Email { get; set; }
    }
}