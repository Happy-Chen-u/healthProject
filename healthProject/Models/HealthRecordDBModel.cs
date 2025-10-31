using System;
using System.Text.Json;

namespace healthProject.Models
{
    public class HealthRecordDBModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime RecordDate { get; set; }
        public TimeSpan? RecordTime { get; set; }

        // 🆕 血壓 - 8個欄位 (4次測量,每次2個值)
        public decimal? BP_First_1_Systolic { get; set; }    // 第一次第一遍收縮壓
        public decimal? BP_First_1_Diastolic { get; set; }   // 第一次第一遍舒張壓
        public decimal? BP_First_2_Systolic { get; set; }    // 第一次第二遍收縮壓
        public decimal? BP_First_2_Diastolic { get; set; }   // 第一次第二遍舒張壓
        public decimal? BP_Second_1_Systolic { get; set; }   // 第二次第一遍收縮壓
        public decimal? BP_Second_1_Diastolic { get; set; }  // 第二次第一遍舒張壓
        public decimal? BP_Second_2_Systolic { get; set; }   // 第二次第二遍收縮壓
        public decimal? BP_Second_2_Diastolic { get; set; }  // 第二次第二遍舒張壓

        // 三餐 (JSON 字串)
        public string? Meals_Breakfast { get; set; }
        public string? Meals_Lunch { get; set; }
        public string? Meals_Dinner { get; set; }

        // 其他
        public string? ExerciseType { get; set; }
        public decimal? ExerciseDuration { get; set; }
        public decimal? WaterIntake { get; set; }
        public string? Beverage { get; set; }
        public decimal? Cigarettes { get; set; }
        public decimal? BetelNut { get; set; }
        public decimal? BloodSugar { get; set; }

