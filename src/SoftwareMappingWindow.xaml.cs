using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace ContextMenuManager
{
    public partial class SoftwareMappingWindow : Window
    {
        private List<KeyValuePair<string, string>> _mappings = new List<KeyValuePair<string, string>>();
        private bool _hasChanges = false;

        public SoftwareMappingWindow()
        {
            InitializeComponent();
            LoadMappings();
        }

        private void LoadMappings()
        {
            _mappings = SoftwareMappingService.GetAllMappings()
                .OrderBy(x => x.Key)
                .ToList();
            RefreshList();
            _hasChanges = false;
        }

        private void RefreshList()
        {
            MappingList.ItemsSource = null;
            MappingList.ItemsSource = _mappings;
            EmptyHint.Visibility = _mappings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SoftwareMappingEditDialog(null, null) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                // 检查关键字是否已存在
                if (_mappings.Any(m => string.Equals(m.Key, dlg.Keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, 
                        "关键字「" + dlg.Keyword + "」已存在，请使用编辑功能修改。", 
                        "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _mappings.Add(new KeyValuePair<string, string>(dlg.Keyword, dlg.SoftwareName));
                _mappings = _mappings.OrderBy(x => x.Key).ToList();
                RefreshList();
                _hasChanges = true;
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedItem();
        }

        private void EditSelectedItem()
        {
            var selected = MappingList.SelectedItem as KeyValuePair<string, string>?;
            if (selected == null)
            {
                MessageBox.Show(this, "请先选择要编辑的映射规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SoftwareMappingEditDialog(selected.Value.Key, selected.Value.Value) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                // 检查新关键字是否与其他项冲突
                if (!string.Equals(selected.Value.Key, dlg.Keyword, StringComparison.OrdinalIgnoreCase))
                {
                    if (_mappings.Any(m => string.Equals(m.Key, dlg.Keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show(this,
                            "关键字「" + dlg.Keyword + "」已存在。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                // 删除旧项，添加新项
                _mappings.RemoveAll(m => string.Equals(m.Key, selected.Value.Key, StringComparison.OrdinalIgnoreCase));
                _mappings.Add(new KeyValuePair<string, string>(dlg.Keyword, dlg.SoftwareName));
                _mappings = _mappings.OrderBy(x => x.Key).ToList();
                RefreshList();
                _hasChanges = true;
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = MappingList.SelectedItem as KeyValuePair<string, string>?;
            if (selected == null)
            {
                MessageBox.Show(this, "请先选择要删除的映射规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(this,
                "确定要删除映射规则吗？\n\n关键字：" + selected.Value.Key + "\n软件名称：" + selected.Value.Value,
                "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _mappings.RemoveAll(m => string.Equals(m.Key, selected.Value.Key, StringComparison.OrdinalIgnoreCase));
                RefreshList();
                _hasChanges = true;
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (_hasChanges)
            {
                var result = MessageBox.Show(this,
                    "重新加载将丢弃所有未保存的更改，确定要继续吗？",
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            try
            {
                SoftwareMappingService.LoadMappings();
                LoadMappings();
                MessageBox.Show(this, "已从配置文件重新加载映射规则。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "重新加载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 清空现有映射
                var allKeys = SoftwareMappingService.GetAllMappings().Keys.ToList();
                foreach (var key in allKeys)
                    SoftwareMappingService.RemoveMapping(key);

                // 添加新映射
                foreach (var kv in _mappings)
                    SoftwareMappingService.SetMapping(kv.Key, kv.Value);

                // 保存到文件
                SoftwareMappingService.SaveMappings();

                MessageBox.Show(this, "映射规则已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                _hasChanges = false;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_hasChanges)
            {
                var result = MessageBox.Show(this,
                    "有未保存的更改，确定要关闭吗？",
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            DialogResult = false;
        }

        private void MappingList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MappingList.SelectedItem != null)
                EditSelectedItem();
        }
    }
}
