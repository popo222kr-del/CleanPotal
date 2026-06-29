using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CleanPotal.FieldInspection.Models;
using CleanPotal.FieldInspection.Repositories;
using CleanPotal.FieldInspection.Web;

namespace CleanPotal
{
    public partial class FieldChecklistView : UserControl
    {
        private readonly ObservableCollection<FieldChecklist> _checklists = new();
        private readonly ObservableCollection<FieldChecklistItem> _items = new();
        private FieldChecklist? _currentChecklist;
        private bool _suppressSelectionEvent;

        // 모든 화면 인스턴스가 같은 서버를 공유 (탭을 닫았다 열어도 서버는 유지)
        private static FieldInspectionWebServer? _webServer;

        public FieldChecklistView()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                RefreshDashboardCounters();
                LoadChecklistManagementTab();
                RefreshWebServerStatus();
            };
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

        // -----------------------------------------------------------------------
        // 체크시트 관리 탭
        // -----------------------------------------------------------------------
        private void LoadChecklistManagementTab()
        {
            try
            {
                // 위치 콤보 (미지정 옵션 포함)
                var locations = FieldInspectionRepository.GetLocations().ToList();
                locations.Insert(0, new FieldLocation { LocationId = 0, Name = "(위치 미지정)" });
                CmbChecklistLocation.ItemsSource = locations;

                LstChecklists.ItemsSource = _checklists;
                var view = (CollectionView)CollectionViewSource.GetDefaultView(_checklists);
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FieldChecklist.Category)));

                DgChecklistItems.ItemsSource = _items;

                RefreshChecklistCategories();
                RefreshChecklists();
                ClearChecklistForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"체크시트 목록을 불러오지 못했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshChecklistCategories()
        {
            var categories = FieldInspectionRepository.GetChecklistCategories();
            CmbChecklistCategory.ItemsSource = categories;
        }

        private void RefreshChecklists(long? selectChecklistId = null)
        {
            _suppressSelectionEvent = true;
            try
            {
                _checklists.Clear();
                foreach (var c in FieldInspectionRepository.GetChecklists())
                {
                    if (string.IsNullOrWhiteSpace(c.Category)) c.Category = "미분류";
                    _checklists.Add(c);
                }

                if (selectChecklistId.HasValue)
                {
                    LstChecklists.SelectedItem = _checklists.FirstOrDefault(c => c.ChecklistId == selectChecklistId.Value);
                }
            }
            finally
            {
                _suppressSelectionEvent = false;
            }

            if (LstChecklists.SelectedItem is FieldChecklist sel)
                LoadChecklistIntoForm(sel);
            else
                ClearChecklistForm();
        }

        private void LstChecklists_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvent) return;
            if (LstChecklists.SelectedItem is FieldChecklist c)
                LoadChecklistIntoForm(c);
        }

        private void LoadChecklistIntoForm(FieldChecklist c)
        {
            _currentChecklist = c;
            TxtChecklistFormTitle.Text = $"체크시트 정보 — {c.Name}";
            TxtChecklistCode.Text = c.Code;
            CmbChecklistCategory.Text = c.Category == "미분류" ? "" : c.Category;
            TxtChecklistName.Text = c.Name;
            CmbChecklistLocation.SelectedValue = c.LocationId ?? 0;
            SelectCycle(c.Cycle);
            ChkChecklistActive.IsChecked = c.IsActive;

            LoadItemsForChecklist(c.ChecklistId);
        }

        private void ClearChecklistForm()
        {
            _currentChecklist = null;
            TxtChecklistFormTitle.Text = "체크시트 정보 (새 체크시트)";
            TxtChecklistCode.Text = "";
            CmbChecklistCategory.Text = "";
            TxtChecklistName.Text = "";
            CmbChecklistLocation.SelectedValue = 0L;
            SelectCycle("DAILY");
            ChkChecklistActive.IsChecked = true;
            _items.Clear();
        }

        private void SelectCycle(string cycle)
        {
            foreach (var obj in CmbChecklistCycle.Items)
            {
                if (obj is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), cycle, StringComparison.OrdinalIgnoreCase))
                {
                    CmbChecklistCycle.SelectedItem = cbi;
                    return;
                }
            }
            CmbChecklistCycle.SelectedIndex = 0;
        }

        private void LoadItemsForChecklist(long checklistId)
        {
            _items.Clear();
            foreach (var item in FieldInspectionRepository.GetChecklistItems(checklistId))
                _items.Add(item);
        }

        private void BtnNewChecklist_Click(object sender, RoutedEventArgs e)
        {
            LstChecklists.SelectedItem = null;
            ClearChecklistForm();
            TxtChecklistCode.Focus();
        }

        private void BtnSaveChecklist_Click(object sender, RoutedEventArgs e)
        {
            if (SessionManager.BlockGuestEdit()) return;
            string code = TxtChecklistCode.Text.Trim();
            string name = TxtChecklistName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("체크시트 이름을 입력해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtChecklistName.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("체크시트 코드를 입력해주세요. (예: 5S-METAL)\n여러 체크시트를 등록·관리할 때 헷갈리지 않도록 짧고 고유한 코드를 권장합니다.",
                    "확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtChecklistCode.Focus();
                return;
            }

            long? locationId = (long?)CmbChecklistLocation.SelectedValue;
            if (locationId == 0) locationId = null;

            var cycle = (CmbChecklistCycle.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "DAILY";
            var category = CmbChecklistCategory.Text.Trim();

            try
            {
                if (_currentChecklist == null)
                {
                    var newItem = new FieldChecklist
                    {
                        Code = code,
                        Category = category,
                        Name = name,
                        LocationId = locationId,
                        Cycle = cycle,
                        IsActive = ChkChecklistActive.IsChecked == true
                    };
                    long id = FieldInspectionRepository.InsertChecklist(newItem);
                    RefreshChecklistCategories();
                    RefreshChecklists(id);
                }
                else
                {
                    _currentChecklist.Code = code;
                    _currentChecklist.Category = category;
                    _currentChecklist.Name = name;
                    _currentChecklist.LocationId = locationId;
                    _currentChecklist.Cycle = cycle;
                    _currentChecklist.IsActive = ChkChecklistActive.IsChecked == true;
                    FieldInspectionRepository.UpdateChecklist(_currentChecklist);
                    RefreshChecklistCategories();
                    RefreshChecklists(_currentChecklist.ChecklistId);
                }

                RefreshDashboardCounters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteChecklist_Click(object sender, RoutedEventArgs e)
        {
            if (SessionManager.BlockGuestEdit()) return;
            if (_currentChecklist == null)
            {
                MessageBox.Show("삭제할 체크시트를 목록에서 선택해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"'{_currentChecklist.Name}' 체크시트와 등록된 항목을 모두 삭제하시겠습니까?",
                    "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                FieldInspectionRepository.DeleteChecklist(_currentChecklist.ChecklistId);
                RefreshChecklistCategories();
                RefreshChecklists();
                RefreshDashboardCounters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 체크 항목
        // -----------------------------------------------------------------------
        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            if (SessionManager.BlockGuestEdit()) return;
            if (_currentChecklist == null)
            {
                MessageBox.Show("먼저 체크시트를 저장(또는 선택)한 뒤 항목을 추가해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var newItem = new FieldChecklistItem
            {
                ChecklistId = _currentChecklist.ChecklistId,
                OrderNo = (_items.Count == 0 ? 0 : _items.Max(i => i.OrderNo)) + 1,
                SectionName = "",
                ShiftLabel = "",
                Title = "새 점검 항목",
                InputType = FieldInspectionConstants.InputTypeOkNg,
                IsRequired = true
            };

            try
            {
                newItem.ItemId = FieldInspectionRepository.InsertChecklistItem(newItem);
                _items.Add(newItem);
                DgChecklistItems.SelectedItem = newItem;
                DgChecklistItems.ScrollIntoView(newItem);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"항목 추가 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (SessionManager.BlockGuestEdit()) return;
            var selected = DgChecklistItems.SelectedItems.Cast<FieldChecklistItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("삭제할 항목을 선택해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"선택한 {selected.Count}개 항목을 삭제하시겠습니까?",
                    "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                foreach (var item in selected)
                {
                    FieldInspectionRepository.DeleteChecklistItem(item.ItemId);
                    _items.Remove(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 모바일 데일리 체크 웹서버
        // -----------------------------------------------------------------------
        private void BtnToggleWebServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_webServer != null && _webServer.IsRunning)
                {
                    _webServer.Stop();
                }
                else
                {
                    _webServer ??= new FieldInspectionWebServer();
                    _webServer.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"모바일 페이지 서버를 시작/중지하는 중 오류가 발생했습니다:\n{ex.Message}\n\n" +
                    "휴대폰에서 접속 가능한 주소(0.0.0.0)로 열려면 관리자 권한이 필요할 수 있습니다.\n" +
                    "관리자 권한으로 실행하거나, 아래 명령으로 사전에 URL 예약을 해주세요.\n" +
                    $"netsh http add urlacl url=http://+:{(_webServer?.Port ?? 5180)}/ user=Everyone",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshWebServerStatus();
            }
        }

        private void BtnCopyWebUrl_Click(object sender, RoutedEventArgs e)
        {
            if (_webServer == null || !_webServer.IsRunning) return;
            try
            {
                Clipboard.SetText(_webServer.GetAccessUrl());
                MessageBox.Show("주소가 클립보드에 복사되었습니다. 휴대폰 브라우저 주소창에 붙여넣어 접속하세요.",
                    "복사됨", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void RefreshWebServerStatus()
        {
            bool running = _webServer?.IsRunning == true;
            WebServerDot.Fill = new System.Windows.Media.SolidColorBrush(
                running ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)
                        : System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));

            if (running)
            {
                if (_webServer!.IsLanAccessible)
                {
                    WebServerStatusText.Text = "모바일 데일리 체크 페이지: 켜짐 (사내망 접속 가능)";
                    WebServerUrlText.Text = $"휴대폰 브라우저에서 접속 ▶ {_webServer.GetAccessUrl()}  (이 PC가 켜져 있어야 하고, Windows 방화벽에서 {_webServer.Port}번 포트 인바운드를 허용해야 합니다)";
                }
                else
                {
                    WebServerDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
                    WebServerStatusText.Text = "모바일 데일리 체크 페이지: 켜짐 (이 PC에서만 접속 가능 — 휴대폰 접속 불가)";
                    WebServerUrlText.Text = $"권한 부족으로 외부 접속이 차단된 상태입니다. 관리자 권한으로 다음 명령을 한 번 실행한 뒤 앱을 재시작하세요:  netsh http add urlacl url=http://+:{_webServer.Port}/ user=Everyone";
                }
                BtnToggleWebServer.Content = "모바일 페이지 중지";
                BtnCopyWebUrl.IsEnabled = true;
            }
            else
            {
                WebServerStatusText.Text = "모바일 데일리 체크 페이지: 꺼짐";
                WebServerUrlText.Text = "시작하면 휴대폰에서 접속할 사내망 주소가 표시됩니다. (이 PC가 켜져 있을 때만 접속 가능)";
                BtnToggleWebServer.Content = "모바일 페이지 시작";
                BtnCopyWebUrl.IsEnabled = false;
            }
        }

        private void DgChecklistItems_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row?.Item is not FieldChecklistItem item) return;

            // 편집 값이 커밋된 뒤 저장
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { FieldInspectionRepository.UpdateChecklistItem(item); }
                catch (Exception ex)
                {
                    MessageBox.Show($"항목 저장 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
