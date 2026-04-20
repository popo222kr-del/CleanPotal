using System;
using System.Collections.ObjectModel;
using System.ComponentModel; // 🔥 추가됨

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

        // 🔥 [추가] 성적서 템플릿 보관 그릇
        public ObservableCollection<VendorTemplateModel> Templates { get; set; } = new();
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

    // 🔥 [신규 추가] 성적서 템플릿 기준정보 모델
    public class VendorTemplateModel : INotifyPropertyChanged
    {
        private bool _isUsed = true;
        public bool IsUsed { get => _isUsed; set { _isUsed = value; OnPropertyChanged(); } }

        private string _itemCode = "";
        public string ItemCode { get => _itemCode; set { _itemCode = value; OnPropertyChanged(); } }

        private string _templatePath = "";
        public string TemplatePath { get => _templatePath; set { _templatePath = value; OnPropertyChanged(); } }

        private string _basePath = "";
        public string BasePath { get => _basePath; set { _basePath = value; OnPropertyChanged(); } }

        private string _fileNameRule = "반출일_품목명_도면명_순번";
        public string FileNameRule { get => _fileNameRule; set { _fileNameRule = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // 🔥 [복구 완료] 세정팀 교대 근무 스케줄 모델
    public class ShiftScheduleModel
    {
        public int Id { get; set; }
        public DateTime TargetDate { get; set; }
        public string TeamGroup { get; set; } = ""; // "세정" or "QA"
        public string Role { get; set; } = "";      // "세정팀장", "사원" 등
        public string MemberName { get; set; } = "";
        public string ShiftType { get; set; } = ""; // "주간", "야간", "휴무", "연차" 등
    }

    // 🔥 [복구 완료] 교육 계획 마스터 모델
    public class EducationPlanModel
    {
        public int Id { get; set; }
        public string MemberName { get; set; } = "";
        public string CourseName { get; set; } = ""; // 교육
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "";     // "대기", "진행중", "완료"
        public int Progress { get; set; }            // 0 ~ 100
        public string EduMethod { get; set; } = "";  // "이러닝", "집합", "사외"
    }
}