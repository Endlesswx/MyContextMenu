using System;
using System.Windows;

namespace ContextMenuManager
{
    public partial class SoftwareNameInputDialog : Window
    {
        public string SoftwareName { get; private set; }

        public SoftwareNameInputDialog(string currentSoftware)
        {
            InitializeComponent();

            if (!string.IsNullOrEmpty(currentSoftware))
            {
                TxtSoftware.Text = currentSoftware;
            }

            Loaded += delegate
            {
                TxtSoftware.Focus();
                TxtSoftware.SelectAll();
            };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string software = (TxtSoftware.Text ?? "").Trim();

            if (software.Length == 0)
            {
                MessageBox.Show(this, "请输入软件名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtSoftware.Focus();
                return;
            }

            SoftwareName = software;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
