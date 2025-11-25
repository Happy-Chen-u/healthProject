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
    }
}