        // 轉換為 ViewModel
        public HealthRecordViewModel ToViewModel()
        {
            var vm = new HealthRecordViewModel
            {
                Id = Id,
                UserId = UserId,
                RecordDate = RecordDate,
                RecordTime = RecordTime,

                // 血壓數值
                BP_First_1_Systolic = BP_First_1_Systolic,
                BP_First_1_Diastolic = BP_First_1_Diastolic,
                BP_First_2_Systolic = BP_First_2_Systolic,
                BP_First_2_Diastolic = BP_First_2_Diastolic,
                BP_Second_1_Systolic = BP_Second_1_Systolic,
                BP_Second_1_Diastolic = BP_Second_1_Diastolic,
                BP_Second_2_Systolic = BP_Second_2_Systolic,
                BP_Second_2_Diastolic = BP_Second_2_Diastolic,

                // 三餐
                Meals_Breakfast = !string.IsNullOrEmpty(Meals_Breakfast)
                    ? JsonSerializer.Deserialize<MealSelection>(Meals_Breakfast)
                    : null,
                Meals_Lunch = !string.IsNullOrEmpty(Meals_Lunch)
                    ? JsonSerializer.Deserialize<MealSelection>(Meals_Lunch)
                    : null,
                Meals_Dinner = !string.IsNullOrEmpty(Meals_Dinner)
                    ? JsonSerializer.Deserialize<MealSelection>(Meals_Dinner)
                    : null,

                // 其他
                ExerciseType = ExerciseType,
                ExerciseDuration = ExerciseDuration,
                WaterIntake = WaterIntake,
                Beverage = Beverage,
                Cigarettes = Cigarettes,
                BetelNut = BetelNut,
                BloodSugar = BloodSugar
            };

            // 🆕 重建輸入框的顯示值 (用於編輯時)
            if (BP_First_1_Systolic.HasValue && BP_First_1_Diastolic.HasValue)
                vm.BP_First_1_Input = $"{BP_First_1_Systolic}/{BP_First_1_Diastolic}";

            if (BP_First_2_Systolic.HasValue && BP_First_2_Diastolic.HasValue)
                vm.BP_First_2_Input = $"{BP_First_2_Systolic}/{BP_First_2_Diastolic}";

            if (BP_Second_1_Systolic.HasValue && BP_Second_1_Diastolic.HasValue)
                vm.BP_Second_1_Input = $"{BP_Second_1_Systolic}/{BP_Second_1_Diastolic}";

            if (BP_Second_2_Systolic.HasValue && BP_Second_2_Diastolic.HasValue)
                vm.BP_Second_2_Input = $"{BP_Second_2_Systolic}/{BP_Second_2_Diastolic}";

            return vm;
        }
    }

    // ========================================
    // 群組顯示 Model (用於 MyRecords 頁面)
    // ========================================
    public class DailyRecordGroup
    {
        public DateTime Date { get; set; }
        public List<HealthRecordViewModel> Records { get; set; } = new();

        // 當日統計 - 總和
        public decimal? TotalWater => Records.Sum(r => r.WaterIntake ?? 0);
        public decimal? TotalExercise => Records.Sum(r => r.ExerciseDuration ?? 0);
        public decimal? TotalCigarettes => Records.Sum(r => r.Cigarettes ?? 0);
        public decimal? TotalBetelNut => Records.Sum(r => r.BetelNut ?? 0);

        // 平均血糖
        public decimal? AvgBloodSugar
        {
            get
            {
                var values = Records.Where(r => r.BloodSugar.HasValue).Select(r => r.BloodSugar.Value).ToList();
                return values.Any() ? values.Average() : null;
            }
        }

        // 平均血壓
        public decimal? AvgSystolicBP
        {
            get
            {
                var allValues = Records
                    .SelectMany(r => new[] {
                    r.BP_First_1_Systolic, r.BP_First_2_Systolic,
                    r.BP_Second_1_Systolic, r.BP_Second_2_Systolic
                    })
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();
                return allValues.Any() ? allValues.Average() : null;
            }
        }

        public decimal? AvgDiastolicBP
        {
            get
            {
                var allValues = Records
                    .SelectMany(r => new[] {
                    r.BP_First_1_Diastolic, r.BP_First_2_Diastolic,
                    r.BP_Second_1_Diastolic, r.BP_Second_2_Diastolic
                    })
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();
                return allValues.Any() ? allValues.Average() : null;
            }
        }

        // 🆕 三餐總計
        public class MealTotal
        {
            public decimal NumericTotal { get; set; }  // 數字總和
            public List<string> OtherTexts { get; set; } = new();  // "其他"文字

            public string Display
            {
                get
                {
                    if (NumericTotal == 0 && !OtherTexts.Any())
                        return "0";

                    var parts = new List<string>();
                    if (NumericTotal > 0)
                        parts.Add($"{NumericTotal} (拳頭)");
                    if (OtherTexts.Any())
                        parts.Add(string.Join(" + ", OtherTexts));

                    return string.Join(" + ", parts);
                }
            }
        }

        // 計算三餐各營養素總量
        public MealTotal TotalVegetables
        {
            get
            {
                var result = new MealTotal();
                var allMeals = Records.SelectMany(r => new[] { r.Meals_Breakfast, r.Meals_Lunch, r.Meals_Dinner })
                                      .Where(m => m != null && !string.IsNullOrEmpty(m.Vegetables))
                                      .ToList();

                foreach (var meal in allMeals)
                {
                    if (decimal.TryParse(meal.Vegetables, out decimal numeric))
                    {
                        result.NumericTotal += numeric;
                    }
                    else if (meal.Vegetables != "0" && meal.Vegetables != "其他")
                    {
                        result.OtherTexts.Add(meal.Vegetables);
                    }
                }

                return result;
            }
        }

        public MealTotal TotalProtein
        {
            get
            {
                var result = new MealTotal();
                var allMeals = Records.SelectMany(r => new[] { r.Meals_Breakfast, r.Meals_Lunch, r.Meals_Dinner })
                                      .Where(m => m != null && !string.IsNullOrEmpty(m.Protein))
                                      .ToList();

                foreach (var meal in allMeals)
                {
                    if (decimal.TryParse(meal.Protein, out decimal numeric))
                    {
                        result.NumericTotal += numeric;
                    }
                    else if (meal.Protein != "0" && meal.Protein != "其他")
                    {
                        result.OtherTexts.Add(meal.Protein);
                    }
                }

                return result;
            }
        }

        public MealTotal TotalCarbs
        {
            get
            {
                var result = new MealTotal();
                var allMeals = Records.SelectMany(r => new[] { r.Meals_Breakfast, r.Meals_Lunch, r.Meals_Dinner })
                                      .Where(m => m != null && !string.IsNullOrEmpty(m.Carbs))
                                      .ToList();

                foreach (var meal in allMeals)
                {
                    if (decimal.TryParse(meal.Carbs, out decimal numeric))
                    {
                        result.NumericTotal += numeric;
                    }
                    else if (meal.Carbs != "0" && meal.Carbs != "其他")
                    {
                        result.OtherTexts.Add(meal.Carbs);
                    }
                }

                return result;
            }
        }

        // 是否有三餐資料
        public bool HasAnyMeals => Records.Any(r =>
            r.Meals_Breakfast != null || r.Meals_Lunch != null || r.Meals_Dinner != null);
    }
}