using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace CleanPotal
{
    public enum PortalTargetType { Folder = 0, File = 1, Url = 2 }

    public sealed class PortalButtonItem
    {
        public string Title { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public PortalTargetType TargetType { get; set; } = PortalTargetType.Folder;
    }

    public class VendorModel : INotifyPropertyChanged
    {
        private string _vendorName = "";
        public string VendorName { get => _vendorName; set { _vendorName = value; OnPropertyChanged(); } }

        private string _category = "일반";
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }

        private string _basePath = "";
        public string BasePath { get => _basePath; set { _basePath = value; OnPropertyChanged(); } }

        public ObservableCollection<AddressModel> Addresses { get; set; } = new();
        public ObservableCollection<ManagerModel> Managers { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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

    // 🔥 [신규] 시스템 전역 공통 템플릿 모델
    public class GlobalTemplateModel : INotifyPropertyChanged
    {
        private string _productCode = "";
        public string ProductCode { get => _productCode; set { _productCode = value; OnPropertyChanged(); } }

        private string _productName = "";
        public string ProductName { get => _productName; set { _productName = value; OnPropertyChanged(); } }

        private string _templatePath = "";
        public string TemplatePath { get => _templatePath; set { _templatePath = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ShiftScheduleModel
    {
        public int Id { get; set; }
        public DateTime TargetDate { get; set; }
        public string TeamGroup { get; set; } = "";
        public string Role { get; set; } = "";
        public string MemberName { get; set; } = "";
        public string ShiftType { get; set; } = "";
    }

    public class EducationPlanModel
    {
        public int Id { get; set; }
        public string MemberName { get; set; } = "";
        public string CourseName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "";
        public int Progress { get; set; }
        public string EduMethod { get; set; } = "";
        public string AttachmentPath { get; set; } = "";
    }

    public class WorkAssignmentMember : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string RealName { get; set; } = "";
        public string TeamName { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string HireDate { get; set; } = "";
        public string Email { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string EmployeeNumber { get; set; } = "";

        public string InitialChar => string.IsNullOrEmpty(RealName) ? "?" : RealName.Substring(0, 1);

        public string CareerStr
        {
            get
            {
                if (string.IsNullOrEmpty(HireDate) || !DateTime.TryParse(HireDate, out var hire)) return "-";
                var today = DateTime.Today;
                int years = today.Year - hire.Year;
                int months = today.Month - hire.Month;
                if (months < 0) { years--; months += 12; }
                if (years < 0) return "-";
                if (years == 0) return $"{months}개월";
                if (months == 0) return $"{years}년";
                return $"{years}년 {months}개월";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class EduBasicItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";

        private string _eduName = "";
        public string EduName { get => _eduName; set { _eduName = value; OnPropertyChanged(); } }

        private string _eduDate = "";
        public string EduDate { get => _eduDate; set { _eduDate = value; OnPropertyChanged(); } }

        private string _instructor = "";
        public string Instructor { get => _instructor; set { _instructor = value; OnPropertyChanged(); } }

        private string _note = "";
        public string Note { get => _note; set { _note = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AccountItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";

        private string _serviceName = "";
        public string ServiceName { get => _serviceName; set { _serviceName = value; OnPropertyChanged(); } }

        private string _accountId = "";
        public string AccountId { get => _accountId; set { _accountId = value; OnPropertyChanged(); } }

        private string _accountPassword = "";
        public string AccountPassword { get => _accountPassword; set { _accountPassword = value; OnPropertyChanged(); } }

        private string _note = "";
        public string Note { get => _note; set { _note = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class EduCombinedRow : INotifyPropertyChanged
    {
        public bool IsManual { get; set; }
        public string Source => IsManual ? "직접입력" : "대시보드";

        // Common identity
        public string Username { get; set; } = "";
        public int EduId { get; set; }  // used for synced rows (EducationPlan.Id)

        private string _eduName = "";
        public string EduName { get => _eduName; set { _eduName = value; OnPropertyChanged(); } }

        private string _eduDate = "";
        public string EduDate { get => _eduDate; set { _eduDate = value; OnPropertyChanged(); } }

        private string _endDate = "";
        public string EndDate { get => _endDate; set { _endDate = value; OnPropertyChanged(); } }

        private string _instructor = "";
        public string Instructor { get => _instructor; set { _instructor = value; OnPropertyChanged(); } }

        private string _status = "";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private string _note = "";
        public string Note { get => _note; set { _note = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class EduDashboardRow : INotifyPropertyChanged
    {
        public int EduId { get; set; }
        public string MemberName { get; set; } = "";
        public string Username { get; set; } = "";
        public string EmployeeNumber { get; set; } = "";
        public string HireDate { get; set; } = "";
        public string CareerStr
        {
            get
            {
                if (string.IsNullOrEmpty(HireDate) || !DateTime.TryParse(HireDate, out var hire)) return "-";
                var today = DateTime.Today;
                int years = today.Year - hire.Year;
                int months = today.Month - hire.Month;
                if (months < 0) { years--; months += 12; }
                if (years < 0) return "-";
                if (years == 0) return $"{months}개월";
                if (months == 0) return $"{years}년";
                return $"{years}년 {months}개월";
            }
        }
        public string TeamName { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string CourseName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        private string _status = "";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressStr)); OnPropertyChanged(nameof(CompletedSortKey)); }
        }

        public int CompletedSortKey => (Status == "완료" || Status == "취소") ? 1 : 0;

        public int Progress { get; set; }
        public string EduMethod { get; set; } = "";

        private string _attachmentPath = "";
        public string AttachmentPath
        {
            get => _attachmentPath;
            set { _attachmentPath = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasAttachment)); OnPropertyChanged(nameof(AttachmentLabel)); }
        }
        public bool HasAttachment => !string.IsNullOrEmpty(_attachmentPath);
        public string AttachmentLabel => HasAttachment ? Path.GetFileName(_attachmentPath) : "첨부";

        public string StartDateStr => StartDate.ToString("yyyy-MM-dd");
        public string EndDateStr => EndDate.ToString("yyyy-MM-dd");

        public int ProgressPercent
        {
            get
            {
                if (Status == "완료") return 100;
                if (Status == "취소" || Status == "대기") return 0;
                var start = StartDate.Date;
                var end = EndDate.Date;
                if (end <= start) return DateTime.Today >= end ? 100 : 0;
                var p = (int)Math.Round((DateTime.Today - start).TotalDays / (end - start).TotalDays * 100.0, MidpointRounding.AwayFromZero);
                return p < 0 ? 0 : (p > 100 ? 100 : p);
            }
        }

        public string ProgressStr => $"{ProgressPercent}%";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}