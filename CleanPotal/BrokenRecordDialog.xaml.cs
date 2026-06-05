using System;
using System.Collections.Generic;
using System.Windows;

namespace CleanPotal
{
    public partial class BrokenRecordDialog : Window
    {
        public BrokenRecord? Result { get; private set; }

        public BrokenRecordDialog(IEnumerable<string> lines, IEnumerable<string> teams)
        {
            InitializeComponent();
            foreach (var l in lines) CmbLine.Items.Add(l);
            foreach (var t in teams) CmbTeam.Items.Add(t);
            DpOccurDate.SelectedDate = DateTime.Today;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (DpOccurDate.SelectedDate == null)
            {
                MessageBox.Show("발생일을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Result = new BrokenRecord
            {
                OccurDate   = DpOccurDate.SelectedDate,
                Line        = CmbLine.Text.Trim(),
                Team        = CmbTeam.Text.Trim(),
                ProductName = TxtProductName.Text.Trim(),
                SN          = TxtSN.Text.Trim(),
                ProductType = TxtProductType.Text.Trim(),
                Causer      = TxtCauser.Text.Trim(),
                JobTitle    = TxtJobTitle.Text.Trim(),
                OccurStage  = TxtOccurStage.Text.Trim(),
                Status      = TxtStatus.Text.Trim(),
                IsOfficial  = TxtIsOfficial.Text.Trim()
            };
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
