using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ContextMenuManager
{
    /// <summary>bool → Visibility 转换器（可配置 True/False 对应的可见性）</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public Visibility TrueValue { get; set; }
        public Visibility FalseValue { get; set; }

        public BoolToVisibilityConverter()
        {
            TrueValue = Visibility.Visible;
            FalseValue = Visibility.Collapsed;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool && (bool)value;
            return b ? TrueValue : FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>菜单项的技术类型</summary>
    public enum EntryKind
    {
        /// <summary>注册表 shell 下的静态菜单项（verb）</summary>
        ShellVerb,
        /// <summary>shellex\ContextMenuHandlers 下的 COM 扩展处理程序</summary>
        ShellExHandler,
        /// <summary>「新建」菜单模板（ShellNew）</summary>
        ShellNew,
        /// <summary>「发送到」文件夹中的项目</summary>
        SendToItem,
        /// <summary>由商店应用（MSIX 包清单）注册的「新建」项，只读</summary>
        PackagedShellNew
    }

    /// <summary>管理分类</summary>
    public enum CategoryId
    {
        DesktopBackground,
        NewMenu,
        AllFiles,
        Folders,
        Drives,
        SendTo,
        ByExtension,
        GlobalSearch // 全局搜索（跨分类）
    }

    /// <summary>一条右键菜单项</summary>
    public class MenuEntry
    {
        public EntryKind Kind { get; set; }

        /// <summary>解析后的友好显示名</summary>
        public string DisplayName { get; set; }

        /// <summary>注册表键名 / 文件名</summary>
        public string RawName { get; set; }

        /// <summary>HKCR 下的相对键路径；SendTo 项为文件完整路径</summary>
        public string KeyPath { get; set; }

        /// <summary>来源说明（如 "文件夹（Directory）"）</summary>
        public string SourceLabel { get; set; }

        /// <summary>命令行 / DLL 路径 / 模板方式 / 快捷方式目标</summary>
        public string Details { get; set; }

        /// <summary>关联软件名称</summary>
        public string AssociatedSoftware { get; set; }

        /// <summary>所属分类（用于全局搜索时显示来源）</summary>
        public CategoryId Category { get; set; }

        /// <summary>分类友好名称（用于全局搜索显示）</summary>
        public string CategoryName { get; set; }

        public bool Enabled { get; set; }

        public string TypeLabel
        {
            get
            {
                switch (Kind)
                {
                    case EntryKind.ShellVerb: return "菜单项";
                    case EntryKind.ShellExHandler: return "Shell 扩展";
                    case EntryKind.ShellNew: return "新建模板";
                    case EntryKind.PackagedShellNew: return "新建模板";
                    case EntryKind.SendToItem: return "发送到";
                    default: return "";
                }
            }
        }

        public string StatusText
        {
            get { return Enabled ? "已启用" : "已屏蔽"; }
        }

        public string ToggleText { get { return Enabled ? "屏蔽" : "恢复"; } }

        /// <summary>是否允许删除（商店应用内置「新建」项不支持删除，只能屏蔽）</summary>
        public bool CanDelete { get { return Kind != EntryKind.PackagedShellNew; } }

        /// <summary>用于展示/复制的完整位置</summary>
        public string FullLocation
        {
            get
            {
                if (Kind == EntryKind.SendToItem) return KeyPath;
                if (Kind == EntryKind.PackagedShellNew) return KeyPath; // 包目录
                return @"HKEY_CLASSES_ROOT\" + KeyPath;
            }
        }
    }

    /// <summary>侧栏分类信息</summary>
    public class CategoryInfo
    {
        public CategoryId Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? Count { get; set; }
        public string CountText { get { return Count.HasValue ? Count.Value.ToString() : ""; } }
    }
}
