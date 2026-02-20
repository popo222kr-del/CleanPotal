using System;

namespace CleanPotal
{
    public enum PortalTargetType
    {
        Folder = 0,
        File = 1,
        Url = 2
    }

    public sealed class PortalButtonItem
    {
        public string Title { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public PortalTargetType TargetType { get; set; } = PortalTargetType.Folder;
    }

    public sealed class HandoverItem
    {
        public DateTime Date { get; set; } = DateTime.Today;
        public string Title { get; set; } = "";
        public string Writer { get; set; } = "";
        public string Status { get; set; } = "진행";

        public string DateText => $"{Date:yyyy-MM-dd}";
    }
}