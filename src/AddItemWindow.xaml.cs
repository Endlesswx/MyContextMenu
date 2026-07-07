using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ContextMenuManager
{
    public partial class AddItemWindow : Window
    {
        /// <summary>新增项所属的分类（供主窗口刷新定位）</summary>
        public CategoryId CreatedCategory { get; private set; }
        public string CreatedName { get; private set; }
        public string CreatedExtension { get; private set; }

        public AddItemWindow(CategoryId currentCategory, string currentExt)
        {
            InitializeComponent();

            // 按当前分类预选模式
            switch (currentCategory)
            {
                case CategoryId.NewMenu:
                    ModeNew.IsChecked = true;
                    break;
                case CategoryId.SendTo:
                    ModeSendTo.IsChecked = true;
                    break;
                case CategoryId.DesktopBackground:
                    TargetCombo.SelectedIndex = 0;
                    break;
                case CategoryId.Folders:
                    TargetCombo.SelectedIndex = 2;
                    break;
                case CategoryId.Drives:
                    TargetCombo.SelectedIndex = 3;
                    break;
                case CategoryId.ByExtension:
                    TargetCombo.SelectedIndex = 4;
                    if (!string.IsNullOrEmpty(currentExt)) VerbExtBox.Text = currentExt;
                    break;
                default:
                    TargetCombo.SelectedIndex = 1;
                    break;
            }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelVerb == null || PanelNew == null || PanelSendTo == null) return;
            PanelVerb.Visibility = ModeVerb.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelNew.Visibility = ModeNew.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelSendTo.Visibility = ModeSendTo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VerbExtBox == null) return;
            VerbExtBox.Visibility = TargetCombo.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            RebuildCommandPreview();
        }

        private void VerbInput_Changed(object sender, TextChangedEventArgs e)
        {
            if (ReferenceEquals(sender, VerbExeBox)) RebuildCommandPreview();
        }

        private void RebuildCommandPreview()
        {
            if (VerbCmdBox == null || VerbExeBox == null) return;
            if (VerbCmdBox.IsFocused) return;
            string exe = (VerbExeBox.Text ?? "").Trim().Trim('"');
            if (exe.Length == 0) { return; }
            string param = TargetCombo.SelectedIndex == 0 ? "%V" : "%1";
            VerbCmdBox.Text = "\"" + exe + "\" \"" + param + "\"";
        }

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "程序 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|所有文件 (*.*)|*.*",
                Title = "选择要执行的程序"
            };
            if (dlg.ShowDialog(this) == true)
            {
                VerbExeBox.Text = dlg.FileName;
                if (string.IsNullOrEmpty(VerbIconBox.Text)) VerbIconBox.Text = dlg.FileName;
                RebuildCommandPreview();
            }
        }

        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图标来源 (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|所有文件 (*.*)|*.*",
                Title = "选择图标文件"
            };
            if (dlg.ShowDialog(this) == true) VerbIconBox.Text = dlg.FileName;
        }

        private void BrowseSendToFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择发送目标（程序或文件）"
            };
            if (dlg.ShowDialog(this) == true)
            {
                SendToTargetBox.Text = dlg.FileName;
                if (string.IsNullOrEmpty(SendToNameBox.Text))
                    SendToNameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        private void BrowseSendToFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择发送目标文件夹（发送 = 复制到该文件夹）"
            })
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SendToTargetBox.Text = dlg.SelectedPath;
                    if (string.IsNullOrEmpty(SendToNameBox.Text))
                        SendToNameBox.Text = Path.GetFileName(dlg.SelectedPath.TrimEnd('\\'));
                }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ModeVerb.IsChecked == true) AddVerb();
                else if (ModeNew.IsChecked == true) AddShellNew();
                else AddSendTo();
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this,
                    "权限不足：写入系统注册表（HKLM）需要管理员权限。\n请以管理员身份重新运行本程序后重试。",
                    "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Security.SecurityException)
            {
                MessageBox.Show(this,
                    "权限不足：写入系统注册表（HKLM）需要管理员权限。\n请以管理员身份重新运行本程序后重试。",
                    "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "添加失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddVerb()
        {
            string name = (VerbNameBox.Text ?? "").Trim();
            string cmd = (VerbCmdBox.Text ?? "").Trim();
            string exe = (VerbExeBox.Text ?? "").Trim();
            if (name.Length == 0) { Warn("请输入菜单名称。", VerbNameBox); return; }
            if (cmd.Length == 0 && exe.Length == 0) { Warn("请选择程序或填写完整命令。", VerbExeBox); return; }
            if (cmd.Length == 0) cmd = "\"" + exe.Trim('"') + "\" \"" + (TargetCombo.SelectedIndex == 0 ? "%V" : "%1") + "\"";

            string root;
            CategoryId cat;
            switch (TargetCombo.SelectedIndex)
            {
                case 0: root = @"Directory\Background"; cat = CategoryId.DesktopBackground; break;
                case 2: root = "Directory"; cat = CategoryId.Folders; break;
                case 3: root = "Drive"; cat = CategoryId.Drives; break;
                case 4:
                    {
                        string ext = (VerbExtBox.Text ?? "").Trim().ToLowerInvariant();
                        if (ext.Length > 0 && !ext.StartsWith(".")) ext = "." + ext;
                        if (ext.Length < 2 || ext.IndexOf('\\') >= 0 || ext.IndexOf(' ') >= 0)
                        { Warn("请输入正确的后缀名，例如 .txt", VerbExtBox); return; }
                        root = @"SystemFileAssociations\" + ext;
                        cat = CategoryId.ByExtension;
                        CreatedExtension = ext;
                        break;
                    }
                default: root = "*"; cat = CategoryId.AllFiles; break;
            }

            MenuService.AddVerb(root, name, cmd,
                (VerbIconBox.Text ?? "").Trim(),
                VerbExtended.IsChecked == true,
                VerbTop.IsChecked == true);

            CreatedCategory = cat;
            CreatedName = name;
            DialogResult = true;
        }

        private void AddShellNew()
        {
            string ext = (NewExtBox.Text ?? "").Trim();
            if (ext.Length == 0) { Warn("请输入后缀名，例如 .md", NewExtBox); return; }
            MenuService.AddShellNew(ext, (NewTypeBox.Text ?? "").Trim());
            CreatedCategory = CategoryId.NewMenu;
            CreatedName = "「新建」" + ext;
            DialogResult = true;
        }

        private void AddSendTo()
        {
            string name = (SendToNameBox.Text ?? "").Trim();
            string target = (SendToTargetBox.Text ?? "").Trim().Trim('"');
            if (name.Length == 0) { Warn("请输入显示名称。", SendToNameBox); return; }
            if (target.Length == 0) { Warn("请选择目标程序或文件夹。", SendToTargetBox); return; }
            MenuService.AddSendTo(name, target);
            CreatedCategory = CategoryId.SendTo;
            CreatedName = name;
            DialogResult = true;
        }

        private void Warn(string message, Control focus)
        {
            MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            if (focus != null) focus.Focus();
        }
    }
}
