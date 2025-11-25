using healthProject.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;


namespace healthProject.Services
{
    public class ReportService
    {
        public ReportService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // ========================================
        // 📄 產生 PDF 報表
        // ========================================
        public byte[] GeneratePdfReport(AnalysisViewModel analysis)
        {
            return QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontFamily("Microsoft JhengHei"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(c => ComposeContent(c, analysis));
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

                // 統計摘要(包含檳榔、三餐)
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

                // 🆕 抽菸圖表
                if (analysis.Charts.CigarettesData?.Any() == true)
                    column.Item().Element(c => ComposeCigaretteChart(c, analysis));

                // 🆕 檳榔圖表
                if (analysis.Charts.BetelNutData?.Any() == true)
                    column.Item().Element(c => ComposeBetelNutChart(c, analysis));

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
        // 📈 統計摘要(完整版)
        // ========================================
        private void ComposeStatistics(IContainer container, AnalysisViewModel analysis)
        {
            var stats = analysis.Statistics;

            container.Column(column =>
            {
                column.Item().PaddingBottom(5).Text("統計摘要").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });

                    // 表頭
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("項目").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("平均值").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("異常天數").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("比例").Bold();
                    });

                    // 血壓
                    AddStatRow(table, "血壓",
                        $"{stats.AvgSystolicBP:F1}/{stats.AvgDiastolicBP:F1} mmHg",
                        $"{stats.HighBPDays}/{stats.TotalDays}",
                        stats.HighBPPercentage);

                    // 血糖
                    AddStatRow(table, "血糖",
                        $"{stats.AvgBloodSugar:F1} mg/dL",
                        $"{stats.HighBloodSugarDays}/{stats.TotalDays}",
                        stats.HighBloodSugarPercentage);

                    // 飲水量
                    AddStatRow(table, "飲水量",
                        $"{stats.AvgWaterIntake:F0} ml",
                        $"{stats.LowWaterDays}/{stats.TotalDays}",
                        stats.LowWaterPercentage);

                    // 運動時間
                    AddStatRow(table, "運動時間",
                        $"{stats.AvgExerciseDuration:F1} 分鐘",
                        $"{stats.LowExerciseDays}/{stats.TotalDays}",
                        stats.LowExercisePercentage);

                    // 抽菸
                    if (stats.AvgCigarettes > 0)
                    {
                        AddStatRow(table, "🚬 抽菸",
                            $"{stats.AvgCigarettes:F1} 支/天 (總 {stats.TotalCigarettes:F0} 支)",
                            "-",
                            null);
                    }

                    // 檳榔
                    if (stats.AvgBetelNut > 0)
                    {
                        AddStatRow(table, "🌿 檳榔",
                            $"{stats.AvgBetelNut:F1} 次/天 (總 {stats.TotalBetelNut:F0} 次)",
                            "-",
                            null);
                    }

                    // 三餐統計
                    if (stats.AvgVegetables > 0 || stats.AvgProtein > 0 || stats.AvgCarbs > 0)
                    {
                        AddStatRow(table, "🥬 蔬菜",
                            $"{stats.AvgVegetables:F1} 份/天", "-", null);
                        AddStatRow(table, "🥩 蛋白質",
                            $"{stats.AvgProtein:F1} 份/天", "-", null);
                        AddStatRow(table, "🍚 澱粉",
                            $"{stats.AvgCarbs:F1} 份/天", "-", null);
                    }
                });
            });
        }

        private void AddStatRow(TableDescriptor table, string label, string value, string abnormalDays, decimal? percentage)
        {
            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(label);
            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(value);
            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(abnormalDays);

            if (percentage.HasValue)
            {
                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                    .Text($"{percentage:F1}%")
                    .FontColor(percentage > 30 ? Colors.Red.Medium : Colors.Green.Medium);
            }
            else
            {
                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("-");
            }
        }

        // ========================================
        // 📊 血壓圖表
        // ========================================
        // ========================================
        // 📊 血壓圖表 (使用 SVG)
        // ========================================
        private void ComposeBloodPressureChart(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("血壓趨勢").FontSize(14).Bold();

                column.Item().Height(150).Svg(GenerateBloodPressureSvg(analysis));

                column.Item().PaddingTop(5).Text("🔵 收縮壓  🟢 舒張壓  🔴 標準線 120/80 mmHg")
                    .FontSize(9).Italic();
            });
        }

        private string GenerateBloodPressureSvg(AnalysisViewModel analysis)
        {
            var data = analysis.Charts.BloodPressureData;
            if (!data.Any()) return "<svg></svg>";

            var width = 500f;
            var height = 150f;
            var padding = 30f;

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

            var svg = new System.Text.StringBuilder();
            svg.AppendLine($"<svg width=\"{width}\" height=\"{height}\" xmlns=\"http://www.w3.org/2000/svg\">");

            // 繪製座標軸
            svg.AppendLine($"<line x1=\"{padding}\" y1=\"{padding}\" x2=\"{padding}\" y2=\"{height - padding}\" stroke=\"black\" stroke-width=\"1\"/>");
            svg.AppendLine($"<line x1=\"{padding}\" y1=\"{height - padding}\" x2=\"{width - padding}\" y2=\"{height - padding}\" stroke=\"black\" stroke-width=\"1\"/>");

            // 繪製標準線 (120/80)
            if (minValue < 120 && maxValue > 80)
            {
                var y120 = height - padding
    - ((120f - (float)minValue) / valueRangeFloat * (height - 2f * padding));
                svg.AppendLine($"<line x1=\"{padding}\" y1=\"{y120}\" x2=\"{width - padding}\" y2=\"{y120}\" stroke=\"red\" stroke-width=\"1\" stroke-dasharray=\"5,5\"/>");
            }

            // 繪製折線和資料點
            if (data.Count > 1)
            {
                var stepX = (width - 2 * padding) / (data.Count - 1);

                var systolicPoints = new List<string>();
                var diastolicPoints = new List<string>();

                for (int i = 0; i < data.Count; i++)
                {
                    var x = padding + i * stepX;

                    // 收縮壓 (藍色)
                    if (data[i].Value.HasValue)
                    {
                        var y = height - padding - ((float)(data[i].Value.Value - minValue) / valueRangeFloat * (height - 2 * padding));
                        systolicPoints.Add($"{x},{y}");
                        svg.AppendLine($"<circle cx=\"{x}\" cy=\"{y}\" r=\"3\" fill=\"blue\"/>");
                    }

                    // 舒張壓 (綠色)
                    if (data[i].Value2.HasValue)
                    {
                        var y = height - padding - ((float)(data[i].Value2.Value - minValue) / valueRangeFloat * (height - 2 * padding));
                        diastolicPoints.Add($"{x},{y}");
                        svg.AppendLine($"<circle cx=\"{x}\" cy=\"{y}\" r=\"3\" fill=\"green\"/>");
                    }
                }

                // 繪製折線
                if (systolicPoints.Count > 1)
                {
                    svg.AppendLine($"<polyline points=\"{string.Join(" ", systolicPoints)}\" stroke=\"blue\" stroke-width=\"2\" fill=\"none\"/>");
                }

                if (diastolicPoints.Count > 1)
                {
                    svg.AppendLine($"<polyline points=\"{string.Join(" ", diastolicPoints)}\" stroke=\"green\" stroke-width=\"2\" fill=\"none\"/>");
                }
            }

            svg.AppendLine("</svg>");
            return svg.ToString();
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
        // 🆕 抽菸圖表
        // ========================================
        private void ComposeCigaretteChart(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("抽菸趨勢").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in analysis.Charts.CigarettesData)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    foreach (var point in analysis.Charts.CigarettesData)
                    {
                        var color = point.IsAbnormal ? Colors.Red.Lighten3 : Colors.Green.Lighten3;
                        table.Cell().Background(color).Border(1).Padding(3).Column(col =>
                        {
                            col.Item().AlignCenter().Text(point.Date).FontSize(8);
                            col.Item().AlignCenter().Text($"{point.Value:F0}支").FontSize(10).Bold();
                        });
                    }
                });

                column.Item().PaddingTop(5).Text("🚬 建議值: 0 支 (請戒菸)")
                    .FontSize(9).Italic();
            });
        }

        // ========================================
        // 🆕 檳榔圖表
        // ========================================
        private void ComposeBetelNutChart(QuestPDF.Infrastructure.IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10).Text("檳榔趨勢").FontSize(14).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in analysis.Charts.BetelNutData)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    foreach (var point in analysis.Charts.BetelNutData)
                    {
                        var color = point.IsAbnormal ? Colors.Red.Lighten3 : Colors.Green.Lighten3;
                        table.Cell().Background(color).Border(1).Padding(3).Column(col =>
                        {
                            col.Item().AlignCenter().Text(point.Date).FontSize(8);
                            col.Item().AlignCenter().Text($"{point.Value:F0}次").FontSize(10).Bold();
                        });
                    }
                });

                column.Item().PaddingTop(5).Text("🌿 建議值: 0 次 (請戒除)")
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

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("日期").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("飲料").Bold();
                    });

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

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Green.Lighten3).Padding(5).Text("日期").Bold();
                        header.Cell().Background(Colors.Green.Lighten3).Padding(5).Text("三餐內容").Bold();
                    });

                    foreach (var record in analysis.Charts.MealRecords)
                    {
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(record.Date);
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(record.Meals ?? "無記錄");
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

            if (stats.AvgBetelNut > 5)
                advices.Add("🌿 檳榔使用量偏高,建議立即戒除並定期口腔檢查。");

            if (stats.AvgVegetables < 3)
                advices.Add("🥬 蔬菜攝取不足,建議每日至少攝取 3 份蔬菜。");

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