using healthProject.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;  



namespace healthProject.Services
{
    public class ReportService
    {
        public ReportService()
        {
            // 設定 QuestPDF 授權 (社群版免費)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // ========================================
        // 📄 產生 PDF 報表
        // ========================================
        public byte[] GeneratePdfReport(AnalysisViewModel analysis)
        {
            // ✅ 明確指定使用 QuestPDF 的 Document
            return QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontFamily("Microsoft JhengHei"));

                    // 頁首
                    page.Header().Element(ComposeHeader);

                    // 內容
                    page.Content().Element(c => ComposeContent(c, analysis));

                    // 頁尾
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        // ========================================
        // 📋 頁首
        // ========================================
        private void ComposeHeader(QuestPDF.Infrastructure.IContainer container)
        {
            container.Column(column =>
            {
                column.Item().AlignCenter().Text("輔仁大學附設醫院")
                    .FontSize(20).Bold().FontColor(Colors.Blue.Darken2);

                column.Item().PaddingTop(5).AlignCenter()
                    .Text("代謝症候群管理系統 - 健康報表")
                    .FontSize(16).SemiBold();

                column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });
        }

        // ========================================
        // 📊 內容
        // ========================================
        private void ComposeContent(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Spacing(15);

                // 基本資訊
                column.Item().Element(c => ComposeBasicInfo(c, analysis));

                // 統計摘要
                column.Item().Element(c => ComposeStatistics(c, analysis));

                // 圖表區域
                if (analysis.Charts.BloodPressureData.Any())
                    column.Item().Element(c => ComposeBloodPressureChart(c, analysis));

                if (analysis.Charts.BloodSugarData.Any())
                    column.Item().Element(c => ComposeBloodSugarChart(c, analysis));

                if (analysis.Charts.WaterIntakeData.Any())
                    column.Item().Element(c => ComposeWaterIntakeChart(c, analysis));

                if (analysis.Charts.ExerciseDurationData.Any())
                    column.Item().Element(c => ComposeExerciseChart(c, analysis));

                // 飲料記錄表格
                if (analysis.Charts.BeverageRecords.Any())
                    column.Item().Element(c => ComposeBeverageTable(c, analysis));

                // 三餐記錄表格
                if (analysis.Charts.MealRecords.Any())
                    column.Item().Element(c => ComposeMealTable(c, analysis));

                // 健康建議
                column.Item().Element(c => ComposeHealthAdvice(c, analysis));
            });
        }

