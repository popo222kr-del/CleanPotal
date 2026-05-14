using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace CleanPotal
{
    public class ScheduleBadge
    {
        public string Text { get; set; } = "";
        public SolidColorBrush BackgroundBrush { get; set; } = Brushes.Transparent;
        public SolidColorBrush TextBrush { get; set; } = Brushes.Black;
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;

        public int RecordId { get; set; } = 0;
        public string RecordType { get; set; } = "";
        public string GroupMembers { get; set; } = "";
        public string RecordOwner { get; set; } = "";

        // 🔥 신규: 마우스를 올렸을 때 나타날 툴팁(말풍선) 텍스트
        public string TooltipText { get; set; } = "";
    }

    public class CalendarDayModel
    {
        public DateTime Date { get; set; }
        public string DayNumber => Date.Day.ToString();
        public bool IsCurrentMonth { get; set; }
        public bool IsToday => Date.Date == DateTime.Today;
        public bool IsHoliday { get; set; }
        public string HolidayName { get; set; } = "";

        public ObservableCollection<ScheduleBadge> Badges { get; set; } = new();
        public ObservableCollection<ScheduleBadge> HeaderTeamEventBadges { get; set; } = new();
        public bool HasHeaderTeamEventBadges => HeaderTeamEventBadges.Count > 0;
    }

    public class TodayStatusItem
    {
        public string BadgeText { get; set; } = "";
        public SolidColorBrush BackgroundBrush { get; set; } = Brushes.Transparent;
        public SolidColorBrush TextBrush { get; set; } = Brushes.Black;
        public string MembersText { get; set; } = "";
    }

    public class TeamEvent
    {
        public int Id { get; set; }
        public string RegisteredBy { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string Content { get; set; } = "";
    }
}