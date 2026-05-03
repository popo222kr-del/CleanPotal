using System;
using System.Linq;
using System.Windows.Controls;
using CleanPotal.FieldInspection.Repositories;

namespace CleanPotal
{
    public partial class FieldChecklistView : UserControl
    {
        public FieldChecklistView()
        {
            InitializeComponent();
            this.Loaded += (s, e) => RefreshDashboardCounters();
        }

        /// <summary>
        /// 1단계: 카드 4개에 안전한 기본 집계만 표시.
        /// 위치/체크시트가 아직 등록되지 않은 환경에서도 0 으로 정상 표기됨.
        /// </summary>
        public void RefreshDashboardCounters()
        {
            try
            {
                var locations = FieldInspectionRepository.GetLocations(onlyActive: true);
                int total = locations.Count;

                var today = DateTime.Today;
                var records = FieldInspectionRepository.SearchRecords(today, today, null, null, null);

                int done = records.Count(r => r.OverallStatus != "IN_PROGRESS");
                int abnormal = records.Count(r => r.OverallStatus == "ABNORMAL");
                int pending = Math.Max(0, total - done);

                StatTotalText.Text = total.ToString();
                StatDoneText.Text = done.ToString();
                StatPendingText.Text = pending.ToString();
                StatAbnormalText.Text = abnormal.ToString();
            }
            catch
            {
                StatTotalText.Text = "0";
                StatDoneText.Text = "0";
                StatPendingText.Text = "0";
                StatAbnormalText.Text = "0";
            }
        }
    }
}
