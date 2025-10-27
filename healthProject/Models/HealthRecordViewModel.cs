using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace healthProject.Models
{

    public class HealthRecordViewModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public DateTime RecordDate { get; set; } = DateTime.Today;

        [Display(Name = "運動種類")]
        [StringLength(100)]
        public string? ExerciseType { get; set; }

        [Display(Name = "運動時間 (分鐘)")]
        [Range(0, 999.9)]
        public decimal? ExerciseDuration { get; set; }

        [Display(Name = "水分攝取 (ml)")]
        [Range(0, 999999)]
        public decimal? WaterIntake { get; set; }

        [Display(Name = "飲料")]
        [StringLength(200)]
        public string? Beverage { get; set; }

        [Display(Name = "三餐內容")]
        [StringLength(500)]
        public string? Meals { get; set; }

        [Display(Name = "抽菸支數")]
        [Range(0, 99999)]
        public decimal? Cigarettes { get; set; }

        [Display(Name = "嚼檳榔次數")]
        [Range(0, 99999)]
        public decimal? BetelNut { get; set; }

        [Display(Name = "血糖 (mg/dL)")]
        [Range(0, 999.9)]
        public decimal? BloodSugar { get; set; }

        [Display(Name = "收縮壓 (mmHg)")]
        [Range(0, 999.99)]
        public decimal? SystolicBP { get; set; }

        [Display(Name = "舒張壓 (mmHg)")]
        [Range(0, 999.99)]
        public decimal? DiastolicBP { get; set; }

        // 標準值常數
        public const int WATER_STANDARD = 2000; // 2000ml
        public const int EXERCISE_STANDARD = 150; // 150分鐘
    }
}