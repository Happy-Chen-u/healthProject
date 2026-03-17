using System;
using System.Collections.Generic;

namespace healthProject.Models
{
    // 報表類型
    public enum ReportType
    {
        Daily,   // 每日
        Weekly,  // 每週
        Monthly, // 每月
        Yearly   // 每年
    }

    

    // 病患個人化目標值 (從最新一筆 CaseManagement 取得)
    public class PatientGoals
    {
        // 血壓目標 (若無則用預設值)
        public decimal? SystolicBPTarget { get; set; }   // 預設 130
        public decimal? DiastolicBPTarget { get; set; }  // 預設 80

        // 血糖目標
        public decimal? FastingGlucoseTarget { get; set; } // 預設 100

        // 飲水目標 (CaseManagement 沒有此欄位，固定 2000)
        public decimal WaterTarget { get; set; } = 2000;

        // 腰圍體重目標 (供參考)
        public decimal? WaistTarget { get; set; }
        public decimal? WeightTarget { get; set; }

        // HbA1c, 三酸甘油酯等
        public decimal? HbA1cTarget { get; set; }
        public decimal? TriglyceridesTarget { get; set; }
        public decimal? HDLTarget { get; set; }
        public decimal? LDLTarget { get; set; }
    }

    // 分析報表主 ViewModel
    public class AnalysisViewModel
    {
        // 性別、生日、目標
        public string PatientGender { get; set; } //
        public DateTime? PatientBirthDate { get; set; } //
        public PatientGoals Goals { get; set; }  // 目標值
        public string PatientName { get; set; }
        public string IDNumber { get; set; }
        public ReportType ReportType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // 統計數據
        public AnalysisStatistics Statistics { get; set; }

        // 原始記錄
        public List<HealthRecordViewModel> Records { get; set; }

        // 圖表數據
        public ChartData Charts { get; set; }

        public TrendSummary TrendSummary { get; set; }
    }

    public class TrendSummary
    {
        public string PeriodLabel { get; set; }
        public List<TrendItem> Items { get; set; } = new();
    }

    public class TrendItem
    {
        public string Label { get; set; }
        public string Icon { get; set; }
        public string CurrentValue { get; set; }
        public string PrevValue { get; set; }
        public string DiffText { get; set; }
        // "good" | "warn" | "bad"
        public string TrendType { get; set; }
        public string Message { get; set; }
    }

    // 統計數據
    public class AnalysisStatistics
    {
        public int TotalDays { get; set; }

        // 血壓
        public decimal? AvgSystolicBP { get; set; }
        public decimal? AvgDiastolicBP { get; set; }
        public int HighBPDays { get; set; }
        public decimal HighBPPercentage => TotalDays > 0 ? (decimal)HighBPDays / TotalDays * 100 : 0;

        // 血糖
        public decimal? AvgBloodSugar { get; set; }
        public int HighBloodSugarDays { get; set; }
        public decimal HighBloodSugarPercentage => TotalDays > 0 ? (decimal)HighBloodSugarDays / TotalDays * 100 : 0;

        // 飲水
        public decimal? AvgWaterIntake { get; set; }
        public int LowWaterDays { get; set; }
        public decimal LowWaterPercentage => TotalDays > 0 ? (decimal)LowWaterDays / TotalDays * 100 : 0;

        // 運動
        public decimal? AvgExerciseDuration { get; set; }
        public int LowExerciseDays { get; set; }
        public decimal LowExercisePercentage => TotalDays > 0 ? (decimal)LowExerciseDays / TotalDays * 100 : 0;

        // 抽菸
        public decimal? AvgCigarettes { get; set; }
        public int SmokingDays { get; set; }
        public decimal TotalCigarettes { get; set; }

        // 檳榔
        public decimal? AvgBetelNut { get; set; }
        public int BetelNutDays { get; set; }
        public decimal TotalBetelNut { get; set; }

        // 三餐平均
        public decimal? AvgVegetables { get; set; }
        public decimal? AvgProtein { get; set; }
        public decimal? AvgCarbs { get; set; }
    }

    // 圖表數據
    public class ChartData
    {
        public List<ChartPoint> BloodPressureData { get; set; } = new();
        public List<ChartPoint> BloodSugarData { get; set; } = new();
        public List<ChartPoint> WaterIntakeData { get; set; } = new();
        public List<ChartPoint> ExerciseDurationData { get; set; } = new();

        //  抽菸檳榔數據
        public List<ChartPoint> CigarettesData { get; set; } = new();
        public List<ChartPoint> BetelNutData { get; set; } = new();

        public List<MealRecord> MealRecords { get; set; } = new();
        public List<BeverageRecord> BeverageRecords { get; set; } = new();

        //  週/月三餐統計
        public MealSummary WeeklyMealSummary { get; set; }
        public MealSummary MonthlyMealSummary { get; set; }
        public MealSummary YearlyMealSummary { get; set; }
    }

    //  三餐統計摘要
    public class MealSummary
    {
        public decimal TotalVegetables { get; set; }
        public decimal TotalProtein { get; set; }
        public decimal TotalCarbs { get; set; }
        public decimal AvgVegetables { get; set; }
        public decimal AvgProtein { get; set; }
        public decimal AvgCarbs { get; set; }
        public int DaysWithMeals { get; set; }
    }

    // 圖表點
    public class ChartPoint
    {
        public string Date { get; set; }
        public decimal? Value { get; set; }
        public decimal? Value2 { get; set; } // 用於血壓的舒張壓
        public bool IsAbnormal { get; set; }
    }

    public class MealRecord
    {
        public string Date { get; set; }
        public string Meals { get; set; }  // 保留舊格式(給 PDF 用)
        public MealStatistics MealData { get; set; }  //  新格式(給前端用)
    }

    //  三餐統計資料結構
    public class MealStatistics
    {
        public List<string> Vegetables { get; set; } = new();
        public List<string> Protein { get; set; } = new();
        public List<string> Carbs { get; set; } = new();
    }



    // 飲料記錄
    public class BeverageRecord
    {
        public string Date { get; set; }
        public string Beverage { get; set; }
    }

    // 週報表驗證請求
    public class WeeklyReportRequest
    {
        public string IDNumber { get; set; }
        public string ReportDate { get; set; } // 格式: 2025-01-01
    }
}