        // ========================================
        // 📝 基本資訊
        // ========================================
        private void ComposeBasicInfo(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Background(Colors.Grey.Lighten3).Padding(10).Column(column =>
            {
                column.Spacing(5);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"姓名: {analysis.PatientName}").FontSize(12);
                    row.RelativeItem().Text($"身分證: {MaskIdNumber(analysis.IDNumber)}").FontSize(12);
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"報表類型: {GetReportTypeName(analysis.ReportType)}").FontSize(12);
                    row.RelativeItem().Text($"期間: {analysis.StartDate:yyyy/MM/dd} ~ {analysis.EndDate:yyyy/MM/dd}").FontSize(12);
                });

                column.Item().Text($"產生時間: {DateTime.Now:yyyy/MM/dd HH:mm}").FontSize(10).Italic();
            });
        }

        // ========================================
        // 📈 統計摘要
        // ========================================
        private void ComposeStatistics(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            var stats = analysis.Statistics;

            container.Column(column =>
            {
                column.Item().PaddingBottom(5).Text("統計摘要").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    // 表頭
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("項目").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("平均值").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("異常天數").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("異常比例").Bold();
                    });

                    // 血壓
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("血壓");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.AvgSystolicBP:F1}/{stats.AvgDiastolicBP:F1} mmHg");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.HighBPDays}/{stats.TotalDays} 天");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.HighBPPercentage:F1}%")
                        .FontColor(stats.HighBPPercentage > 30 ? Colors.Red.Medium : Colors.Green.Medium);

                    // 血糖
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("血糖");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.AvgBloodSugar:F1} mg/dL");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.HighBloodSugarDays}/{stats.TotalDays} 天");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.HighBloodSugarPercentage:F1}%")
                        .FontColor(stats.HighBloodSugarPercentage > 30 ? Colors.Red.Medium : Colors.Green.Medium);

                    // 飲水量
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("飲水量");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.AvgWaterIntake:F0} ml");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.LowWaterDays}/{stats.TotalDays} 天");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.LowWaterPercentage:F1}%")
                        .FontColor(stats.LowWaterPercentage > 30 ? Colors.Red.Medium : Colors.Green.Medium);

                    // 運動時間
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("運動時間");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.AvgExerciseDuration:F1} 分鐘");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.LowExerciseDays}/{stats.TotalDays} 天");
                    table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                        .Text($"{stats.LowExercisePercentage:F1}%")
                        .FontColor(stats.LowExercisePercentage > 30 ? Colors.Red.Medium : Colors.Green.Medium);
                });
            });
        }

        // ========================================
        // 📊 血壓圖表 (折線圖版本)
        // ========================================
        private void ComposeBloodPressureChart(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("血壓趨勢").FontSize(14).Bold();

                column.Item().Height(150).Canvas((canvasObj, size) =>
                {
                    // ✅ 明確轉型為 SKCanvas
                    var canvas = (SKCanvas)canvasObj;

                    var data = analysis.Charts.BloodPressureData;
                    if (!data.Any()) return;

                    var width = size.Width;
                    var height = size.Height;
                    var padding = 30f;

                    // 計算比例
                    var maxValue = Math.Max(
                        data.Max(d => d.Value ?? 0),
                        data.Max(d => d.Value2 ?? 0)
                    );
                    var minValue = Math.Min(
                        data.Min(d => d.Value ?? 999),
                        data.Min(d => d.Value2 ?? 999)
                    );
                    var valueRange = maxValue - minValue + 20;
                    var valueRangeFloat = (float)valueRange;

                    // 繪製座標軸
                    canvas.DrawLine(
                        new SKPoint(padding, padding),
                        new SKPoint(padding, height - padding),
                        new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = true }
                    );

                    canvas.DrawLine(
                        new SKPoint(padding, height - padding),
                        new SKPoint(width - padding, height - padding),
                        new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = true }
                    );

                    // 繪製標準線 (120/80)
                    if (minValue < 120 && maxValue > 80)
                    {
                        var y120 = height - padding - ((float)(120 - minValue) / valueRangeFloat * (height - 2 * padding));
                        canvas.DrawLine(
                            new SKPoint(padding, y120),
                            new SKPoint(width - padding, y120),
                            new SKPaint
                            {
                                Color = SKColors.Red,
                                StrokeWidth = 1,
                                PathEffect = SKPathEffect.CreateDash(new[] { 5f, 5f }, 0),
                                IsAntialias = true
                            }
                        );
                    }

                    // 繪製折線
                    if (data.Count > 1)
                    {
                        var stepX = (width - 2 * padding) / (data.Count - 1);

                        for (int i = 0; i < data.Count - 1; i++)
                        {
                            var x1 = padding + i * stepX;
                            var x2 = padding + (i + 1) * stepX;

                            // 收縮壓 (藍色)
                            if (data[i].Value.HasValue && data[i + 1].Value.HasValue)
                            {
                                var y1 = height - padding - ((float)(data[i].Value.Value - minValue) / valueRangeFloat * (height - 2 * padding));
                                var y2 = height - padding - ((float)(data[i + 1].Value.Value - minValue) / valueRangeFloat * (height - 2 * padding));

                                canvas.DrawLine(
                                    new SKPoint(x1, y1),
                                    new SKPoint(x2, y2),
                                    new SKPaint { Color = SKColors.Blue, StrokeWidth = 2, IsAntialias = true }
                                );

                                // 繪製數據點
                                canvas.DrawCircle(x1, y1, 3, new SKPaint { Color = SKColors.Blue, IsAntialias = true });
                            }

                            // 舒張壓 (綠色)
                            if (data[i].Value2.HasValue && data[i + 1].Value2.HasValue)
                            {
                                var y1 = height - padding - ((float)(data[i].Value2.Value - minValue) / valueRangeFloat * (height - 2 * padding));
                                var y2 = height - padding - ((float)(data[i + 1].Value2.Value - minValue) / valueRangeFloat * (height - 2 * padding));

                                canvas.DrawLine(
                                    new SKPoint(x1, y1),
                                    new SKPoint(x2, y2),
                                    new SKPaint { Color = SKColors.Green, StrokeWidth = 2, IsAntialias = true }
                                );

                                // 繪製數據點
                                canvas.DrawCircle(x1, y1, 3, new SKPaint { Color = SKColors.Green, IsAntialias = true });
                            }
                        }

                        // 繪製最後一個點
                        if (data.Count > 0)
                        {
                            var lastIndex = data.Count - 1;
                            var lastX = padding + lastIndex * stepX;

                            if (data[lastIndex].Value.HasValue)
                            {
                                var lastY = height - padding - ((float)(data[lastIndex].Value.Value - minValue) / valueRangeFloat * (height - 2 * padding));
                                canvas.DrawCircle(lastX, lastY, 3, new SKPaint { Color = SKColors.Blue, IsAntialias = true });
                            }

                            if (data[lastIndex].Value2.HasValue)
                            {
                                var lastY = height - padding - ((float)(data[lastIndex].Value2.Value - minValue) / valueRangeFloat * (height - 2 * padding));
                                canvas.DrawCircle(lastX, lastY, 3, new SKPaint { Color = SKColors.Green, IsAntialias = true });
                            }
                        }
                    }
                });

                column.Item().PaddingTop(5).Text("🔵 收縮壓  🟢 舒張壓  🔴 標準線 120/80 mmHg")
                    .FontSize(9).Italic();
            });
        }

        // ========================================
        // 📊 血糖圖表
        // ========================================
        private void ComposeBloodSugarChart(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("血糖趨勢").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in analysis.Charts.BloodSugarData)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    foreach (var point in analysis.Charts.BloodSugarData)
                    {
                        var color = point.IsAbnormal ? Colors.Red.Lighten3 : Colors.Green.Lighten3;
                        table.Cell().Background(color).Border(1).Padding(3).Column(col =>
                        {
                            col.Item().AlignCenter().Text(point.Date).FontSize(8);
                            col.Item().AlignCenter().Text($"{point.Value:F1}").FontSize(10).Bold();
                        });
                    }
                });

                column.Item().PaddingTop(5).Text("⚠️ 標準值: ≤99 mg/dL")
                    .FontSize(9).Italic();
            });
        }

        // ========================================
        // 📊 飲水量圖表
        // ========================================
        private void ComposeWaterIntakeChart(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("飲水量趨勢").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in analysis.Charts.WaterIntakeData)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    foreach (var point in analysis.Charts.WaterIntakeData)
                    {
                        var color = point.IsAbnormal ? Colors.Orange.Lighten3 : Colors.Blue.Lighten3;
                        table.Cell().Background(color).Border(1).Padding(3).Column(col =>
                        {
                            col.Item().AlignCenter().Text(point.Date).FontSize(8);
                            col.Item().AlignCenter().Text($"{point.Value:F0}ml").FontSize(10).Bold();
                        });
                    }
                });

                column.Item().PaddingTop(5).Text("💧 建議量: ≥2000 ml")
                    .FontSize(9).Italic();
            });
        }

        // ========================================
        // 📊 運動時間圖表
        // ========================================
        private void ComposeExerciseChart(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("運動時間趨勢").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in analysis.Charts.ExerciseDurationData)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    foreach (var point in analysis.Charts.ExerciseDurationData)
                    {
                        var color = point.IsAbnormal ? Colors.Orange.Lighten3 : Colors.Green.Lighten3;
                        table.Cell().Background(color).Border(1).Padding(3).Column(col =>
                        {
                            col.Item().AlignCenter().Text(point.Date).FontSize(8);
                            col.Item().AlignCenter().Text($"{point.Value:F0}分").FontSize(10).Bold();
                        });
                    }
                });

                column.Item().PaddingTop(5).Text("🏃 建議量: ≥150 分鐘/週")
                    .FontSize(9).Italic();
            });
        }

        // ========================================
        // 📋 飲料記錄表格
        // ========================================
        private void ComposeBeverageTable(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("飲料記錄").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(80);
                        columns.RelativeColumn();
                    });

                    // 表頭
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("日期").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("飲料").Bold();
                    });

                    // 資料
                    foreach (var record in analysis.Charts.BeverageRecords)
                    {
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(record.Date);
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(record.Beverage);
                    }
                });
            });
        }

        // ========================================
        // 📋 三餐記錄表格
        // ========================================
        private void ComposeMealTable(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("三餐記錄").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(80);
                        columns.RelativeColumn();
                    });

                    // 表頭
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Green.Lighten3).Padding(5).Text("日期").Bold();
                        header.Cell().Background(Colors.Green.Lighten3).Padding(5).Text("三餐內容").Bold();
                    });

                    // 資料
                    foreach (var record in analysis.Charts.MealRecords)
                    {
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(record.Date);
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(record.Meals);
                    }
                });
            });
        }

        // ========================================
        // 💡 健康建議
        // ========================================
        private void ComposeHealthAdvice(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            var stats = analysis.Statistics;
            var advices = new List<string>();

            // 根據統計數據產生建議
            if (stats.HighBPPercentage > 30)
                advices.Add("⚠️ 血壓異常比例較高,建議定期監測並諮詢醫師調整用藥。");

            if (stats.HighBloodSugarPercentage > 30)
                advices.Add("⚠️ 血糖異常比例較高,建議控制飲食並遵循醫囑用藥。");

            if (stats.LowWaterPercentage > 50)
                advices.Add("💧 飲水量不足天數較多,建議每日至少攝取 2000ml 水分。");

            if (stats.LowExercisePercentage > 50)
                advices.Add("🏃 運動時間不足,建議每週至少運動 150 分鐘。");

            if (stats.AvgCigarettes > 5)
                advices.Add("🚭 抽菸量偏高,建議尋求戒菸門診協助。");

            if (!advices.Any())
                advices.Add("✅ 各項指標控制良好,請繼續保持!");

            container.Background(Colors.Yellow.Lighten4).Padding(10).Column(column =>
            {
                column.Item().Text("健康建議").FontSize(14).Bold();
                column.Spacing(5);

                foreach (var advice in advices)
                {
                    column.Item().Text($"• {advice}").FontSize(11);
                }
            });
        }

        // ========================================
        // 🛠️ 輔助方法
        // ========================================
        private string GetReportTypeName(ReportType type)
        {
            return type switch
            {
                ReportType.Daily => "每日報表",
                ReportType.Weekly => "每週報表",
                ReportType.Monthly => "每月報表",
                ReportType.Yearly => "每年報表",
                _ => "報表"
            };
        }

        private string MaskIdNumber(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber) || idNumber.Length < 10)
                return "***";
            return idNumber.Substring(0, 4) + "***" + idNumber.Substring(7);
        }
    }
}