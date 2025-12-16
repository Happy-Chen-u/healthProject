namespace healthProject.Models
{
    public class MissedRecordViewModel
    {
        public int UserId { get; set; }
        public string IDNumber { get; set; }
        public string FullName { get; set; }
        public DateTime BirthDate { get; set; }
        public string Gender { get; set; }
        public string PhoneNumber { get; set; }
        public int MissedDays { get; set; }
        public DateTime? LastRecordDate { get; set; }
        public string MissedReason { get; set; }
        public string LineUserId { get; set; }

        //  722 追蹤狀態
        public bool Is722Tracking { get; set; }

        //動態血壓缺失狀態
        public DateTime? ReportDate { get; set; } // 檢查的日期
        public bool IsMorningMissing { get; set; } // 上午未填
        public bool IsEveningMissing { get; set; } // 睡前未填
        public bool IsBothMissing { get; set; } // 上午和睡前均未填

        public decimal? MorningSystolic1 { get; set; }
        public decimal? MorningDiastolic1 { get; set; }
        public decimal? MorningSystolic2 { get; set; }
        public decimal? MorningDiastolic2 { get; set; }
        public decimal? EveningSystolic1 { get; set; }
        public decimal? EveningDiastolic1 { get; set; }
        public decimal? EveningSystolic2 { get; set; }
        public decimal? EveningDiastolic2 { get; set; }

        //  計算平均血壓
        public string MorningBPDisplay
        {
            get
            {
                if (!MorningSystolic1.HasValue && !MorningSystolic2.HasValue)
                    return "--";

                var readings = new List<string>();
                if (MorningSystolic1.HasValue && MorningDiastolic1.HasValue)
                    readings.Add($"{MorningSystolic1:0}/{MorningDiastolic1:0}");
                if (MorningSystolic2.HasValue && MorningDiastolic2.HasValue)
                    readings.Add($"{MorningSystolic2:0}/{MorningDiastolic2:0}");

                return readings.Any() ? string.Join(", ", readings) : "--";
            }
        }

        public string EveningBPDisplay
        {
            get
            {
                if (!EveningSystolic1.HasValue && !EveningSystolic2.HasValue)
                    return "--";

                var readings = new List<string>();
                if (EveningSystolic1.HasValue && EveningDiastolic1.HasValue)
                    readings.Add($"{EveningSystolic1:0}/{EveningDiastolic1:0}");
                if (EveningSystolic2.HasValue && EveningDiastolic2.HasValue)
                    readings.Add($"{EveningSystolic2:0}/{EveningDiastolic2:0}");

                return readings.Any() ? string.Join(", ", readings) : "--";
            }
        }
    }
}