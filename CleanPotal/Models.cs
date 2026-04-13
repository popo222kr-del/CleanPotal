using System;
using System.Collections.ObjectModel;

namespace CleanPotal
{
    // --- [포털 메뉴 관리 모델] ---
    public enum PortalTargetType { Folder = 0, File = 1, Url = 2 }

    public sealed class PortalButtonItem
    {
        public string Title { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public PortalTargetType TargetType { get; set; } = PortalTargetType.Folder;
    }

    // --- [업체 및 배차 관리 공용 모델] ---
    public class VendorModel
    {
        public string VendorName { get; set; } = "";
        public string Category { get; set; } = "일반";
        public ObservableCollection<AddressModel> Addresses { get; set; } = new();
        public ObservableCollection<ManagerModel> Managers { get; set; } = new();
    }

    public class AddressModel
    {
        public bool IsMain { get; set; }
        public string LocationName { get; set; } = "";
        public string FullAddress { get; set; } = "";
    }

    public class ManagerModel
    {
        public string ManagerName { get; set; } = "";
        public string ContactNumber { get; set; } = "";
    }

    // 🔥 [신규 추가] 세정팀 교대 근무 스케줄 모델
    public class ShiftScheduleModel
    {
        public int Id { get; set; }
        public DateTime TargetDate { get; set; }
        public string TeamGroup { get; set; } = ""; // "세정" or "QA"
        public string Role { get; set; } = "";      // "세정팀장", "사원" 등
        public string MemberName { get; set; } = "";
        public string ShiftType { get; set; } = ""; // "주간", "야간", "휴무", "연차" 등
    }

    // 🔥 [신규 추가] 교육 계획 마스터 모델
    public class EducationPlanModel
    {
        public int Id { get; set; }
        public string MemberName { get; set; } = "";
        public string CourseName { get; set; } = ""; // 교육명
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "대기";   // "대기", "진행", "완료"
        public int Progress { get; set; } = 0;       // 진행률(%)
        public string EduMethod { get; set; } = "";  // "이러닝", "집합" 등
    }
}