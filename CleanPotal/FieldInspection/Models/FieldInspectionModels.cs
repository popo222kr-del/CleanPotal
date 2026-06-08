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
        // 짧고 고유한 식별 코드 (예: "5S-METAL", "SAFETY-INJECTION") — 체크시트가 늘어나도 헷갈리지 않게 참조용으로 사용
        public string Code { get; set; } = "";
        // 분류 (예: "5S 점검", "안전 점검", "설비 순회점검") — 관리 화면에서 그룹핑용
        public string Category { get; set; } = "";
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
        // 구분 (예: "검사실", "세정실") — 데일리 화면에서 항목을 섹션 헤더로 묶어서 표시
        public string SectionName { get; set; } = "";
        // 교대 (예: "주간", "야간") — 같은 항목을 교대별로 별도 입력해야 할 때 사용. 비워두면 교대 구분 없음
        public string ShiftLabel { get; set; } = "";
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
        // 점검 대상 일자 (yyyy-MM-dd) — 같은 날 같은 체크시트의 중복 제출을 막고 "오늘 기록" 조회를 단순화
        public DateTime CheckDate { get; set; } = DateTime.Today;
        // 교대 (예: "주간", "야간") — 체크시트 항목에 ShiftLabel이 있는 경우 함께 기록
        public string ShiftLabel { get; set; } = "";
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
