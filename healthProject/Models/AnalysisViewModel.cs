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
        // 平均值
        public decimal? AvgSystolicBP { get; set; }
        public decimal? AvgDiastolicBP { get; set; }
        public decimal? AvgBloodSugar { get; set; }
        public decimal? AvgWaterIntake { get; set; }
        public decimal? AvgExerciseDuration { get; set; }
        public decimal? AvgCigarettes { get; set; }

        // 異常天數
        public int TotalDays { get; set; }
        public int HighBPDays { get; set; }
        public int HighBloodSugarDays { get; set; }
        public int LowWaterDays { get; set; }
        public int LowExerciseDays { get; set; }

        // 異常百分比
        public decimal HighBPPercentage => TotalDays > 0 ? (decimal)HighBPDays / TotalDays * 100 : 0;
        public decimal HighBloodSugarPercentage => TotalDays > 0 ? (decimal)HighBloodSugarDays / TotalDays * 100 : 0;
        public decimal LowWaterPercentage => TotalDays > 0 ? (decimal)LowWaterDays / TotalDays * 100 : 0;
        public decimal LowExercisePercentage => TotalDays > 0 ? (decimal)LowExerciseDays / TotalDays * 100 : 0;
    }

    // 圖表數據
    public class ChartData
    {
        public List<ChartPoint> BloodPressureData { get; set; } = new();
        public List<ChartPoint> BloodSugarData { get; set; } = new();
        public List<ChartPoint> WaterIntakeData { get; set; } = new();
        public List<ChartPoint> ExerciseDurationData { get; set; } = new();
        public List<MealRecord> MealRecords { get; set; } = new();
        public List<BeverageRecord> BeverageRecords { get; set; } = new();
    }

    // 圖表點
    public class ChartPoint
    {
        public string Date { get; set; }
        public decimal? Value { get; set; }
        public decimal? Value2 { get; set; } // 用於血壓的舒張壓
        public bool IsAbnormal { get; set; }
    }

    // 三餐記錄
    public class MealRecord
    {
        public string Date { get; set; }
        public string Meals { get; set; }
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