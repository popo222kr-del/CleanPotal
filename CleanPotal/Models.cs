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
        public ObservableCollection<AddressModel> Addresses { get; set; } = new ObservableCollection<AddressModel>();
        public ObservableCollection<ManagerModel> Managers { get; set; } = new ObservableCollection<ManagerModel>();
    }

    public class AddressModel
    {
        public bool IsMain { get; set; } // HandoverView 등에서 사용
        public string LocationName { get; set; } = "";
        public string FullAddress { get; set; } = "";
    }

    public class ManagerModel
    {
        public string ManagerName { get; set; } = "";
        public string ContactNumber { get; set; } = "";
    }
}