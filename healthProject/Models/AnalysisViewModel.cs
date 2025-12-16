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

    // 分析報表主 ViewModel
    public class AnalysisViewModel
    {
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