using healthProject.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using Svg.Skia;
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
        // 🖼️ SVG 字串 → PNG bytes（SkiaSharp + Svg.Skia）
        // ========================================
        private byte[] SvgToPng(string svgContent, int width, int height)
        {
            using var svg = new SKSvg();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
            svg.Load(stream);

            var info = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            if (svg.Picture != null)
            {
                float scaleX = width / svg.Picture.CullRect.Width;
                float scaleY = height / svg.Picture.CullRect.Height;
                canvas.Scale(scaleX, scaleY);
                canvas.DrawPicture(svg.Picture);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        // ========================================
        // 📋 頁首
        // ========================================
        private void ComposeHeader(IContainer container)
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
        private void ComposeContent(IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Spacing(15);

                column.Item().Element(c => ComposeBasicInfo(c, analysis));
                column.Item().Element(c => ComposeStatistics(c, analysis));

                if (analysis.Charts.BloodPressureData.Any())
                    column.Item().Element(c => ComposeBloodPressureChart(c, analysis));

                if (analysis.Charts.BloodSugarData.Any())
                    column.Item().Element(c => ComposeBarChart(c,
                        "血糖趨勢",
                        analysis.Charts.BloodSugarData,
                        "mg/dL",
                        $"標準值：≤ {analysis.Goals?.FastingGlucoseTarget ?? 100} mg/dL",
                        (float)(analysis.Goals?.FastingGlucoseTarget ?? 100),
                        "#ef9a9a", "#a5d6a7"));

                if (analysis.Charts.WaterIntakeData.Any())
                    column.Item().Element(c => ComposeBarChart(c,
                        "飲水量趨勢",
                        analysis.Charts.WaterIntakeData,
                        "ml",
                        "建議量：≥ 2000 ml",
                        2000f,
                        "#ffcc80", "#80deea"));

                if (analysis.Charts.ExerciseDurationData.Any())
                    column.Item().Element(c => ComposeBarChart(c,
                        "運動時間趨勢",
                        analysis.Charts.ExerciseDurationData,
                        "分鐘",
                        "建議量：≥ 150 分鐘/週",
                        150f,
                        "#ffcc80", "#a5d6a7"));

                if (analysis.Charts.CigarettesData?.Any() == true)
                    column.Item().Element(c => ComposeBarChart(c,
                        "抽菸趨勢",
                        analysis.Charts.CigarettesData,
                        "支",
                        "建議值：0 支（請戒菸）",
                        0f,
                        "#ef9a9a", "#ef9a9a"));

                if (analysis.Charts.BetelNutData?.Any() == true)
                    column.Item().Element(c => ComposeBarChart(c,
                        "檳榔趨勢",
                        analysis.Charts.BetelNutData,
                        "次",
                        "建議值：0 次（請戒除）",
                        0f,
                        "#ef9a9a", "#ef9a9a"));

                if (analysis.Charts.BeverageRecords.Any())
                    column.Item().Element(c => ComposeBeverageTable(c, analysis));

                if (analysis.Charts.MealRecords.Any())
                    column.Item().Element(c => ComposeMealTable(c, analysis));

                column.Item().Element(c => ComposeHealthAdvice(c, analysis));
            });
        }

        // ========================================
        // 📝 基本資訊
        // ========================================
        private void ComposeBasicInfo(IContainer container, AnalysisViewModel analysis)
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

                column.Item().Text($"產生時間: {DateTime.Now:yyyy/MM/dd HH:mm}").FontSize(10);
            });
        }

        // ========================================
        // 📈 統計摘要
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

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("項目").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("平均值").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("異常天數").Bold();
                        header.Cell().Background(Colors.Blue.Lighten3).Padding(5).Text("比例").Bold();
                    });

                    AddStatRow(table, "血壓",
                        $"{stats.AvgSystolicBP:F1}/{stats.AvgDiastolicBP:F1} mmHg",
                        $"{stats.HighBPDays}/{stats.TotalDays}", stats.HighBPPercentage);
                    AddStatRow(table, "血糖",
                        $"{stats.AvgBloodSugar:F1} mg/dL",
                        $"{stats.HighBloodSugarDays}/{stats.TotalDays}", stats.HighBloodSugarPercentage);
                    AddStatRow(table, "飲水量",
                        $"{stats.AvgWaterIntake:F0} ml",
                        $"{stats.LowWaterDays}/{stats.TotalDays}", stats.LowWaterPercentage);
                    AddStatRow(table, "運動時間",
                        $"{stats.AvgExerciseDuration:F1} 分鐘",
                        $"{stats.LowExerciseDays}/{stats.TotalDays}", stats.LowExercisePercentage);

                    if (stats.AvgCigarettes > 0)
                        AddStatRow(table, "抽菸",
                            $"{stats.AvgCigarettes:F1} 支/天 (總 {stats.TotalCigarettes:F0} 支)", "-", null);
                    if (stats.AvgBetelNut > 0)
                        AddStatRow(table, "檳榔",
                            $"{stats.AvgBetelNut:F1} 次/天 (總 {stats.TotalBetelNut:F0} 次)", "-", null);
                    if (stats.AvgVegetables > 0 || stats.AvgProtein > 0 || stats.AvgCarbs > 0)
                    {
                        AddStatRow(table, "蔬菜", $"{stats.AvgVegetables:F1} 份/天", "-", null);
                        AddStatRow(table, "蛋白質", $"{stats.AvgProtein:F1} 份/天", "-", null);
                        AddStatRow(table, "澱粉", $"{stats.AvgCarbs:F1} 份/天", "-", null);
                    }
                });
            });
        }

        private void AddStatRow(TableDescriptor table, string label, string value,
            string abnormalDays, decimal? percentage)
        {
            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(label);
            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(value);
            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(abnormalDays);

            if (percentage.HasValue)
                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                    .Text($"{percentage:F1}%")
                    .FontColor(percentage > 30 ? Colors.Red.Medium : Colors.Green.Medium);
            else
                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("-");
        }

        // ========================================
        // 📊 血壓折線圖（SVG → PNG）
        // ========================================
        private void ComposeBloodPressureChart(IContainer container, AnalysisViewModel analysis)
        {
            var svgStr = GenerateBloodPressureSvg(analysis);
            var pngBytes = SvgToPng(svgStr, 1040, 320);

            container.Column(column =>
            {
                column.Item().PaddingTop(5).PaddingBottom(4).Text("血壓趨勢").FontSize(14).Bold();
                column.Item().Image(pngBytes).FitWidth();
            });
        }

        private string GenerateBloodPressureSvg(AnalysisViewModel analysis)
        {
            var data = analysis.Charts.BloodPressureData;
            if (!data.Any()) return "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>";

            float svgW = 1040f, svgH = 320f;
            float padL = 70f, padR = 60f, padT = 20f, padB = 70f;
            float chartW = svgW - padL - padR;
            float chartH = svgH - padT - padB;

            var allValues = data.SelectMany(d => new[] { d.Value ?? 0, d.Value2 ?? 0 })
                                .Where(v => v > 0).ToList();
            if (!allValues.Any()) return "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>";

            float minV = (float)allValues.Min() - 10f;
            float maxV = (float)allValues.Max() + 10f;
            if (minV < 0) minV = 0;
            float range = maxV - minV;
            if (range == 0) range = 1;

            float xStep = data.Count > 1 ? chartW / (data.Count - 1) : chartW / 2;
            float toY(float v) => padT + chartH - ((v - minV) / range * chartH);
            float toX(int i) => padL + (data.Count > 1 ? i * xStep : chartW / 2);

            var svg = new StringBuilder();
            svg.AppendLine($"<svg width=\"{svgW}\" height=\"{svgH}\" xmlns=\"http://www.w3.org/2000/svg\">");

            // 背景
            svg.AppendLine($"<rect x=\"{padL}\" y=\"{padT}\" width=\"{chartW}\" height=\"{chartH}\" fill=\"#fafafa\" stroke=\"#ccc\" stroke-width=\"1\"/>");

            // Y軸格線 + 刻度
            for (int i = 0; i <= 5; i++)
            {
                float v = minV + range * i / 5f;
                float y = toY(v);
                svg.AppendLine($"<line x1=\"{padL}\" y1=\"{y:F0}\" x2=\"{padL + chartW}\" y2=\"{y:F0}\" stroke=\"#e0e0e0\" stroke-width=\"1\" stroke-dasharray=\"4,4\"/>");
                svg.AppendLine($"<text x=\"{padL - 8}\" y=\"{y + 5:F0}\" text-anchor=\"end\" font-size=\"16\" fill=\"#555\">{v:F0}</text>");
            }

            // 標準線 120
            if (120f >= minV && 120f <= maxV + 10)
            {
                float y120 = toY(120f);
                svg.AppendLine($"<line x1=\"{padL}\" y1=\"{y120:F0}\" x2=\"{padL + chartW}\" y2=\"{y120:F0}\" stroke=\"#e53935\" stroke-width=\"2\" stroke-dasharray=\"8,4\"/>");
                svg.AppendLine($"<text x=\"{padL + chartW + 5}\" y=\"{y120 + 5:F0}\" font-size=\"15\" fill=\"#e53935\" font-weight=\"bold\">120</text>");
            }

            // 標準線 80
            if (80f >= minV && 80f <= maxV + 10)
            {
                float y80 = toY(80f);
                svg.AppendLine($"<line x1=\"{padL}\" y1=\"{y80:F0}\" x2=\"{padL + chartW}\" y2=\"{y80:F0}\" stroke=\"#1e88e5\" stroke-width=\"2\" stroke-dasharray=\"8,4\"/>");
                svg.AppendLine($"<text x=\"{padL + chartW + 5}\" y=\"{y80 + 5:F0}\" font-size=\"15\" fill=\"#1e88e5\" font-weight=\"bold\">80</text>");
            }

            // 折線
            var sysPoints = new List<(float x, float y)>();
            var diaPoints = new List<(float x, float y)>();
            for (int i = 0; i < data.Count; i++)
            {
                if (data[i].Value.HasValue) sysPoints.Add((toX(i), toY((float)data[i].Value.Value)));
                if (data[i].Value2.HasValue) diaPoints.Add((toX(i), toY((float)data[i].Value2.Value)));
            }

            if (sysPoints.Count > 1)
                svg.AppendLine($"<polyline points=\"{string.Join(" ", sysPoints.Select(p => $"{p.x:F0},{p.y:F0}"))}\" stroke=\"#e53935\" stroke-width=\"3\" fill=\"none\"/>");
            if (diaPoints.Count > 1)
                svg.AppendLine($"<polyline points=\"{string.Join(" ", diaPoints.Select(p => $"{p.x:F0},{p.y:F0}"))}\" stroke=\"#1e88e5\" stroke-width=\"3\" fill=\"none\"/>");

            // 資料點 + 數值
            for (int i = 0; i < data.Count; i++)
            {
                float x = toX(i);
                if (data[i].Value.HasValue)
                {
                    float y = toY((float)data[i].Value.Value);
                    svg.AppendLine($"<circle cx=\"{x:F0}\" cy=\"{y:F0}\" r=\"6\" fill=\"#e53935\" stroke=\"white\" stroke-width=\"2\"/>");
                    svg.AppendLine($"<text x=\"{x:F0}\" y=\"{y - 10:F0}\" text-anchor=\"middle\" font-size=\"15\" fill=\"#c62828\" font-weight=\"bold\">{data[i].Value:F0}</text>");
                }
                if (data[i].Value2.HasValue)
                {
                    float y = toY((float)data[i].Value2.Value);
                    svg.AppendLine($"<circle cx=\"{x:F0}\" cy=\"{y:F0}\" r=\"6\" fill=\"#1e88e5\" stroke=\"white\" stroke-width=\"2\"/>");
                    svg.AppendLine($"<text x=\"{x:F0}\" y=\"{y + 22:F0}\" text-anchor=\"middle\" font-size=\"15\" fill=\"#1565c0\" font-weight=\"bold\">{data[i].Value2:F0}</text>");
                }
                svg.AppendLine($"<text x=\"{x:F0}\" y=\"{padT + chartH + 20:F0}\" text-anchor=\"middle\" font-size=\"14\" fill=\"#444\">{data[i].Date}</text>");
            }

            // 軸線
            svg.AppendLine($"<line x1=\"{padL}\" y1=\"{padT}\" x2=\"{padL}\" y2=\"{padT + chartH}\" stroke=\"#888\" stroke-width=\"2\"/>");
            svg.AppendLine($"<line x1=\"{padL}\" y1=\"{padT + chartH}\" x2=\"{padL + chartW}\" y2=\"{padT + chartH}\" stroke=\"#888\" stroke-width=\"2\"/>");

            // Y軸標題
            float midY = padT + chartH / 2;
            svg.AppendLine($"<text x=\"20\" y=\"{midY:F0}\" text-anchor=\"middle\" font-size=\"14\" fill=\"#666\" transform=\"rotate(-90, 20, {midY:F0})\">mmHg</text>");

            // 圖例
            float legY = padT + chartH + 42f;
            svg.AppendLine($"<rect x=\"{padL}\" y=\"{legY}\" width=\"20\" height=\"5\" fill=\"#e53935\" rx=\"2\"/>");
            svg.AppendLine($"<text x=\"{padL + 26}\" y=\"{legY + 8}\" font-size=\"14\" fill=\"#333\">收縮壓</text>");
            svg.AppendLine($"<rect x=\"{padL + 90}\" y=\"{legY}\" width=\"20\" height=\"5\" fill=\"#1e88e5\" rx=\"2\"/>");
            svg.AppendLine($"<text x=\"{padL + 116}\" y=\"{legY + 8}\" font-size=\"14\" fill=\"#333\">舒張壓</text>");
            svg.AppendLine($"<line x1=\"{padL + 185}\" y1=\"{legY + 2}\" x2=\"{padL + 210}\" y2=\"{legY + 2}\" stroke=\"#e53935\" stroke-width=\"2\" stroke-dasharray=\"6,3\"/>");
            svg.AppendLine($"<text x=\"{padL + 216}\" y=\"{legY + 8}\" font-size=\"14\" fill=\"#e53935\">標準線 120 mmHg</text>");
            svg.AppendLine($"<line x1=\"{padL + 400}\" y1=\"{legY + 2}\" x2=\"{padL + 425}\" y2=\"{legY + 2}\" stroke=\"#1e88e5\" stroke-width=\"2\" stroke-dasharray=\"6,3\"/>");
            svg.AppendLine($"<text x=\"{padL + 431}\" y=\"{legY + 8}\" font-size=\"14\" fill=\"#1e88e5\">標準線 80 mmHg</text>");

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        // ========================================
        // 📊 通用長條圖（SVG → PNG）
        // ========================================
        private void ComposeBarChart(IContainer container, string title,
            List<ChartPoint> data, string unit, string hintText,
            float standardLine, string abnormalColor, string normalColor)
        {
            var svgStr = GenerateBarChartSvg(data, unit, standardLine, abnormalColor, normalColor);
            var pngBytes = SvgToPng(svgStr, 1040, 290);

            container.Column(column =>
            {
                column.Item().PaddingTop(5).PaddingBottom(4).Text(title).FontSize(14).Bold();
                column.Item().Image(pngBytes).FitWidth();
                column.Item().Text(hintText).FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }

        private string GenerateBarChartSvg(List<ChartPoint> data, string unit,
            float standardLine, string abnormalColor, string normalColor)
        {
            if (!data.Any()) return "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>";

            float svgW = 1040f, svgH = 290f;
            float padL = 70f, padR = 60f, padT = 20f, padB = 55f;
            float chartW = svgW - padL - padR;
            float chartH = svgH - padT - padB;

            float maxV = (float)data.Max(d => d.Value ?? 0);
            float topV = Math.Max(maxV, standardLine) * 1.25f;
            if (topV == 0) topV = 10f;

            float gap = chartW / data.Count;
            float barW = Math.Min(gap * 0.55f, 60f);

            float toY(float v) => padT + chartH - (v / topV * chartH);
            float toX(int i) => padL + i * gap + gap / 2f;

            var svg = new StringBuilder();
            svg.AppendLine($"<svg width=\"{svgW}\" height=\"{svgH}\" xmlns=\"http://www.w3.org/2000/svg\">");

            // 背景
            svg.AppendLine($"<rect x=\"{padL}\" y=\"{padT}\" width=\"{chartW}\" height=\"{chartH}\" fill=\"#fafafa\" stroke=\"#ccc\" stroke-width=\"1\"/>");

            // Y軸格線 + 刻度
            for (int i = 0; i <= 4; i++)
            {
                float v = topV * i / 4f;
                float y = toY(v);
                svg.AppendLine($"<line x1=\"{padL}\" y1=\"{y:F0}\" x2=\"{padL + chartW}\" y2=\"{y:F0}\" stroke=\"#e0e0e0\" stroke-width=\"1\" stroke-dasharray=\"4,4\"/>");
                svg.AppendLine($"<text x=\"{padL - 8}\" y=\"{y + 5:F0}\" text-anchor=\"end\" font-size=\"16\" fill=\"#555\">{v:F0}</text>");
            }

            // 標準線
            if (standardLine > 0)
            {
                float sy = toY(standardLine);
                svg.AppendLine($"<line x1=\"{padL}\" y1=\"{sy:F0}\" x2=\"{padL + chartW}\" y2=\"{sy:F0}\" stroke=\"#ff7043\" stroke-width=\"2\" stroke-dasharray=\"8,4\"/>");
                svg.AppendLine($"<text x=\"{padL + chartW + 5}\" y=\"{sy + 5:F0}\" font-size=\"15\" fill=\"#ff7043\" font-weight=\"bold\">{standardLine:F0}</text>");
            }

            // 長條 + 數值 + X軸
            for (int i = 0; i < data.Count; i++)
            {
                float val = (float)(data[i].Value ?? 0);
                float cx = toX(i);
                float x = cx - barW / 2;
                float barH = val > 0 ? val / topV * chartH : 0;
                float y = toY(val);

                string color = data[i].IsAbnormal ? abnormalColor : normalColor;
                string borderColor = DarkenHex(color);

                svg.AppendLine($"<rect x=\"{x:F0}\" y=\"{y:F0}\" width=\"{barW:F0}\" height=\"{barH:F0}\" fill=\"{color}\" rx=\"4\" stroke=\"{borderColor}\" stroke-width=\"1.5\"/>");
                svg.AppendLine($"<text x=\"{cx:F0}\" y=\"{y - 6:F0}\" text-anchor=\"middle\" font-size=\"15\" fill=\"#333\" font-weight=\"bold\">{val:F0}</text>");
                svg.AppendLine($"<text x=\"{cx:F0}\" y=\"{padT + chartH + 20:F0}\" text-anchor=\"middle\" font-size=\"14\" fill=\"#444\">{data[i].Date}</text>");
            }

            // 軸線
            svg.AppendLine($"<line x1=\"{padL}\" y1=\"{padT}\" x2=\"{padL}\" y2=\"{padT + chartH}\" stroke=\"#888\" stroke-width=\"2\"/>");
            svg.AppendLine($"<line x1=\"{padL}\" y1=\"{padT + chartH}\" x2=\"{padL + chartW}\" y2=\"{padT + chartH}\" stroke=\"#888\" stroke-width=\"2\"/>");

            // Y軸單位
            float midY = padT + chartH / 2;
            svg.AppendLine($"<text x=\"20\" y=\"{midY:F0}\" text-anchor=\"middle\" font-size=\"14\" fill=\"#666\" transform=\"rotate(-90, 20, {midY:F0})\">{unit}</text>");

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        private string DarkenHex(string hex) => hex switch
        {
            "#ef9a9a" => "#e57373",
            "#a5d6a7" => "#81c784",
            "#ffcc80" => "#ffa726",
            "#80deea" => "#26c6da",
            _ => "#9e9e9e"
        };

        // ========================================
        // 📋 飲料記錄表格
        // ========================================
        private void ComposeBeverageTable(IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(5).Text("飲料記錄").FontSize(14).Bold();

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
        private void ComposeMealTable(IContainer container, AnalysisViewModel analysis)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(5).Text("三餐記錄").FontSize(14).Bold();

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
        private void ComposeHealthAdvice(IContainer container, AnalysisViewModel analysis)
        {
            var stats = analysis.Statistics;
            var advices = new List<string>();

            if (stats.HighBPPercentage > 30)
                advices.Add("血壓異常比例較高，建議定期監測並諮詢醫師調整用藥。");
            if (stats.HighBloodSugarPercentage > 30)
                advices.Add("血糖異常比例較高，建議控制飲食並遵循醫囑用藥。");
            if (stats.LowWaterPercentage > 50)
                advices.Add("飲水量不足天數較多，建議每日至少攝取 2000ml 水分。");
            if (stats.LowExercisePercentage > 50)
                advices.Add("運動時間不足，建議每週至少運動 150 分鐘。");
            if (stats.AvgCigarettes > 5)
                advices.Add("抽菸量偏高，建議尋求戒菸門診協助。");
            if (stats.AvgBetelNut > 5)
                advices.Add("檳榔使用量偏高，建議立即戒除並定期口腔檢查。");
            if (stats.AvgVegetables < 3)
                advices.Add("蔬菜攝取不足，建議每日至少攝取 3 份蔬菜。");

            if (!advices.Any())
                advices.Add("各項指標控制良好，請繼續保持！");

            container.Background(Colors.Yellow.Lighten4).Padding(10).Column(column =>
            {
                column.Item().Text("健康建議").FontSize(14).Bold();
                column.Spacing(5);
                foreach (var advice in advices)
                    column.Item().Text($"• {advice}").FontSize(11);
            });
        }

        // ========================================
        // 🛠️ 輔助方法
        // ========================================
        private string GetReportTypeName(ReportType type) => type switch
        {
            ReportType.Daily => "每日報表",
            ReportType.Weekly => "每週報表",
            ReportType.Monthly => "每月報表",
            ReportType.Yearly => "每年報表",
            _ => "報表"
        };

        private string MaskIdNumber(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber) || idNumber.Length < 10) return "***";
            return idNumber.Substring(0, 4) + "***" + idNumber.Substring(7);
        }
    }
}