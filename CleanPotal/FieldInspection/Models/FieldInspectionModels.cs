using System;
using System.Collections.Generic;

namespace CleanPotal.FieldInspection.Models
{
    public class FieldLocation
    {
        public long LocationId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Zone { get; set; } = "";
        public string Equipment { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public string Memo { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class FieldTag
    {
        public string TagId { get; set; } = "";
        public long LocationId { get; set; }
        public string TagType { get; set; } = "BOTH";
        public string QrPayload { get; set; } = "";
        public string Token { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public string Memo { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class FieldChecklist
    {
        public long ChecklistId { get; set; }
        public string Name { get; set; } = "";
        public long? LocationId { get; set; }
        public string Cycle { get; set; } = "DAILY";
        public bool IsActive { get; set; } = true;
        public string Memo { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class FieldChecklistItem
    {
        public long ItemId { get; set; }
        public long ChecklistId { get; set; }
        public int OrderNo { get; set; }
        public string Title { get; set; } = "";
        public string InputType { get; set; } = "OK_NG";
        public string UnitOrHint { get; set; } = "";
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public bool IsRequired { get; set; } = true;
        public string Memo { get; set; } = "";
    }

    public class FieldInspectionRecord
    {
        public string RecordId { get; set; } = Guid.NewGuid().ToString("N");
        public string TagId { get; set; } = "";
        public long LocationId { get; set; }
        public long ChecklistId { get; set; }
        public string InspectorName { get; set; } = "";
        public string InspectorId { get; set; } = "";
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public string OverallStatus { get; set; } = "IN_PROGRESS";
        public string Note { get; set; } = "";
        public string ClientIp { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<FieldInspectionRecordItem> Items { get; set; } = new();
    }

    public class FieldInspectionRecordItem
    {
        public long RecordItemId { get; set; }
        public string RecordId { get; set; } = "";
        public long ItemId { get; set; }
        public string ResultText { get; set; } = "";
        public bool IsAbnormal { get; set; }
        public string Comment { get; set; } = "";
    }

    public class FieldInspectionAttachment
    {
        public long AttachmentId { get; set; }
        public string RecordId { get; set; } = "";
        public long? RecordItemId { get; set; }
        public string FileName { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long ByteSize { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public static class FieldInspectionConstants
    {
        public const string StatusInProgress = "IN_PROGRESS";
        public const string StatusNormal = "NORMAL";
        public const string StatusAbnormal = "ABNORMAL";

        public const string InputTypeOkNg = "OK_NG";
        public const string InputTypeYesNo = "YES_NO";
        public const string InputTypeNumber = "NUMBER";
        public const string InputTypeText = "TEXT";

        public const string TagTypeNfc = "NFC";
        public const string TagTypeQr = "QR";
        public const string TagTypeBoth = "BOTH";

        public const string CycleDaily = "DAILY";
        public const string CycleWeekly = "WEEKLY";
        public const string CycleMonthly = "MONTHLY";
    }
}
