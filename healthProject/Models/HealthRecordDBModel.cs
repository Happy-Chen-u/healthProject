using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace healthProject.Models
{
    
    public class HealthRecordDBModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public DateTime RecordDate { get; set; } = DateTime.Today;

        
        [StringLength(100)]
        public string? ExerciseType { get; set; }

        
        [Range(0, 999.9)]
        public decimal? ExerciseDuration { get; set; }

        
        [Range(0, 999999)]
        public decimal? WaterIntake { get; set; }

        
        [StringLength(200)]
        public string? Beverage { get; set; }

        
        [StringLength(500)]
        public string? Meals { get; set; }

        
        [Range(0, 99999)]
        public decimal? Cigarettes { get; set; }

        
        [Range(0, 99999)]
        public decimal? BetelNut { get; set; }

        
        [Range(0, 999.9)]
        public decimal? BloodSugar { get; set; }

        
        [Range(0, 999.99)]
        public decimal? SystolicBP { get; set; }

        
        [Range(0, 999.99)]
        public decimal? DiastolicBP { get; set; }

        // 標準值常數
        public const int WATER_STANDARD = 2000; // 2000ml
        public const int EXERCISE_STANDARD = 150; // 150分鐘
    }
}