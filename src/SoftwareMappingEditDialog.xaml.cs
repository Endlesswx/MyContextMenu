using System;
using System.Windows;

namespace ContextMenuManager
{
    public partial class SoftwareMappingEditDialog : Window
    {
        public string Keyword { get; private set; }
        public string SoftwareName { get; private set; }

        public SoftwareMappingEditDialog(string keyword, string softwareName)
        {
            InitializeComponent();

            if (!string.IsNullOrEmpty(keyword))
            {
                TxtKeyword.Text = keyword;
                TxtSoftware.Text = softwareName;
                Title = "编辑映射规则";
            }
            else
            {
                Title = "新增映射规则";
            }

            Loaded += delegate { TxtKeyword.Focus(); };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string keyword = (TxtKeyword.Text ?? "").Trim();
            string software = (TxtSoftware.Text ?? "").Trim();

            if (keyword.Length == 0)
            {
                MessageBox.Show(this, "请输入关键字。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtKeyword.Focus();
                return;
            }

            if (software.Length == 0)
            {
                MessageBox.Show(this, "请输入软件名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtSoftware.Focus();
                return;
            }

            // 检查关键字中是否包含不合适的字符
            if (keyword.IndexOfAny(new[] { '<', '>', '&', '"', '\'' }) >= 0)
            {
                MessageBox.Show(this, "关键字不能包含特殊字符（< > & \" '）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtKeyword.Focus();
                return;
            }

            Keyword = keyword;
            SoftwareName = software;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
