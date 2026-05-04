using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using CleanPotal.FieldInspection.Models;
using CleanPotal.FieldInspection.Repositories;
using CleanPotal.FieldInspection.Services;

namespace CleanPotal
{
    public partial class FieldChecklistView : UserControl
    {
        private readonly ObservableCollection<FieldLocation> _locations = new();
        private readonly ObservableCollection<FieldTag> _tags = new();

        public FieldChecklistView()
        {
            InitializeComponent();
            LocationsGrid.ItemsSource = _locations;
            TagsGrid.ItemsSource = _tags;

            this.Loaded += (s, e) =>
            {
                ReloadLocations();
                RefreshDashboardCounters();
            };
        }

        // ============================================================
        // 대시보드 카운터 (1단계에서 추가)
        // ============================================================

        public void RefreshDashboardCounters()
        {
            try
            {
                var locations = FieldInspectionRepository.GetLocations(onlyActive: true);
                int total = locations.Count;

                var today = DateTime.Today;
                var records = FieldInspectionRepository.SearchRecords(today, today, null, null, null);

                int done = records.Count(r => r.OverallStatus != FieldInspectionConstants.StatusInProgress);
                int abnormal = records.Count(r => r.OverallStatus == FieldInspectionConstants.StatusAbnormal);
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

        // ============================================================
        // 위치 목록 로드
        // ============================================================

        private void ReloadLocations(long? selectId = null)
        {
            _locations.Clear();
            var rows = FieldInspectionRepository.GetLocations(onlyActive: false);
            foreach (var r in rows) _locations.Add(r);
            LocCountText.Text = $"({_locations.Count})";

            if (selectId.HasValue)
            {
                var match = _locations.FirstOrDefault(l => l.LocationId == selectId.Value);
                if (match != null) LocationsGrid.SelectedItem = match;
            }
            else if (_locations.Count > 0 && LocationsGrid.SelectedItem == null)
            {
                LocationsGrid.SelectedIndex = 0;
            }
        }

        private FieldLocation? SelectedLocation => LocationsGrid.SelectedItem as FieldLocation;
        private FieldTag? SelectedTag => TagsGrid.SelectedItem as FieldTag;

        private void LocationsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ReloadTagsForSelectedLocation();
        }

        private void ReloadTagsForSelectedLocation()
        {
            _tags.Clear();
            QrPreviewImage.Source = null;
            QrUrlText.Text = "";

            var loc = SelectedLocation;
            if (loc == null)
            {
                TagSectionSubtitle.Text = "좌측에서 위치를 선택하세요.";
                return;
            }

            TagSectionSubtitle.Text = $"{loc.Code} · {loc.Name}";
            var rows = FieldInspectionRepository.GetTags(loc.LocationId);
            foreach (var t in rows) _tags.Add(t);

            if (_tags.Count > 0) TagsGrid.SelectedIndex = 0;
        }

        private void TagsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateQrPreview();
        }

        private void UpdateQrPreview()
        {
            var tag = SelectedTag;
            if (tag == null)
            {
                QrPreviewImage.Source = null;
                QrUrlText.Text = "";
                return;
            }

            try
            {
                string url = QrCodeService.BuildUrl(tag.TagId, tag.Token);
                QrPreviewImage.Source = QrCodeService.GenerateBitmap(url, 8);
                QrUrlText.Text = url;
            }
            catch (Exception ex)
            {
                QrPreviewImage.Source = null;
                QrUrlText.Text = "QR 생성 실패: " + ex.Message;
            }
        }

        // ============================================================
        // 위치 CRUD
        // ============================================================

