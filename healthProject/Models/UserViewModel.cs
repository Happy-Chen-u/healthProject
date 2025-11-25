using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace healthProject.Models
{
    public class UserViewModel
    {
        [Required(ErrorMessage = "特殊符號為必填")]
        [StringLength(1, MinimumLength = 1, ErrorMessage = "特殊符號必須為1個字元")]
        public string SpecialSymbol { get; set; }

        [Required(ErrorMessage = "身分證字號為必填")]
        [StringLength(10, MinimumLength = 10, ErrorMessage = "身分證字號必須為10碼")]
        [RegularExpression(@"^[A-Z][12]\d{8}$", ErrorMessage = "身分證格式有誤:第一碼為英文大寫,第二碼為1或2,總共10碼")]
        public string IDNumber { get; set; }

        [Required(ErrorMessage = "姓名為必填")]
        [StringLength(50, ErrorMessage = "姓名長度不可超過50字")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "電話號碼為必填")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "電話號碼格式有誤:請輸入10碼數字")]
        public string PhoneNumber { get; set; }
    }
}