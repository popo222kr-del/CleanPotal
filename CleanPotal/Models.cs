using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

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
    }
}