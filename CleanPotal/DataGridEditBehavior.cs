using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CleanPotal
{
    /// <summary>
    /// DataGrid에 Single-Click 즉시 편집 + 셀 이동 시 SelectAll 기능을 부여하는 Attached Behavior.
    /// 사용법: local:DataGridEditBehavior.SingleClickEdit="True"
    /// </summary>
    public static class DataGridEditBehavior
    {
        public static readonly DependencyProperty SingleClickEditProperty =
            DependencyProperty.RegisterAttached(
                "SingleClickEdit",
                typeof(bool),
                typeof(DataGridEditBehavior),
                new PropertyMetadata(false, OnSingleClickEditChanged));

        public static bool GetSingleClickEdit(DependencyObject obj) =>
            (bool)obj.GetValue(SingleClickEditProperty);

        public static void SetSingleClickEdit(DependencyObject obj, bool value) =>
            obj.SetValue(SingleClickEditProperty, value);

        private static void OnSingleClickEditChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            if ((bool)e.NewValue)
            {
                // DataGridCell.GotKeyboardFocus가 DataGrid까지 버블링되는 시점에 핸들러 등록.
                // DataGridCell.OnGotKeyboardFocus(class handler)가 이미 실행되어 CurrentCell이
                // 설정된 이후이므로 BeginEdit()가 확실히 성공한다.
                grid.AddHandler(
                    DataGridCell.GotKeyboardFocusEvent,
                    new KeyboardFocusChangedEventHandler(OnCellGotKeyboardFocus));
                grid.PreparingCellForEdit += OnPreparingCellForEdit;
            }
            else
            {
                grid.RemoveHandler(
                    DataGridCell.GotKeyboardFocusEvent,
                    new KeyboardFocusChangedEventHandler(OnCellGotKeyboardFocus));
                grid.PreparingCellForEdit -= OnPreparingCellForEdit;
            }
        }

        private static void OnCellGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // e.NewFocus가 DataGridCell일 때만 처리(편집 TextBox 포커스 이벤트 버블링은 무시)
            if (e.NewFocus is not DataGridCell cell || cell.IsEditing || cell.IsReadOnly) return;
            ((DataGrid)sender).BeginEdit();
        }

        private static void OnPreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.EditingElement is not TextBox tb) return;
            // Input 우선순위로 지연 실행하여 편집 TextBox가 완전히 렌더링된 후 SelectAll 수행
            tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }));
        }
    }
}
