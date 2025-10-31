using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace healthProject.Models
{
    // ========================================
    // 三餐選項 Model
    // ========================================
    public class MealSelection
    {
        public string Vegetables { get; set; } // 蔬菜
        public string Protein { get; set; }    // 蛋白質
        public string Carbs { get; set; }      // 澱粉

        public override string ToString()
        {
            return $"蔬菜:{Vegetables ?? "未選"}, 蛋白質:{Protein ?? "未選"}, 澱粉:{Carbs ?? "未選"}";
        }
    }

    // ========================================
    // 自訂血壓驗證 Attribute
    // ========================================
    public class BloodPressureAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return ValidationResult.Success; // 允許空值

            var input = value.ToString();
            var parts = input.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                return new ValidationResult("血壓格式錯誤,請輸入如: 120/80");

            if (!decimal.TryParse(parts[0], out var sys) || !decimal.TryParse(parts[1], out var dia))
                return new ValidationResult("血壓數值必須為數字");

            if (sys < 50 || sys > 250 || dia < 30 || dia > 150)
                return new ValidationResult("血壓數值超出合理範圍");

            return ValidationResult.Success;
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

        // 🆕 記錄時間 (用於區分同一天的多筆)
        public TimeSpan? RecordTime { get; set; }

        // ────────────────────────────────────────
        // 血壓輸入 (前端輸入 120/80 格式)
        // ────────────────────────────────────────
        [Display(Name = "第一次第一遍(上午)")]
        [BloodPressure]
        public string? BP_First_1_Input { get; set; }

        [Display(Name = "第一次第二遍(上午)")]
        [BloodPressure]
        public string? BP_First_2_Input { get; set; }

        [Display(Name = "第二次第一遍(睡前)")]
        [BloodPressure]
        public string? BP_Second_1_Input { get; set; }

        [Display(Name = "第二次第二遍(睡前)")]
        [BloodPressure]
        public string? BP_Second_2_Input { get; set; }

        // ────────────────────────────────────────
        // 內部數值 (存入 DB) - 4次量測,每次2個值(收縮壓/舒張壓)
        // ────────────────────────────────────────
        public decimal? BP_First_1_Systolic { get; set; }   // 第一次第一遍收縮壓
        public decimal? BP_First_1_Diastolic { get; set; }  // 第一次第一遍舒張壓

        public decimal? BP_First_2_Systolic { get; set; }   // 第一次第二遍收縮壓
        public decimal? BP_First_2_Diastolic { get; set; }  // 第一次第二遍舒張壓

        public decimal? BP_Second_1_Systolic { get; set; }  // 第二次第一遍收縮壓
        public decimal? BP_Second_1_Diastolic { get; set; } // 第二次第一遍舒張壓

        public decimal? BP_Second_2_Systolic { get; set; }  // 第二次第二遍收縮壓
        public decimal? BP_Second_2_Diastolic { get; set; } // 第二次第二遍舒張壓

        // ────────────────────────────────────────
        // 解析血壓輸入 (Controller 呼叫)
        // ────────────────────────────────────────
        public void ParseBloodPressure()
        {
            ParseOne(BP_First_1_Input, out var s1, out var d1);
            BP_First_1_Systolic = s1;
            BP_First_1_Diastolic = d1;

            ParseOne(BP_First_2_Input, out var s2, out var d2);
            BP_First_2_Systolic = s2;
            BP_First_2_Diastolic = d2;

            ParseOne(BP_Second_1_Input, out var s3, out var d3);
            BP_Second_1_Systolic = s3;
            BP_Second_1_Diastolic = d3;

            ParseOne(BP_Second_2_Input, out var s4, out var d4);
            BP_Second_2_Systolic = s4;
            BP_Second_2_Diastolic = d4;
        }

        private static void ParseOne(string input, out decimal? sys, out decimal? dia)
        {
            sys = dia = null;
            if (string.IsNullOrWhiteSpace(input)) return;

            var parts = input.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return;

            if (decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                sys = s;
            if (decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                dia = d;
        }

        // ────────────────────────────────────────
        // 顯示用 (計算平均血壓)
        // ────────────────────────────────────────
        public decimal? AvgSystolicBP
        {
            get
            {
                var values = new[] { BP_First_1_Systolic, BP_First_2_Systolic,
                                    BP_Second_1_Systolic, BP_Second_2_Systolic }
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();

                return values.Any() ? values.Average() : null;
            }
        }

        public decimal? AvgDiastolicBP
        {
            get
            {
                var values = new[] { BP_First_1_Diastolic, BP_First_2_Diastolic,
                                    BP_Second_1_Diastolic, BP_Second_2_Diastolic }
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();

                return values.Any() ? values.Average() : null;
            }
        }

        // ========================================
        // 🆕 三餐 - 移除 Required,改為可選
        // ========================================
        public MealSelection Meals_Breakfast { get; set; }
        public MealSelection Meals_Lunch { get; set; }
        public MealSelection Meals_Dinner { get; set; }

        // 用於前端顯示的完整三餐描述
        public string MealsDisplay
        {
            get
            {
                var meals = new List<string>();
                if (Meals_Breakfast != null) meals.Add($"早餐: {Meals_Breakfast}");
                if (Meals_Lunch != null) meals.Add($"午餐: {Meals_Lunch}");
                if (Meals_Dinner != null) meals.Add($"晚餐: {Meals_Dinner}");
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