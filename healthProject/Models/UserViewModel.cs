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
        public string IDNumber { get; set; }

        [Required(ErrorMessage = "姓名為必填")]
        [StringLength(50, ErrorMessage = "姓名長度不可超過50字")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "電話號碼為必填")]
        [Phone(ErrorMessage = "電話號碼格式不正確")]
        public string PhoneNumber { get; set; }
    }
}