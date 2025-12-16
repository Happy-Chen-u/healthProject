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

        // 只保留這 8 個數字欄位
        public decimal? BP_First_1_Systolic { get; set; }
        public decimal? BP_First_1_Diastolic { get; set; }
        public decimal? BP_First_2_Systolic { get; set; }
        public decimal? BP_First_2_Diastolic { get; set; }
        public decimal? BP_Second_1_Systolic { get; set; }
        public decimal? BP_Second_1_Diastolic { get; set; }
        public decimal? BP_Second_2_Systolic { get; set; }
        public decimal? BP_Second_2_Diastolic { get; set; }

        public bool? BP_Morning_NotMeasured { get; set; }  // 上午血壓尚未測量
        public bool? BP_Evening_NotMeasured { get; set; }  // 睡前血壓尚未測量

        // 用於 Controller 傳入當日是否已完成紀錄的狀態
        public bool IsMorningCompletedToday { get; set; } = false;
        public bool IsEveningCompletedToday { get; set; } = false;



        // 新增驗證方法
        public List<string> ValidateBloodPressure()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            // --- 階段 1: 數值下限檢查 (確保輸入是安全的) ---

            if (BP_First_1_Systolic.HasValue && BP_First_1_Systolic.Value < 30)
                warnings.Add("⚠️ 上午第一遍收縮壓不可低於 30 mmHg");
            if (BP_First_1_Diastolic.HasValue && BP_First_1_Diastolic.Value < 10)
                warnings.Add("⚠️ 上午第一遍舒張壓不可低於 10 mmHg");
            if (BP_First_2_Systolic.HasValue && BP_First_2_Systolic.Value < 30)
                warnings.Add("⚠️ 上午第二遍收縮壓不可低於 30 mmHg");
            if (BP_First_2_Diastolic.HasValue && BP_First_2_Diastolic.Value < 10)
                warnings.Add("⚠️ 上午第二遍舒張壓不可低於 10 mmHg");
            if (BP_Second_1_Systolic.HasValue && BP_Second_1_Systolic.Value < 30)
                warnings.Add("⚠️ 睡前第一遍收縮壓不可低於 30 mmHg");
            if (BP_Second_1_Diastolic.HasValue && BP_Second_1_Diastolic.Value < 10)
                warnings.Add("⚠️ 睡前第一遍舒張壓不可低於 10 mmHg");
            if (BP_Second_2_Systolic.HasValue && BP_Second_2_Systolic.Value < 30)
                warnings.Add("⚠️ 睡前第二遍收縮壓不可低於 30 mmHg");
            if (BP_Second_2_Diastolic.HasValue && BP_Second_2_Diastolic.Value < 10)
                warnings.Add("⚠️ 睡前第二遍舒張壓不可低於 10 mmHg");


            // --- 階段 2: 遍數完整性檢查 (填了收縮壓就必須填舒張壓) ---

            // 檢查 BP_First_1 配對
            bool hasBPF1_Sys = BP_First_1_Systolic.HasValue;
            bool hasBPF1_Dia = BP_First_1_Diastolic.HasValue;
            if (hasBPF1_Sys != hasBPF1_Dia)
                errors.Add("🔴 上午血壓第一遍:收縮壓和舒張壓必須同時填寫!");

            // 檢查 BP_First_2 配對
            bool hasBPF2_Sys = BP_First_2_Systolic.HasValue;
            bool hasBPF2_Dia = BP_First_2_Diastolic.HasValue;
            if (hasBPF2_Sys != hasBPF2_Dia)
                errors.Add("🔴 上午血壓第二遍:收縮壓和舒張壓必須同時填寫!");

            // 檢查 BP_Second_1 配對
            bool hasBPS1_Sys = BP_Second_1_Systolic.HasValue;
            bool hasBPS1_Dia = BP_Second_1_Diastolic.HasValue;
            if (hasBPS1_Sys != hasBPS1_Dia)
                errors.Add("🔴 睡前血壓第一遍:收縮壓和舒張壓必須同時填寫!");

            // 檢查 BP_Second_2 配對
            bool hasBPS2_Sys = BP_Second_2_Systolic.HasValue;
            bool hasBPS2_Dia = BP_Second_2_Diastolic.HasValue;
            if (hasBPS2_Sys != hasBPS2_Dia)
                errors.Add("🔴 睡前血壓第二遍:收縮壓和舒張壓必須同時填寫!");

            // 如果有配對錯誤,則先返回,不進行更複雜的時段檢查
            if (errors.Any()) return errors;


            // --- 階段 3: 時段完整性檢查 (BP_First_1/2 必須一起填;BP_Second_1/2 必須一起填) ---

            // 檢查上午血壓完整性 (排除勾選「尚未測量」和已完成的情況)
            bool isMorningInputAttempted = hasBPF1_Sys || hasBPF2_Sys;
            bool isMorningChecked = BP_Morning_NotMeasured == true;
            bool isMorningComplete = hasBPF1_Sys && hasBPF2_Sys; // 定義:兩遍都填了

            // 🚨 需求 2.3: 檢查第一遍/第二遍互補
            if (isMorningInputAttempted && !isMorningChecked)
            {
                if ((hasBPF1_Sys && !hasBPF2_Sys) || (!hasBPF1_Sys && hasBPF2_Sys))
                {
                    errors.Add("🔴 上午血壓:請務必將第一遍和第二遍**同時**填寫完整才能上傳。");
                    return errors; // 配對錯誤優先
                }
            }


            // 檢查睡前血壓完整性 (排除勾選「尚未測量」和已完成的情況)
            bool isEveningInputAttempted = hasBPS1_Sys || hasBPS2_Sys;
            bool isEveningChecked = BP_Evening_NotMeasured == true;
            bool isEveningComplete = hasBPS1_Sys && hasBPS2_Sys; // 定義:兩遍都填了

            // 🚨 需求 2.3: 檢查第一遍/第二遍互補
            if (isEveningInputAttempted && !isEveningChecked)
            {
                if ((hasBPS1_Sys && !hasBPS2_Sys) || (!hasBPS1_Sys && hasBPS2_Sys))
                {
                    errors.Add("🔴 睡前血壓:請務必將第一遍和第二遍**同時**填寫完整才能上傳。");
                    return errors; // 配對錯誤優先
                }
            }


            // --- 階段 4: 必填邏輯檢查 ---

            // ✅ 🎯 核心修正: 如果兩個時段都已完成,則完全跳過必填檢查
            if (IsMorningCompletedToday && IsEveningCompletedToday)
            {
                // 什麼都不做,直接跳過所有必填檢查
                // 允許使用者不填任何血壓資料
            }
            // ✅ 情況 2: 如果只有上午完成,則只檢查睡前時段
            else if (IsMorningCompletedToday && !IsEveningCompletedToday)
            {
                if (!(isEveningComplete || isEveningChecked))
                {
                    errors.Add("🔴 睡前血壓為必填時段。請填寫睡前血壓(兩遍)或勾選『尚未測量』。");
                }
            }
            // ✅ 情況 3: 如果只有睡前完成,則只檢查上午時段
            else if (!IsMorningCompletedToday && IsEveningCompletedToday)
            {
                if (!(isMorningComplete || isMorningChecked))
                {
                    errors.Add("🔴 上午血壓為必填時段。請填寫上午血壓(兩遍)或勾選『尚未測量』。");
                }
            }
            // ✅ 情況 4: 兩個時段都未完成,則兩者都要檢查
            else
            {
                if (!(isMorningComplete || isMorningChecked))
                {
                    errors.Add("🔴 上午血壓為必填時段。請填寫上午血壓(兩遍)或勾選『尚未測量』。");
                }

                if (!(isEveningComplete || isEveningChecked))
                {
                    errors.Add("🔴 睡前血壓為必填時段。請填寫睡前血壓(兩遍)或勾選『尚未測量』。");
                }
            }


            // 返回所有錯誤,如果有硬性錯誤 (errors),會優先於警告 (warnings) 顯示
            if (errors.Any()) return errors;
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