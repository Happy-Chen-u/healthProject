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
    }
}