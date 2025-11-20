using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace healthProject.Models
{
    [Table("CaseManagement")]
    public class CaseManagementViewModel
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey("User")]
        [Required, StringLength(12)]
        public string IDNumber { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }

        [Required, StringLength(10)]
        public string Gender { get; set; }

        [Required]
        public DateTime BirthDate { get; set; }

        [Required]
        public decimal Height { get; set; }

        [Required]
        public decimal Weight { get; set; }

        public bool BMI { get; set; }
        public decimal? BMI_Value { get; set; }

        public bool SystolicBP { get; set; }
        public decimal? SystolicBP_Value { get; set; }

        public bool DiastolicBP { get; set; }
        public decimal? DiastolicBP_Value { get; set; }

        public bool CurrentWaist { get; set; }
        public decimal? CurrentWaist_Value { get; set; }

        public bool FastingGlucose { get; set; }
        public decimal? FastingGlucose_Value { get; set; }

        public bool HDL { get; set; }
        public decimal? HDL_Value { get; set; }

        public bool LDL { get; set; }
        public decimal? LDL_Value { get; set; }

        public bool HbA1c { get; set; }
        public decimal? HbA1c_Value { get; set; }

        public bool Triglycerides { get; set; }
        public decimal? Triglycerides_Value { get; set; }

        public DateTime? FollowUpDate { get; set; }

        public bool Assessment { get; set; }
        public DateTime? AssessmentDate { get; set; }

        public bool AnnualAssessment { get; set; }
        public DateTime? AnnualAssessment_Date { get; set; }

        // 運動
        public bool ExerciseNone { get; set; }
        public bool ExerciseUsually { get; set; }
        public bool ExerciseAlways { get; set; }

        // 抽菸
        public bool SmokingNone { get; set; }
        public bool SmokingUsually { get; set; }
        public bool SmokingUnder10 { get; set; }
        public bool SmokingOver10 { get; set; }

        // 嚼檳榔
        public bool BetelNutNone { get; set; }
        public bool BetelNutUsually { get; set; }
        public bool BetelNutAlways { get; set; }

        // 疾病風險
        public bool CoronaryHigh { get; set; }
        public bool CoronaryMedium { get; set; }
        public bool CoronaryLow { get; set; }
        public bool CoronaryNotApplicable { get; set; }

        public bool DiabetesHigh { get; set; }
        public bool DiabetesMedium { get; set; }
        public bool DiabetesLow { get; set; }
        public bool DiabetesNotApplicabe { get; set; }

        public bool HypertensionHigh { get; set; }
        public bool HypertensionMedium { get; set; }
        public bool HypertensionLow { get; set; }
        public bool HypertensionNotApplicable { get; set; }

        public bool StrokeHigh { get; set; }
        public bool StrokeMedium { get; set; }
        public bool StrokeLow { get; set; }
        public bool StrokeNotApplicable { get; set; }

        public bool CardiovascularHigh { get; set; }
        public bool CardiovascularMedium { get; set; }
        public bool CardiovascularLow { get; set; }
        public bool CardiovascularNotApplicable { get; set; }

        // 戒菸服務
        public bool SmokingService { get; set; }
        public bool SmokingServiceType1 { get; set; }
        public bool SmokingServiceType2 { get; set; }
        public bool SmokingServiceType2_Provide { get; set; }
        public bool SmokingServiceType2_Referral { get; set; }

        // 戒檳服務
        public bool BetelNutService { get; set; }
        public bool BetelQuitGoal { get; set; }
        public int? BetelQuitYear { get; set; }
        public int? BetelQuitMonth { get; set; }
        public int? BetelQuitDay { get; set; }

        public bool OralExam { get; set; }
        public int? OralExamYear { get; set; }
        public int? OralExamMonth { get; set; }

        // 飲食管理
        public bool DietManagement { get; set; }
        public bool DailyCalories1200 { get; set; }
        public bool DailyCalories1500 { get; set; }
        public bool DailyCalories1800 { get; set; }
        public bool DailyCalories2000 { get; set; }
        public bool DailyCaloriesOther { get; set; }
        public string? DailyCaloriesOtherValue { get; set; }

        public bool ReduceFriedFood { get; set; }
        public bool ReduceSweetFood { get; set; }
        public bool ReduceSalt { get; set; }
        public bool ReduceSugaryDrinks { get; set; }
        public bool ReduceOther { get; set; }
        public string? ReduceOtherValue { get; set; }

        // 想達成的腰圍體重
        public bool Achievement { get; set; }
        public decimal? WaistTarget_Value { get; set; }
        public decimal? WeightTarget_Value { get; set; }

        // 量血壓
        public bool BloodPressureGuidance722 { get; set; }

        // 運動建議
        public bool ExerciseRecommendation { get; set; }
        public bool ExerciseGuidance { get; set; }
        public bool SocialExerciseResources { get; set; }
        public string? SocialExerciseResources_Text { get; set; }

        // 其他叮嚀
        public bool OtherReminders { get; set; }
        public bool FastingGlucoseTarget { get; set; }
        public decimal? FastingGlucoseTarget_Value { get; set; }

        public bool HbA1cTarget { get; set; }
        public decimal? HbA1cTarget_Value { get; set; }
        public bool TriglyceridesTarget { get; set; }
        public decimal? TriglyceridesTarget_Value { get; set; }
        public bool HDL_CholesterolTarget { get; set; }
        public decimal? HDL_CholesterolTarget_Value { get; set; }
        public bool LDL_CholesterolTarget { get; set; }
        public decimal? LDL_CholesterolTarget_Value { get; set; }

        public string? Notes { get; set; }

        // ⭐ 新增：用於傳遞多筆評估記錄到 View (不對應資料庫欄位)
        [NotMapped]
        public List<EvaluationRecord> EvaluationRecords { get; set; } = new List<EvaluationRecord>();

    }


    // 評估記錄 DTO (Data Transfer Object)
    public class EvaluationRecord
    {
        public int CaseId { get; set; }
        public DateTime EvaluationDate { get; set; }

        // 腰圍
        public string WaistTarget_Value { get; set; }
        public string WaistCurrent_Value { get; set; }
        public bool WaistAchievement { get; set; }

        // 體重
        public string WeightTarget_Value { get; set; }
        public string WeightCurrent_Value { get; set; }
        public bool WeightAchievement { get; set; }

        // 空腹血糖
        public string FastingGlucoseTarget_Value { get; set; }
        public string FastingGlucoseCurrent_Value { get; set; }
        public bool FastingGlucoseAchievement { get; set; }

        // HbA1c
        public string HbA1cTarget_Value { get; set; }
        public string HbA1cCurrent_Value { get; set; }
        public bool HbA1cAchievement { get; set; }

        // 三酸甘油脂
        public string TriglyceridesTarget_Value { get; set; }
        public string TriglyceridesCurrent_Value { get; set; }
        public bool TriglyceridesAchievement { get; set; }

        // HDL
        public string HDL_CholesterolTarget_Value { get; set; }
        public string HDL_CholesterolCurrent_Value { get; set; }
        public bool HDL_CholesterolAchievement { get; set; }

        // LDL
        public string LDL_CholesterolTarget_Value { get; set; }
        public string LDL_CholesterolCurrent_Value { get; set; }
        public bool LDL_CholesterolAchievement { get; set; }

        // 抽菸
        public bool SmokingNone { get; set; }
        public bool SmokingUsually { get; set; }
        public bool SmokingUnder10 { get; set; }
        public bool SmokingOver10 { get; set; }

        // 嚼檳榔
        public bool BetelNutNone { get; set; }
        public bool BetelNutUsually { get; set; }
        public bool BetelNutAlways { get; set; }
    }
}