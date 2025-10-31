using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace healthProject.Models
{
    // ========================================
    // 三餐選項 Model
    // ========================================
    public class MealSelection
    {
        public string? Vegetables { get; set; }
        public string? Protein { get; set; }
        public string? Carbs { get; set; }

        public override string ToString()
        {
            return $"蔬菜:{Vegetables}, 蛋白質:{Protein}, 澱粉:{Carbs}";
        }
    }

    // ========================================
    // 今日健康記錄 ViewModel
    // ========================================
    public class HealthRecordViewModel
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public DateTime RecordDate { get; set; } = DateTime.Today;

        public TimeSpan? RecordTime { get; set; }

        // ✅ 只保留這 8 個數字欄位
        public decimal? BP_First_1_Systolic { get; set; }
        public decimal? BP_First_1_Diastolic { get; set; }
        public decimal? BP_First_2_Systolic { get; set; }
        public decimal? BP_First_2_Diastolic { get; set; }
        public decimal? BP_Second_1_Systolic { get; set; }
        public decimal? BP_Second_1_Diastolic { get; set; }
        public decimal? BP_Second_2_Systolic { get; set; }
        public decimal? BP_Second_2_Diastolic { get; set; }

        // ✅ 新增驗證方法
        public List<string> ValidateBloodPressure()
        {
            var warnings = new List<string>();

            // 第一次(上午)
            bool hasFirst1 = BP_First_1_Systolic.HasValue && BP_First_1_Diastolic.HasValue;
            bool hasFirst2 = BP_First_2_Systolic.HasValue && BP_First_2_Diastolic.HasValue;

            if (hasFirst1 && !hasFirst2)
            {
                warnings.Add("⚠️ 上午血壓: 已填第一遍,但未填第二遍");
            }

            // 第二次(睡前)
            bool hasSecond1 = BP_Second_1_Systolic.HasValue && BP_Second_1_Diastolic.HasValue;
            bool hasSecond2 = BP_Second_2_Systolic.HasValue && BP_Second_2_Diastolic.HasValue;

            if (hasSecond1 && !hasSecond2)
            {
                warnings.Add("⚠️ 睡前血壓: 已填第一遍,但未填第二遍");
            }

            return warnings;
        }

        // ========================================
        // 🆕 前端輸入用的字串欄位 (格式: "120/80")
        // ========================================
        public string? BP_First_1_Input { get; set; }
        public string? BP_First_2_Input { get; set; }
        public string? BP_Second_1_Input { get; set; }
        public string? BP_Second_2_Input { get; set; }

        


        private void ParseBPInput(string input, out decimal? systolic, out decimal? diastolic)
        {
            systolic = null;
            diastolic = null;

            if (string.IsNullOrWhiteSpace(input)) return;

            // 正則表達式: 匹配 "120/80" 或 "120 / 80" 格式
            var match = Regex.Match(input.Trim(), @"^(\d+)\s*/\s*(\d+)$");
            if (match.Success)
            {
                if (decimal.TryParse(match.Groups[1].Value, out decimal sys))
                    systolic = sys;
                if (decimal.TryParse(match.Groups[2].Value, out decimal dia))
                    diastolic = dia;
            }
        }

        

        // ========================================
        // 計算平均血壓 (所有有效測量的平均)
        // ========================================
        public decimal? AvgSystolicBP
        {
            get
            {
                var values = new[] {
                    BP_First_1_Systolic, BP_First_2_Systolic,
                    BP_Second_1_Systolic, BP_Second_2_Systolic
                }.Where(v => v.HasValue).Select(v => v.Value);

                return values.Any() ? values.Average() : null;
            }
        }

        public decimal? AvgDiastolicBP
        {
            get
            {
                var values = new[] {
                    BP_First_1_Diastolic, BP_First_2_Diastolic,
                    BP_Second_1_Diastolic, BP_Second_2_Diastolic
                }.Where(v => v.HasValue).Select(v => v.Value);

                return values.Any() ? values.Average() : null;
            }
        }

        // ========================================
        // 三餐 - JSON 格式
        // ========================================
        public MealSelection? Meals_Breakfast { get; set; }
        public MealSelection? Meals_Lunch { get; set; }
        public MealSelection? Meals_Dinner { get; set; }

        public string MealsDisplay
        {
            get
            {
                var meals = new List<string>();
                if (Meals_Breakfast != null) meals.Add($"早: {Meals_Breakfast}");
                if (Meals_Lunch != null) meals.Add($"午: {Meals_Lunch}");
                if (Meals_Dinner != null) meals.Add($"晚: {Meals_Dinner}");
                return meals.Any() ? string.Join(" | ", meals) : "未記錄";
            }
        }

        // ========================================
        // 其他欄位
        // ========================================
        [StringLength(100)]
        [Display(Name = "運動類型")]
        public string? ExerciseType { get; set; }

        [Range(0, 999.9)]
        [Display(Name = "運動時間 (分鐘)")]
        public decimal? ExerciseDuration { get; set; }

        [Range(0, 999999)]
        [Display(Name = "飲水量 (ml)")]
        public decimal? WaterIntake { get; set; }

        [StringLength(200)]
        [Display(Name = "飲料")]
        public string? Beverage { get; set; }

        [Range(0, 99999)]
        [Display(Name = "抽菸數量")]
        public decimal? Cigarettes { get; set; }

        [Range(0, 99999)]
        [Display(Name = "檳榔數量")]
        public decimal? BetelNut { get; set; }

        [Range(0, 999.9)]
        [Display(Name = "血糖 (mg/dL)")]
        public decimal? BloodSugar { get; set; }

        // 標準值常數
        public const int WATER_STANDARD = 2000;
        public const int EXERCISE_STANDARD = 150;
        public const int BP_SYSTOLIC_STANDARD = 120;
        public const int BP_DIASTOLIC_STANDARD = 80;
        public const int BLOOD_SUGAR_STANDARD = 99;
    }
}