using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ContextMenuManager
{
    public partial class MainWindow : Window
    {
        private readonly List<CategoryInfo> _categories = new List<CategoryInfo>
        {
            new CategoryInfo { Id = CategoryId.DesktopBackground, Title = "桌面右键菜单",
                Description = "管理在桌面空白处（以及文件夹窗口空白处）单击右键时显示的菜单项。" },
            new CategoryInfo { Id = CategoryId.NewMenu, Title = "「新建」菜单",
                Description = "管理右键「新建」子菜单中的项目。屏蔽仅将其从「新建」菜单移除（随时可恢复），不影响该类型文件的打开方式。" },
            new CategoryInfo { Id = CategoryId.AllFiles, Title = "文件右键菜单",
                Description = "管理右键任意文件时显示的菜单项（对所有文件类型生效）。特定类型文件独有的菜单请使用「按后缀名查询」。" },
            new CategoryInfo { Id = CategoryId.Folders, Title = "文件夹右键菜单",
                Description = "管理右键文件夹时显示的菜单项。" },
            new CategoryInfo { Id = CategoryId.Drives, Title = "驱动器右键菜单",
                Description = "管理右键磁盘驱动器（如 C:、D:）时显示的菜单项。" },
            new CategoryInfo { Id = CategoryId.SendTo, Title = "「发送到」菜单",
                Description = "管理右键「发送到」子菜单中的项目。屏蔽 = 设为隐藏（可随时恢复）；新增 = 在发送到文件夹中创建快捷方式。" },
            new CategoryInfo { Id = CategoryId.ByExtension, Title = "按后缀名查询",
                Description = "输入文件后缀名（如 .txt、.jpg），查询并管理该类型文件特有的右键菜单项（含其关联程序与「新建」模板）。" },
        };

        private List<MenuEntry> _entries = new List<MenuEntry>();
        private string _currentExt = "";
        private bool _suppressLoad;

        public MainWindow()
        {
            InitializeComponent();

            bool admin = MenuService.IsAdministrator();
            if (admin)
            {
                AdminBadge.Text = "✓ 管理员模式";
                AdminBadge.Foreground = System.Windows.Media.Brushes.Green;
                AdminBadge.Cursor = Cursors.Arrow;
                AdminBadge.ToolTip = "已具备修改系统级菜单所需的权限";
            }
            else
            {
                AdminBadge.Text = "⚠ 未以管理员运行（点此提权）";
                AdminBadge.Foreground = System.Windows.Media.Brushes.DarkOrange;
                AdminBadge.ToolTip = "点击以管理员身份重新启动；否则系统级（HKLM）菜单项无法修改";
            }

            if (MenuService.IsWindows11)
            {
                BtnWin11.Visibility = Visibility.Visible;
                Win11Tip.Visibility = Visibility.Collapsed; // 在 LoadCategory 中按需显示
                UpdateWin11ButtonText();
            }

            CategoryList.ItemsSource = _categories;
            _suppressLoad = true;
            CategoryList.SelectedIndex = 0;
            _suppressLoad = false;

            Loaded += delegate
            {
                RefreshAllCounts();
                LoadCategory();
            };
        }

        private CategoryInfo CurrentCategory
        {
            get { return CategoryList.SelectedItem as CategoryInfo; }
        }

        #region 加载与刷新

        private void WithWaitCursor(Action action)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try { action(); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void RefreshAllCounts()
        {
            WithWaitCursor(delegate
            {
                foreach (var c in _categories)
                {
                    if (c.Id == CategoryId.ByExtension) continue;
                    try { c.Count = MenuService.ScanCategory(c.Id).Count; }
                    catch { c.Count = null; }
                }
                CategoryList.Items.Refresh();
            });
        }

        private void LoadCategory()
        {
            var cat = CurrentCategory;
            if (cat == null) return;

            CatTitle.Text = cat.Title;
            CatDesc.Text = cat.Description;
            ExtPanel.Visibility = cat.Id == CategoryId.ByExtension ? Visibility.Visible : Visibility.Collapsed;
            Win11Tip.Visibility = MenuService.IsWindows11 && cat.Id != CategoryId.SendTo && cat.Id != CategoryId.NewMenu
                ? Visibility.Visible : Visibility.Collapsed;

            WithWaitCursor(delegate
            {
                try
                {
                    if (cat.Id == CategoryId.ByExtension)
                    {
                        _entries = string.IsNullOrEmpty(_currentExt)
                            ? new List<MenuEntry>()
                            : MenuService.ScanCategory(CategoryId.ByExtension, _currentExt);
                    }
                    else
                    {
                        _entries = MenuService.ScanCategory(cat.Id);
                        cat.Count = _entries.Count;
                        CategoryList.Items.Refresh();
                    }
                }
                catch (Exception ex)
                {
                    _entries = new List<MenuEntry>();
                    SetStatus("扫描失败：" + ex.Message);
                }

                ItemList.ItemsSource = _entries;
                ResetSort();
                ApplyFilter();
            });
        }

        #region 排序（点击列标题）

        private string _sortProperty;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;

        /// <summary>列标题 → 排序所用属性名</summary>
        private static string SortKeyFor(string header)
        {
            switch (header)
            {
                case "状态": return "StatusText";
                case "名称": return "DisplayName";
                case "类型": return "TypeLabel";
                case "位置": return "SourceLabel";
                case "命令 / 详情": return "Details";
                default: return null;
            }
        }

        private void ResetSort()
        {
            _sortProperty = null;
            _sortDirection = ListSortDirection.Ascending;
            var view = CollectionViewSource.GetDefaultView(ItemList.ItemsSource);
            if (view != null) view.SortDescriptions.Clear();
            ClearHeaderArrows();
        }

        private void GridHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as System.Windows.Controls.GridViewColumnHeader;
            if (header == null || header.Column == null) return;
            // 用原始标题文本（去掉可能已附加的箭头）确定排序字段
            string baseHeader = (header.Column.Header as string) ?? "";
            baseHeader = baseHeader.Replace(" ▲", "").Replace(" ▼", "");
            string prop = SortKeyFor(baseHeader);
            if (prop == null) return; // 「操作」列不排序

            if (_sortProperty == prop)
                _sortDirection = _sortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending : ListSortDirection.Ascending;
            else
            {
                _sortProperty = prop;
                _sortDirection = ListSortDirection.Ascending;
            }

            var view = CollectionViewSource.GetDefaultView(ItemList.ItemsSource);
            if (view == null) return;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
            view.Refresh();

            UpdateHeaderArrows(baseHeader);
        }

        private void ClearHeaderArrows()
        {
            var gv = ItemList.View as GridView;
            if (gv == null) return;
            foreach (var col in gv.Columns)
            {
                var h = col.Header as string;
                if (h != null) col.Header = h.Replace(" ▲", "").Replace(" ▼", "");
            }
        }

        private void UpdateHeaderArrows(string activeBaseHeader)
        {
            var gv = ItemList.View as GridView;
            if (gv == null) return;
            string arrow = _sortDirection == ListSortDirection.Ascending ? " ▲" : " ▼";
            foreach (var col in gv.Columns)
            {
                var h = col.Header as string;
                if (h == null) continue;
                string bare = h.Replace(" ▲", "").Replace(" ▼", "");
                col.Header = bare == activeBaseHeader ? bare + arrow : bare;
            }
        }

        #endregion

        private void ApplyFilter()
        {
            string q = (SearchBox.Text ?? "").Trim();
            var view = CollectionViewSource.GetDefaultView(ItemList.ItemsSource);
            if (view == null) return;
            if (q.Length == 0)
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = delegate (object o)
                {
                    var e = o as MenuEntry;
                    if (e == null) return false;
                    return ContainsIgnoreCase(e.DisplayName, q)
                        || ContainsIgnoreCase(e.RawName, q)
                        || ContainsIgnoreCase(e.Details, q)
                        || ContainsIgnoreCase(e.KeyPath, q)
                        || ContainsIgnoreCase(e.SourceLabel, q);
                };
            }
            UpdateCountAndEmptyHint();
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            return haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateCountAndEmptyHint()
        {
            var view = CollectionViewSource.GetDefaultView(ItemList.ItemsSource);
            int shown = view == null ? 0 : view.Cast<object>().Count();
            int total = _entries.Count;
            int disabled = _entries.Count(x => !x.Enabled);
            CountText.Text = string.Format("显示 {0} / 共 {1} 项，已屏蔽 {2} 项", shown, total, disabled);

            bool empty = shown == 0;
            EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (empty)
            {
                var cat = CurrentCategory;
                if (cat != null && cat.Id == CategoryId.ByExtension && string.IsNullOrEmpty(_currentExt))
                    EmptyHint.Text = "输入后缀名（如 .txt）后点击「查询」";
                else if ((SearchBox.Text ?? "").Trim().Length > 0)
                    EmptyHint.Text = "没有匹配搜索条件的项目";
                else
                    EmptyHint.Text = "此分类下暂无项目";
            }
        }

        private void SetStatus(string msg)
        {
            StatusText.Text = msg;
        }

        #endregion

        #region 事件：分类 / 搜索 / 查询

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLoad) return;
            SearchBox.Text = "";
            LoadCategory();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void BtnExtQuery_Click(object sender, RoutedEventArgs e)
        {
            string ext = (ExtBox.Text ?? "").Trim();
            if (ext.Length == 0)
            {
                MessageBox.Show(this, "请输入文件后缀名，例如 .txt", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ext.StartsWith(".")) ext = "." + ext;
            if (ext.IndexOf('\\') >= 0 || ext.IndexOf('/') >= 0 || ext.IndexOf(' ') >= 0)
            {
                MessageBox.Show(this, "后缀名格式不正确。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _currentExt = ext;
            ExtBox.Text = ext;
            LoadCategory();
            SetStatus("已查询后缀名 " + ext + " 的右键菜单");
        }

        private void ExtBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnExtQuery_Click(sender, null);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshAllCounts();
            LoadCategory();
            SetStatus("已刷新");
        }

        #endregion

        #region 事件：行操作

        private MenuEntry EntryFromSender(object sender)
        {
            var fe = sender as FrameworkElement;
            return fe == null ? null : fe.DataContext as MenuEntry;
        }

        private void RowToggle_Click(object sender, RoutedEventArgs e) { ToggleEntry(EntryFromSender(sender)); }
        private void RowDelete_Click(object sender, RoutedEventArgs e) { DeleteEntry(EntryFromSender(sender)); }
        private void RowLocate_Click(object sender, RoutedEventArgs e) { LocateEntry(EntryFromSender(sender)); }

        private void CtxToggle_Click(object sender, RoutedEventArgs e) { ToggleEntry(ItemList.SelectedItem as MenuEntry); }
        private void CtxDelete_Click(object sender, RoutedEventArgs e) { DeleteEntry(ItemList.SelectedItem as MenuEntry); }
        private void CtxLocate_Click(object sender, RoutedEventArgs e) { LocateEntry(ItemList.SelectedItem as MenuEntry); }

        private void CtxCopy_Click(object sender, RoutedEventArgs e)
        {
            var entry = ItemList.SelectedItem as MenuEntry;
            if (entry == null) return;
            try
            {
                Clipboard.SetText(entry.FullLocation);
                SetStatus("已复制：" + entry.FullLocation);
            }
            catch { }
        }

        private void ToggleEntry(MenuEntry entry)
        {
            if (entry == null) return;
            bool target = !entry.Enabled;
            try
            {
                MenuService.SetEnabled(entry, target);
                ItemList.Items.Refresh();
                UpdateCountAndEmptyHint();
                if (entry.Kind == EntryKind.PackagedShellNew)
                {
                    SetStatus((target ? "已恢复：" : "已屏蔽：") + entry.DisplayName +
                        "（商店应用内置项，需重启资源管理器后生效）");
                    var rr = MessageBox.Show(this,
                        (target ? "已恢复「" : "已屏蔽「") + entry.DisplayName + "」。\n\n" +
                        "商店应用内置的「新建」项需要重启资源管理器后才会在右键菜单中生效。\n\n是否立即重启资源管理器？",
                        "需要重启资源管理器", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (rr == MessageBoxResult.Yes)
                    {
                        WithWaitCursor(delegate { MenuService.RestartExplorer(); });
                        SetStatus("资源管理器已重启，" + (target ? "已恢复：" : "已屏蔽：") + entry.DisplayName);
                    }
                }
                else
                {
                    SetStatus((target ? "已恢复：" : "已屏蔽：") + entry.DisplayName +
                        "（修改立即写入注册表，若菜单未变化可重启资源管理器）");
                }
            }
            catch (Exception ex)
            {
                ShowOperationError(ex, entry);
            }
        }

        private void DeleteEntry(MenuEntry entry)
        {
            if (entry == null) return;
            if (entry.Kind == EntryKind.PackagedShellNew)
            {
                MessageBox.Show(this,
                    "「" + entry.DisplayName + "」是商店应用（内置）注册的「新建」项，无法删除。\n\n" +
                    "请改用「屏蔽」按钮将其从「新建」菜单中隐藏——该操作可随时“恢复”。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string what = entry.Kind == EntryKind.SendToItem
                ? "该「发送到」项将被移动到备份文件夹。"
                : "该注册表项将先导出为 .reg 备份文件，然后从注册表中删除。\n（双击备份文件即可完整还原。）";
            var r = MessageBox.Show(this,
                "确定要删除「" + entry.DisplayName + "」吗？\n\n" + what,
                "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                string backup = MenuService.DeleteEntry(entry);
                SetStatus("已删除：" + entry.DisplayName + "，备份位于 " + backup);
                LoadCategory();
            }
            catch (Exception ex)
            {
                ShowOperationError(ex, entry);
            }
        }

        private void LocateEntry(MenuEntry entry)
        {
            if (entry == null) return;
            try
            {
                if (entry.Kind == EntryKind.SendToItem)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + entry.KeyPath + "\"")
                    { UseShellExecute = true });
                }
                else if (entry.Kind == EntryKind.PackagedShellNew)
                {
                    // 打开商店应用的安装目录
                    if (Directory.Exists(entry.KeyPath))
                        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + entry.KeyPath + "\"")
                        { UseShellExecute = true });
                    else
                        SetStatus("包目录不存在：" + entry.KeyPath);
                }
                else
                {
                    RegistryHelper.JumpToRegedit(entry.FullLocation);
                }
            }
            catch (Exception ex)
            {
                SetStatus("定位失败：" + ex.Message);
            }
        }

        private void ShowOperationError(Exception ex, MenuEntry entry)
        {
            if (ex is UnauthorizedAccessException || ex is SecurityException)
            {
                MessageBox.Show(this,
                    "权限不足，无法修改此项。\n\n「" + entry.DisplayName + "」位于系统注册表（HKLM）中，" +
                    "请以管理员身份重新运行本程序后重试。\n\n（点击窗口左上角「未以管理员运行」可快速提权。）",
                    "权限不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(this, "操作失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            LoadCategory();
        }

        #endregion

        #region 事件：顶栏

        private void AdminBadge_Click(object sender, MouseButtonEventArgs e)
        {
            if (MenuService.IsAdministrator()) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Application.Current.Shutdown();
            }
            catch
            {
                // 用户取消 UAC
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var cat = CurrentCategory;
            var dlg = new AddItemWindow(cat == null ? CategoryId.AllFiles : cat.Id, _currentExt) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                // 切换到新增项所属分类并刷新
                foreach (var c in _categories)
                {
                    if (c.Id == dlg.CreatedCategory)
                    {
                        if (!ReferenceEquals(CategoryList.SelectedItem, c))
                        {
                            _suppressLoad = true;
                            CategoryList.SelectedItem = c;
                            _suppressLoad = false;
                        }
                        break;
                    }
                }
                if (dlg.CreatedCategory == CategoryId.ByExtension && !string.IsNullOrEmpty(dlg.CreatedExtension))
                {
                    _currentExt = dlg.CreatedExtension;
                    ExtBox.Text = dlg.CreatedExtension;
                }
                RefreshAllCounts();
                LoadCategory();
                SetStatus("已新增：" + dlg.CreatedName);
            }
        }

        private void BtnRestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this,
                "将结束并重新启动资源管理器（explorer.exe），任务栏和桌面会短暂消失，打开的文件夹窗口将被关闭。\n\n是否继续？",
                "重启资源管理器", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            WithWaitCursor(delegate { MenuService.RestartExplorer(); });
            SetStatus("资源管理器已重启");
        }

        private void UpdateWin11ButtonText()
        {
            BtnWin11.Content = MenuService.IsClassicMenuEnabled()
                ? "恢复 Win11 默认菜单"
                : "切换 Win11 经典菜单";
        }

        private void BtnWin11_Click(object sender, RoutedEventArgs e)
        {
            bool classic = MenuService.IsClassicMenuEnabled();
            try
            {
                MenuService.SetClassicMenu(!classic);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "切换失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            UpdateWin11ButtonText();
            var r = MessageBox.Show(this,
                (!classic
                    ? "已切换为经典完整右键菜单（与 Windows 10 相同）。"
                    : "已恢复 Windows 11 默认精简菜单。") +
                "\n\n需要重启资源管理器后生效，是否立即重启？",
                "右键菜单管家", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                WithWaitCursor(delegate { MenuService.RestartExplorer(); });
                SetStatus("资源管理器已重启，右键菜单风格已切换");
            }
            else
            {
                SetStatus("右键菜单风格已修改，重启资源管理器后生效");
            }
        }

        private void OpenBackupDir_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + MenuService.BackupDir + "\"")
                { UseShellExecute = true });
            }
            catch { }
        }

        #endregion
    }
}