        private void BtnAddLocation_Click(object sender, RoutedEventArgs e)
        {
            var win = new FieldLocationEditWindow { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                try
                {
                    long id = FieldInspectionRepository.InsertLocation(win.Result);
                    ReloadLocations(id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("위치 등록에 실패했습니다.\n(코드 중복 가능)\n\n" + ex.Message,
                        "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnEditLocation_Click(object sender, RoutedEventArgs e)
        {
            var loc = SelectedLocation;
            if (loc == null)
            {
                MessageBox.Show("수정할 위치를 선택하세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new FieldLocationEditWindow(loc) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                FieldInspectionRepository.UpdateLocation(win.Result);
                ReloadLocations(loc.LocationId);
            }
        }

        private void BtnDeleteLocation_Click(object sender, RoutedEventArgs e)
        {
            var loc = SelectedLocation;
            if (loc == null) return;

            var tags = FieldInspectionRepository.GetTags(loc.LocationId);
            if (tags.Count > 0)
            {
                MessageBox.Show($"이 위치에 등록된 태그가 {tags.Count}개 있습니다.\n태그를 먼저 삭제하거나 다른 위치로 이동해야 합니다.",
                    "삭제 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"[{loc.Code}] {loc.Name} 위치를 삭제하시겠습니까?",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            FieldInspectionRepository.DeleteLocation(loc.LocationId);
            ReloadLocations();
        }

        // ============================================================
        // 태그(NFC/QR) 발급 / 관리
        // ============================================================

        private void BtnIssueTag_Click(object sender, RoutedEventArgs e)
        {
            var loc = SelectedLocation;
            if (loc == null)
            {
                MessageBox.Show("먼저 위치를 선택하세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string tagId = QrCodeService.NewTagId();
            string token = QrCodeService.ComputeToken(tagId);
            string url = QrCodeService.BuildUrl(tagId, token);

            var tag = new FieldTag
            {
                TagId = tagId,
                LocationId = loc.LocationId,
                TagType = FieldInspectionConstants.TagTypeBoth,
                QrPayload = url,
                Token = token,
                IsActive = true,
                Memo = ""
            };

            try
            {
                FieldInspectionRepository.InsertTag(tag);
                ReloadTagsForSelectedLocation();
                var added = _tags.FirstOrDefault(t => t.TagId == tagId);
                if (added != null) TagsGrid.SelectedItem = added;
            }
            catch (Exception ex)
            {
                MessageBox.Show("태그 발급에 실패했습니다.\n" + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnToggleTagActive_Click(object sender, RoutedEventArgs e)
        {
            var tag = SelectedTag;
            if (tag == null) return;

            tag.IsActive = !tag.IsActive;
            FieldInspectionRepository.UpdateTag(tag);

            ReloadTagsForSelectedLocation();
            SelectTag(tag.TagId);
        }

        private void BtnReissueToken_Click(object sender, RoutedEventArgs e)
        {
            var tag = SelectedTag;
            if (tag == null) return;

            if (MessageBox.Show("이 태그의 토큰을 재발급하시겠습니까?\n기존 NFC/QR 은 즉시 사용할 수 없게 됩니다.",
                "토큰 재발급", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            tag.Token = QrCodeService.ComputeToken(tag.TagId + "|" + DateTime.Now.Ticks);
            tag.QrPayload = QrCodeService.BuildUrl(tag.TagId, tag.Token);
            FieldInspectionRepository.UpdateTag(tag);

            ReloadTagsForSelectedLocation();
            SelectTag(tag.TagId);
        }

        private void SelectTag(string tagId)
        {
            var match = _tags.FirstOrDefault(t => t.TagId == tagId);
            if (match != null) TagsGrid.SelectedItem = match;
        }

        private void BtnDeleteTag_Click(object sender, RoutedEventArgs e)
        {
            var tag = SelectedTag;
            if (tag == null) return;

            if (MessageBox.Show($"태그 [{tag.TagId}] 를 삭제하시겠습니까?",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            FieldInspectionRepository.DeleteTag(tag.TagId);
            ReloadTagsForSelectedLocation();
        }

        private void BtnSaveQr_Click(object sender, RoutedEventArgs e)
        {
            var tag = SelectedTag;
            if (tag == null) return;

            string url = QrCodeService.BuildUrl(tag.TagId, tag.Token);
            var dlg = new SaveFileDialog
            {
                Filter = "PNG 이미지 (*.png)|*.png",
                FileName = $"QR_{tag.TagId}.png"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                QrCodeService.SavePng(url, dlg.FileName, 12);
                MessageBox.Show($"QR 이미지를 저장했습니다.\n{dlg.FileName}",
                    "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("QR 저장에 실패했습니다.\n" + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            var tag = SelectedTag;
            if (tag == null) return;

            try
            {
                string url = QrCodeService.BuildUrl(tag.TagId, tag.Token);
                Clipboard.SetText(url);
                MessageBox.Show($"URL을 클립보드에 복사했습니다.\n\n{url}",
                    "복사", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("복사 실패: " + ex.Message,
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================================
        // 서버 주소 설정
        // ============================================================

        private void BtnServerSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new FieldInspectionSettingsWindow { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                UpdateQrPreview();
                MessageBox.Show("서버 주소가 변경되었습니다.\n기존 태그의 URL/QR 도 새 주소 기준으로 갱신됩니다.",
                    "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
