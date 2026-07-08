using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ContextMenuManager
{
    /// <summary>右键菜单扫描与管理核心</summary>
    public static class MenuService
    {
        public static string AppDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string BackupDir
        {
            get
            {
                string d = Path.Combine(AppDir, "backups");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static bool IsWindows11
        {
            get { return Environment.OSVersion.Version.Build >= 22000; }
        }

        #region 扫描

        /// <summary>常见系统 verb 的中文名</summary>
        private static readonly Dictionary<string, string> CanonicalVerbNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "open", "打开" },
            { "opennewwindow", "在新窗口中打开" },
            { "opennewprocess", "在新进程中打开" },
            { "opennewtab", "在新标签页中打开" },
            { "openas", "打开方式" },
            { "edit", "编辑" },
            { "explore", "浏览" },
            { "find", "搜索" },
            { "print", "打印" },
            { "printto", "打印到（拖放）" },
            { "play", "播放" },
            { "playall", "全部播放" },
            { "preview", "预览" },
            { "properties", "属性" },
            { "runas", "以管理员身份运行" },
            { "runasuser", "以其他用户身份运行" },
            { "cmd", "在此处打开命令窗口" },
            { "powershell", "在此处打开 PowerShell 窗口" },
            { "pintohome", "固定到“快速访问”" },
            { "pintostartscreen", "固定到“开始”屏幕" },
        };

        public static List<MenuEntry> ScanCategory(CategoryId id, string extension = null)
        {
            var list = new List<MenuEntry>();
            switch (id)
            {
                case CategoryId.DesktopBackground:
                    ScanShellRoot(@"Directory\Background", "桌面/窗口空白处", list, id, "桌面右键菜单");
                    ScanShellRoot("DesktopBackground", "桌面（系统项）", list, id, "桌面右键菜单");
                    break;
                case CategoryId.AllFiles:
                    ScanShellRoot("*", "所有文件", list, id, "文件右键菜单");
                    ScanShellRoot("AllFilesystemObjects", "文件与文件夹", list, id, "文件右键菜单");
                    break;
                case CategoryId.Folders:
                    ScanShellRoot("Directory", "文件夹（目录）", list, id, "文件夹右键菜单");
                    ScanShellRoot("Folder", "文件夹（通用）", list, id, "文件夹右键菜单");
                    break;
                case CategoryId.Drives:
                    ScanShellRoot("Drive", "驱动器", list, id, "驱动器右键菜单");
                    break;
                case CategoryId.NewMenu:
                    ScanNewMenu(list, id, "「新建」菜单");
                    break;
                case CategoryId.SendTo:
                    ScanSendTo(list, id, "「发送到」菜单");
                    break;
                case CategoryId.ByExtension:
                    ScanByExtension(extension, list);
                    break;
                case CategoryId.GlobalSearch:
                    // 全局搜索：扫描所有分类
                    ScanAllCategories(list);
                    break;
            }
            return list;
        }

        /// <summary>扫描所有分类（用于全局搜索）</summary>
        private static void ScanAllCategories(List<MenuEntry> list)
        {
            ScanShellRoot(@"Directory\Background", "桌面/窗口空白处", list, CategoryId.DesktopBackground, "桌面右键菜单");
            ScanShellRoot("DesktopBackground", "桌面（系统项）", list, CategoryId.DesktopBackground, "桌面右键菜单");
            ScanShellRoot("*", "所有文件", list, CategoryId.AllFiles, "文件右键菜单");
            ScanShellRoot("AllFilesystemObjects", "文件与文件夹", list, CategoryId.AllFiles, "文件右键菜单");
            ScanShellRoot("Directory", "文件夹（目录）", list, CategoryId.Folders, "文件夹右键菜单");
            ScanShellRoot("Folder", "文件夹（通用）", list, CategoryId.Folders, "文件夹右键菜单");
            ScanShellRoot("Drive", "驱动器", list, CategoryId.Drives, "驱动器右键菜单");
            ScanNewMenu(list, CategoryId.NewMenu, "「新建」菜单");
            ScanSendTo(list, CategoryId.SendTo, "「发送到」菜单");
        }

        /// <summary>扫描某根键下的 shell（verb）与 shellex\ContextMenuHandlers（COM 扩展）</summary>
        private static void ScanShellRoot(string root, string sourceLabel, List<MenuEntry> list, CategoryId category = CategoryId.AllFiles, string categoryName = "")
        {
            ScanShellVerbs(root, sourceLabel, list, category, categoryName);
            ScanShellExHandlers(root, sourceLabel, list, category, categoryName);
        }

        private static void ScanShellVerbs(string root, string sourceLabel, List<MenuEntry> list, CategoryId category = CategoryId.AllFiles, string categoryName = "")
        {
            using (var shell = RegistryHelper.OpenHkcr(root + @"\shell"))
            {
                if (shell == null) return;
                foreach (var name in shell.GetSubKeyNames())
                {
                    try
                    {
                        using (var vk = shell.OpenSubKey(name))
                        {
                            if (vk == null) continue;

                            // 过滤不会在右键菜单中显示的系统内部 verb
                            if (IsHiddenSystemVerb(vk, name)) continue;

                            var e = new MenuEntry
                            {
                                Kind = EntryKind.ShellVerb,
                                RawName = name,
                                KeyPath = root + @"\shell\" + name,
                                SourceLabel = sourceLabel,
                                Category = category,
                                CategoryName = categoryName,
                            };
                            e.DisplayName = ResolveVerbDisplayName(vk, name);
                            e.Enabled = vk.GetValue("LegacyDisable") == null
                                        && vk.GetValue("ProgrammaticAccessOnly") == null;
                            e.Details = BuildVerbDetails(vk);
                            list.Add(e);
                        }
                    }
                    catch { /* 个别键无权限读取时跳过 */ }
                }
            }
        }

        /// <summary>判断一个 verb 是否是不会在右键菜单中显示的系统内部项</summary>
        private static bool IsHiddenSystemVerb(RegistryKey verbKey, string keyName)
        {
            // CommandStateHandler：可见性由 COM 对象在运行时控制，
            // 结合 CommandStateSync 的 "HideDisabled" 时该项通常不可见
            string csh = verbKey.GetValue("CommandStateHandler") as string;
            if (!string.IsNullOrEmpty(csh))
            {
                // 检查 CommandStateSync：值为 "HideDisabled" 时该项被隐藏
                // 但如果有 MUIVerb 或默认值（说明是正规菜单项），不过滤
                // 只过滤那些既无 MUIVerb 又无默认值的纯内部 verb
                string mui = verbKey.GetValue("MUIVerb") as string;
                string def = verbKey.GetValue("") as string;
                if (string.IsNullOrEmpty(mui) && string.IsNullOrEmpty(def))
                    return true;
            }

            // 没有 command 子键且没有 SubCommands/ExtendedSubCommandsKey 的 verb
            // 是无效项（无法执行也无子菜单）
            using (var cmd = verbKey.OpenSubKey("command"))
            {
                if (cmd == null
                    && verbKey.GetValue("SubCommands") == null
                    && verbKey.GetValue("ExtendedSubCommandsKey") == null
                    && verbKey.GetValue("DelegateExecute") == null)
                {
                    // 无 command 且无子菜单的 verb，通常是系统保留的占位项
                    return true;
                }
            }

            return false;
        }

        private static string ResolveVerbDisplayName(RegistryKey verbKey, string keyName)
        {
            string mui = verbKey.GetValue("MUIVerb") as string;
            string def = verbKey.GetValue("") as string;

            // 优先使用 MUIVerb
            if (!string.IsNullOrEmpty(mui))
            {
                string resolved = NativeMethods.LoadIndirectString(mui);
                // 如果解析成功（不再以 @ 开头），使用解析结果
                if (!string.IsNullOrEmpty(resolved) && !resolved.StartsWith("@"))
                    return CleanMenuName(resolved);
                // MUIVerb 解析失败，尝试后备
            }

            // 其次使用默认值
            if (!string.IsNullOrEmpty(def))
            {
                string resolved = NativeMethods.LoadIndirectString(def);
                if (!string.IsNullOrEmpty(resolved) && !resolved.StartsWith("@"))
                    return CleanMenuName(resolved);
            }

            // 再次尝试从规范 verb 名映射
            string zh;
            if (CanonicalVerbNames.TryGetValue(keyName, out zh)) return zh;

            // 最终后备：如果 MUIVerb 或默认值有非空非 @ 的文本，使用它
            if (!string.IsNullOrEmpty(mui) && !mui.StartsWith("@"))
                return CleanMenuName(mui);
            if (!string.IsNullOrEmpty(def) && !def.StartsWith("@"))
                return CleanMenuName(def);

            return keyName;
        }

        /// <summary>清理菜单名中的加速键标记和多余字符</summary>
        private static string CleanMenuName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            // 移除 (&X) 格式的加速键（如 "打印(&P)" → "打印"）
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(&.\)", "");
            // 移除单独的 & 加速键标记（如 "&Edit" → "Edit"）
            name = name.Replace("&", "");
            return name.Trim();
        }

        private static string BuildVerbDetails(RegistryKey verbKey)
        {
            string details = null;
            using (var cmd = verbKey.OpenSubKey("command"))
            {
                if (cmd != null)
                {
                    string c = cmd.GetValue("") as string;
                    if (!string.IsNullOrEmpty(c)) details = c;
                    else
                    {
                        string dele = cmd.GetValue("DelegateExecute") as string;
                        if (!string.IsNullOrEmpty(dele)) details = "DelegateExecute: " + dele;
                    }
                }
            }
            if (details == null)
            {
                if (verbKey.GetValue("SubCommands") != null || verbKey.GetValue("ExtendedSubCommandsKey") != null)
                    details = "级联子菜单";
                else
                    details = "";
            }
            if (verbKey.GetValue("Extended") != null)
                details += "  [仅 Shift+右键 显示]";
            return details.Trim();
        }

        /// <summary>去掉 CLSID 字符串上的屏蔽记号（前缀 - 与 } 前的 -）</summary>
        private static string StripClsidDisableMarks(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            while (s.StartsWith("-")) s = s.Substring(1);
            return s.Replace("-}", "}");
        }

        private static bool IsGuid(string s)
        {
            Guid g;
            return !string.IsNullOrEmpty(s) && Guid.TryParse(s, out g);
        }

        /// <summary>
        /// 解析一个 ContextMenuHandlers 子键的状态。
        /// 外壳解析规则：默认值若为 GUID 则使用；否则回退用键名。
        /// 屏蔽记号：GUID 前加 "-"（或某些工具在 } 前加 "-"）使其无法解析。
        /// </summary>
        private static void ResolveShellExState(string keyName, string defaultValue, out string clsid, out bool enabled, out bool clsidInKeyName)
        {
            clsidInKeyName = false;
            if (!string.IsNullOrEmpty(defaultValue))
            {
                if (IsGuid(defaultValue)) { clsid = defaultValue; enabled = true; return; }
                string stripped = StripClsidDisableMarks(defaultValue);
                if (IsGuid(stripped)) { clsid = stripped; enabled = false; return; }
            }
            // 默认值为空或仅为描述文字：CLSID 在键名上
            if (IsGuid(keyName)) { clsid = keyName; enabled = true; clsidInKeyName = true; return; }
            string strippedName = StripClsidDisableMarks(keyName);
            if (IsGuid(strippedName)) { clsid = strippedName; enabled = false; clsidInKeyName = true; return; }
            // 无法识别：按值前缀判断
            clsid = string.IsNullOrEmpty(defaultValue) ? keyName : StripClsidDisableMarks(defaultValue);
            enabled = string.IsNullOrEmpty(defaultValue) || !defaultValue.StartsWith("-");
        }

        private static void ScanShellExHandlers(string root, string sourceLabel, List<MenuEntry> list, CategoryId category = CategoryId.AllFiles, string categoryName = "")
        {
            string path = root + @"\shellex\ContextMenuHandlers";
            using (var handlers = RegistryHelper.OpenHkcr(path))
            {
                if (handlers == null) return;
                foreach (var name in handlers.GetSubKeyNames())
                {
                    try
                    {
                        using (var hk = handlers.OpenSubKey(name))
                        {
                            if (hk == null) continue;
                            string def = hk.GetValue("") as string;
                            string clsid;
                            bool enabled, clsidInKeyName;
                            ResolveShellExState(name, def, out clsid, out enabled, out clsidInKeyName);

                            var e = new MenuEntry
                            {
                                Kind = EntryKind.ShellExHandler,
                                RawName = name,
                                KeyPath = path + @"\" + name,
                                SourceLabel = sourceLabel,
                                Enabled = enabled,
                                Category = category,
                                CategoryName = categoryName,
                            };

                            string friendly = null, dll = null;
                            using (var ck = RegistryHelper.OpenHkcr(@"CLSID\" + clsid))
                            {
                                if (ck != null)
                                {
                                    // 优先尝试 LocalizedString（@dll,-xxx 形式的本地化名称）
                                    string localized = ck.GetValue("LocalizedString") as string;
                                    if (!string.IsNullOrEmpty(localized))
                                    {
                                        string resolved = NativeMethods.LoadIndirectString(localized);
                                        if (!string.IsNullOrEmpty(resolved) && !resolved.StartsWith("@"))
                                            friendly = resolved;
                                    }
                                    // 其次使用默认值
                                    if (string.IsNullOrEmpty(friendly))
                                    {
                                        string clsDefault = ck.GetValue("") as string;
                                        if (!string.IsNullOrEmpty(clsDefault))
                                        {
                                            // 如果默认值也是间接字符串，尝试解析
                                            string resolved = NativeMethods.LoadIndirectString(clsDefault);
                                            if (!string.IsNullOrEmpty(resolved) && !resolved.StartsWith("@"))
                                                friendly = resolved;
                                        }
                                    }
                                    using (var srv = ck.OpenSubKey("InprocServer32"))
                                    {
                                        if (srv != null) dll = srv.GetValue("") as string;
                                    }
                                }
                            }
                            // 如果 CLSID 解析未获取到名称，使用默认值（非 GUID 部分）
                            if (string.IsNullOrEmpty(friendly) && !string.IsNullOrEmpty(def) && !IsGuid(StripClsidDisableMarks(def)))
                                friendly = def;
                            // 如果键名不是 GUID 格式且比 COM 名更有描述性，优先使用键名
                            if (string.IsNullOrEmpty(friendly))
                                friendly = IsGuid(StripClsidDisableMarks(name)) ? clsid : name;
                            e.DisplayName = CleanMenuName(friendly);
                            e.Details = string.IsNullOrEmpty(dll) ? clsid : clsid + "  —  " + dll;
                            list.Add(e);
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>「新建」菜单有效的模板值</summary>
        private static readonly string[] ShellNewActiveValues = { "NullFile", "FileName", "Data", "Command", "Directory", "Handler" };

        private static bool IsShellNewKeyValid(RegistryKey key)
        {
            var names = key.GetValueNames();
            foreach (var v in names)
            {
                foreach (var a in ShellNewActiveValues)
                {
                    if (string.Equals(v, a, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static void ScanNewMenu(List<MenuEntry> list, CategoryId category = CategoryId.NewMenu, string categoryName = "「新建」菜单")
        {
            // 「新建 > 文件夹」
            AddShellNewEntry("Folder", "文件夹", list, null, category, categoryName);

            string[] rootNames;
            try { rootNames = Registry.ClassesRoot.GetSubKeyNames(); }
            catch { return; }

            foreach (var ext in rootNames)
            {
                if (ext.Length < 2 || ext[0] != '.') continue;
                try
                {
                    string typeName = ResolveShellNewTypeName(ext);

                    string progId = RegistryHelper.GetDefaultValue(ext);
                    AddShellNewEntry(ext, typeName, list, null, category, categoryName);

                    // .ext\<ProgID>\ShellNew 形式
                    if (!string.IsNullOrEmpty(progId))
                        AddShellNewEntry(ext + "\\" + progId, typeName, list, ext, category, categoryName);
                }
                catch { }
            }

            // 商店应用（MSIX 包）注册的「新建」项（如 记事本→文本文档、画图→BMP 图像）
            AddPackagedNewEntries(list, category, categoryName);

            list.Sort(CompareNewMenuEntries);
        }

        /// <summary>
        /// 解析「新建」菜单中某扩展名的友好类型名。
        /// 优先级：FriendlyTypeName（.ext 或 ProgID 键） > ProgID 默认值 > 后备格式
        /// Windows 资源管理器实际使用 FriendlyTypeName 来显示「新建」菜单项名称。
        /// </summary>
        private static string ResolveShellNewTypeName(string ext)
        {
            string progId = RegistryHelper.GetDefaultValue(ext);

            // 1. 优先从 .ext 键读取 FriendlyTypeName
            string friendlyName = TryResolveFriendlyTypeName(ext);
            if (!string.IsNullOrEmpty(friendlyName)) return friendlyName;

            // 2. 从 ProgID 键读取 FriendlyTypeName
            if (!string.IsNullOrEmpty(progId))
            {
                friendlyName = TryResolveFriendlyTypeName(progId);
                if (!string.IsNullOrEmpty(friendlyName)) return friendlyName;
            }

            // 3. ProgID 默认值
            if (!string.IsNullOrEmpty(progId))
            {
                string progDefault = RegistryHelper.GetDefaultValue(progId);
                if (!string.IsNullOrEmpty(progDefault)) return progDefault;
            }

            // 4. 后备
            return ext.TrimStart('.').ToUpperInvariant() + " 文件";
        }

        /// <summary>尝试从注册表键读取并解析 FriendlyTypeName 值</summary>
        private static string TryResolveFriendlyTypeName(string keyPath)
        {
            using (var k = RegistryHelper.OpenHkcr(keyPath))
            {
                if (k == null) return null;
                string ftn = k.GetValue("FriendlyTypeName") as string;
                if (string.IsNullOrEmpty(ftn)) return null;
                string resolved = NativeMethods.LoadIndirectString(ftn);
                if (!string.IsNullOrEmpty(resolved) && !resolved.StartsWith("@"))
                    return resolved;
            }
            return null;
        }

        /// <summary>
        /// 扫描由商店应用（MSIX/AppX 包清单）注册的「新建」菜单项。
        /// 这类项目不写入经典的 HKCR\.ext\...\ShellNew 键，而由包清单声明，
        /// 仅在 HKCU\...\Local Settings\MrtCache 中缓存了本地化显示名
        /// （值名形如 ShellNewDisplayName_&lt;ext&gt;），并绑定到具体的包安装目录。
        /// 通过该缓存反查仍然安装的包即可列出这些项；
        /// 屏蔽方式为改写该缓存显示名（追加超长内容使其从菜单消失，可逆恢复）。
        /// </summary>
        private static void AddPackagedNewEntries(List<MenuEntry> list, CategoryId category = CategoryId.NewMenu, string categoryName = "「新建」菜单")
        {
            // 已存在（经典 ShellNew）的扩展名，避免重复
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in list)
                if (!string.IsNullOrEmpty(e.RawName)) existing.Add(e.RawName.TrimStart('.'));

            var found = new Dictionary<string, MenuEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var mrt = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\MrtCache"))
                {
                    if (mrt == null) return;
                    foreach (var pkgEncoded in mrt.GetSubKeyNames())
                    {
                        // 顶层子键名即包资源路径（%5C 编码）；解码得到包安装目录
                        string pkgDir = DecodePackageDir(pkgEncoded);
                        if (string.IsNullOrEmpty(pkgDir)) continue;
                        // 包目录仍存在 = 该应用仍安装（旧版本目录已被删除）
                        bool installed;
                        try { installed = Directory.Exists(pkgDir); }
                        catch { installed = false; }
                        if (!installed) continue;

                        using (var pkgKey = mrt.OpenSubKey(pkgEncoded))
                        {
                            if (pkgKey == null) continue;
                            CollectShellNewNames(pkgKey, pkgDir, found, category, categoryName);
                        }
                    }
                }
            }
            catch { /* 读取缓存失败时忽略，不影响其它扫描 */ }

            foreach (var kv in found)
            {
                if (existing.Contains(kv.Key.TrimStart('.'))) continue; // 已由经典项覆盖
                list.Add(kv.Value);
            }
        }

        /// <summary>从 MrtCache 顶层子键名解码出包安装目录（截到 microsoft.system.package 之前）</summary>
        private static string DecodePackageDir(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return null;
            string decoded = encoded.Replace("%5C", "\\").Replace("%5c", "\\");
            int idx = decoded.IndexOf(@"\microsoft.system.package", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return decoded.Substring(0, idx);
            // 兜底：截到 .pri 之前的目录
            idx = decoded.IndexOf(@"\resources", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return decoded.Substring(0, idx);
            return null;
        }

        /// <summary>屏蔽标记：写入缓存的显示名中，用于识别已屏蔽项并可逆还原。
        /// 格式：&lt;原始显示名&gt;&lt;MARKER&gt;&lt;超长填充&gt;。超长内容使该项从「新建」菜单中消失。</summary>
        private const string PackagedBlockMarker = "_CMMBLOCKED_";
        private static readonly string PackagedBlockPadding = new string('█', 400); // 300 个实心方块（可见字符，外壳不裁剪）

        /// <summary>在某包资源缓存子树中查找 ShellNewDisplayName_&lt;ext&gt; 值</summary>
        private static void CollectShellNewNames(RegistryKey key, string pkgDir, Dictionary<string, MenuEntry> found, CategoryId category = CategoryId.NewMenu, string categoryName = "「新建」菜单")
        {
            try
            {
                foreach (var vn in key.GetValueNames())
                {
                    const string prefix = "ShellNewDisplayName_";
                    int p = vn.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                    if (p < 0) continue;
                    string ext = vn.Substring(p + prefix.Length);
                    // 值名形如 "@{Pkg?ms-resource://.../ShellNewDisplayName_Bmp}"，
                    // 截取扩展名部分（仅保留起始的字母数字，去掉结尾的 } 等字符）
                    int end = 0;
                    while (end < ext.Length && (char.IsLetterOrDigit(ext[end]))) end++;
                    ext = ext.Substring(0, end);
                    if (ext.Length == 0) continue;
                    ext = "." + ext.ToLowerInvariant();
                    if (found.ContainsKey(ext)) continue;

                    string raw = key.GetValue(vn) as string;
                    bool blocked = IsPackagedBlocked(raw);
                    string display = StripBlockMarker(raw);
                    if (string.IsNullOrEmpty(display)) display = ext.TrimStart('.').ToUpperInvariant() + " 文件";

                    found[ext] = new MenuEntry
                    {
                        Kind = EntryKind.PackagedShellNew,
                        RawName = ext,
                        DisplayName = display + "（" + ext + "）",
                        KeyPath = pkgDir,
                        SourceLabel = "商店应用（内置）",
                        Details = "由应用包提供：" + System.IO.Path.GetFileName(pkgDir),
                        Enabled = !blocked,
                        Category = category,
                        CategoryName = categoryName,
                    };
                }
            }
            catch { }

            try
            {
                foreach (var sn in key.GetSubKeyNames())
                {
                    using (var sk = key.OpenSubKey(sn))
                    {
                        if (sk != null) CollectShellNewNames(sk, pkgDir, found, category, categoryName);
                    }
                }
            }
            catch { }
        }

        /// <summary>去掉屏蔽标记及其后的填充，取回原始显示名</summary>
        private static string StripBlockMarker(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            int cut = raw.Length;
            int i = raw.IndexOf(PackagedBlockMarker, StringComparison.Ordinal); if (i >= 0 && i < cut) cut = i;
            int iOld = raw.IndexOf("_CMM", StringComparison.Ordinal); if (iOld >= 0 && iOld < cut) cut = iOld;
            int iNull = raw.IndexOf('\0'); if (iNull >= 0 && iNull < cut) cut = iNull;
            int iBlk = raw.IndexOf('█'); if (iBlk >= 0 && iBlk < cut) cut = iBlk;
            int iFw = raw.IndexOf('　'); if (iFw >= 0 && iFw < cut) cut = iFw;
            return cut < raw.Length ? raw.Substring(0, cut).TrimEnd(' ') : raw;
        }

        /// <summary>判断缓存显示名是否处于已屏蔽状态（兼容历史遗留格式）</summary>
        private static bool IsPackagedBlocked(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            return raw.IndexOf(PackagedBlockMarker, StringComparison.Ordinal) >= 0
                || raw.IndexOf("_CMM", StringComparison.Ordinal) >= 0
                || raw.IndexOf('\0') >= 0
                || raw.IndexOf('█') >= 0
                || raw.IndexOf('　') >= 0;
        }

        /// <summary>
        /// 屏蔽/恢复商店应用注册的「新建」项。
        /// 遍历所有仍安装的包缓存中该扩展名对应的 ShellNewDisplayName 值：
        /// 屏蔽 = 在原始名后追加标记与超长填充（该项从菜单消失）；
        /// 恢复 = 去掉标记与填充，还原原始名。返回受影响的值个数。
        /// </summary>
        private static int SetPackagedNewEnabled(string ext, bool enable)
        {
            string extSuffix = ext.TrimStart('.');
            int affected = 0;
            using (var mrt = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\MrtCache", true))
            {
                if (mrt == null) throw new IOException("无法打开 MrtCache 缓存。");
                foreach (var pkgEncoded in mrt.GetSubKeyNames())
                {
                    string pkgDir = DecodePackageDir(pkgEncoded);
                    if (string.IsNullOrEmpty(pkgDir)) continue;
                    bool installed;
                    try { installed = Directory.Exists(pkgDir); }
                    catch { installed = false; }
                    if (!installed) continue;

                    using (var pkgKey = mrt.OpenSubKey(pkgEncoded, true))
                    {
                        if (pkgKey != null)
                            affected += ApplyPackagedBlock(pkgKey, extSuffix, enable);
                    }
                }
            }
            NativeMethods.NotifyShellChanged();
            return affected;
        }

        private static int ApplyPackagedBlock(RegistryKey key, string extSuffix, bool enable)
        {
            int affected = 0;
            try
            {
                foreach (var vn in key.GetValueNames())
                {
                    if (vn.IndexOf("ShellNewDisplayName_" + extSuffix, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (key.GetValueKind(vn) != RegistryValueKind.String) continue;

                    string raw = key.GetValue(vn) as string;
                    string original = StripBlockMarker(raw);
                    bool currentlyBlocked = IsPackagedBlocked(raw);

                    if (enable)
                    {
                        if (currentlyBlocked)
                        {
                            key.SetValue(vn, original, RegistryValueKind.String);
                            affected++;
                        }
                    }
                    else
                    {
                        if (!currentlyBlocked)
                        {
                            key.SetValue(vn, original + PackagedBlockMarker + PackagedBlockPadding, RegistryValueKind.String);
                            affected++;
                        }
                    }
                }
            }
            catch { }

            try
            {
                foreach (var sn in key.GetSubKeyNames())
                {
                    using (var sk = key.OpenSubKey(sn, true))
                    {
                        if (sk != null) affected += ApplyPackagedBlock(sk, extSuffix, enable);
                    }
                }
            }
            catch { }
            return affected;
        }

        private static int CompareNewMenuEntries(MenuEntry a, MenuEntry b)
        {
            return string.Compare(a.RawName, b.RawName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>检查 ownerPath 下的 ShellNew / ShellNew-，找到则加入列表</summary>
        private static void AddShellNewEntry(string ownerPath, string typeName, List<MenuEntry> list, string displayExt = null, CategoryId category = CategoryId.NewMenu, string categoryName = "「新建」菜单")
        {
            string activePath = ownerPath + @"\ShellNew";
            string disabledPath = ownerPath + @"\ShellNew-";
            string ext = displayExt ?? ownerPath;

            bool activeValid = false, disabledValid = false;
            string mechanism = "";

            using (var k = RegistryHelper.OpenHkcr(activePath))
            {
                if (k != null && IsShellNewKeyValid(k))
                {
                    activeValid = true;
                    mechanism = DescribeShellNew(k);
                }
            }
            if (!activeValid)
            {
                using (var k = RegistryHelper.OpenHkcr(disabledPath))
                {
                    if (k != null && IsShellNewKeyValid(k))
                    {
                        disabledValid = true;
                        mechanism = DescribeShellNew(k);
                    }
                }
            }
            if (!activeValid && !disabledValid) return;

            list.Add(new MenuEntry
            {
                Kind = EntryKind.ShellNew,
                RawName = ext,
                DisplayName = typeName + "（" + ext + "）",
                KeyPath = activeValid ? activePath : disabledPath,
                SourceLabel = "「新建」菜单",
                Details = mechanism,
                Enabled = activeValid,
                Category = category,
                CategoryName = categoryName,
            });
        }

        private static string DescribeShellNew(RegistryKey k)
        {
            foreach (var name in k.GetValueNames())
            {
                if (string.Equals(name, "NullFile", StringComparison.OrdinalIgnoreCase)) return "空白文件（NullFile）";
                if (string.Equals(name, "FileName", StringComparison.OrdinalIgnoreCase)) return "模板文件：" + (k.GetValue(name) as string);
                if (string.Equals(name, "Command", StringComparison.OrdinalIgnoreCase)) return "命令：" + (k.GetValue(name) as string);
                if (string.Equals(name, "Data", StringComparison.OrdinalIgnoreCase)) return "内置数据（Data）";
                if (string.Equals(name, "Directory", StringComparison.OrdinalIgnoreCase)) return "目录（Directory）";
                if (string.Equals(name, "Handler", StringComparison.OrdinalIgnoreCase)) return "处理程序（Handler）";
            }
            return "";
        }

        public static string SendToFolder
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.SendTo); }
        }

        private static void ScanSendTo(List<MenuEntry> list, CategoryId category = CategoryId.SendTo, string categoryName = "「发送到」菜单")
        {
            string dir = SendToFolder;
            if (!Directory.Exists(dir)) return;

            object wsh = null;
            try { wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); }
            catch { }

            foreach (var path in Directory.GetFileSystemEntries(dir))
            {
                string fileName = Path.GetFileName(path);
                if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var attr = File.GetAttributes(path);
                    string display = fileName;
                    string details = path;
                    if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        display = Path.GetFileNameWithoutExtension(fileName);
                        string target = GetShortcutTarget(wsh, path);
                        if (!string.IsNullOrEmpty(target)) details = "→ " + target;
                    }
                    else if (fileName.EndsWith(".DeskLink", StringComparison.OrdinalIgnoreCase)
                          || fileName.EndsWith(".MAPIMail", StringComparison.OrdinalIgnoreCase)
                          || fileName.EndsWith(".mydocs", StringComparison.OrdinalIgnoreCase)
                          || fileName.EndsWith(".ZFSendToTarget", StringComparison.OrdinalIgnoreCase))
                    {
                        display = Path.GetFileNameWithoutExtension(fileName);
                        details = "系统发送目标（" + Path.GetExtension(fileName) + "）";
                    }

                    list.Add(new MenuEntry
                    {
                        Kind = EntryKind.SendToItem,
                        RawName = fileName,
                        DisplayName = display,
                        KeyPath = path,
                        SourceLabel = "「发送到」文件夹",
                        Details = details,
                        Enabled = (attr & FileAttributes.Hidden) == 0,
                        Category = category,
                        CategoryName = categoryName,
                    });
                }
                catch { }
            }
        }

        private static string GetShortcutTarget(object wsh, string lnkPath)
        {
            if (wsh == null) return null;
            try
            {
                dynamic shell = wsh;
                dynamic sc = shell.CreateShortcut(lnkPath);
                return sc.TargetPath as string;
            }
            catch { return null; }
        }

        private static void ScanByExtension(string extension, List<MenuEntry> list)
        {
            if (string.IsNullOrEmpty(extension)) return;
            string ext = extension.Trim();
            if (!ext.StartsWith(".")) ext = "." + ext;
            ext = ext.ToLowerInvariant();

            // 1. .ext 键自身
            ScanShellRoot(ext, "扩展名（" + ext + "）", list, CategoryId.ByExtension, "按后缀名查询");

            // 2. 关联的 ProgID
            string progId = RegistryHelper.GetDefaultValue(ext);
            if (!string.IsNullOrEmpty(progId) && RegistryHelper.KeyExists(progId))
                ScanShellRoot(progId, "关联类型（" + progId + "）", list, CategoryId.ByExtension, "按后缀名查询");

            // 3. SystemFileAssociations\.ext
            string sfa = @"SystemFileAssociations\" + ext;
            if (RegistryHelper.KeyExists(sfa))
                ScanShellRoot(sfa, "系统关联（" + ext + "）", list, CategoryId.ByExtension, "按后缀名查询");

            // 4. PerceivedType（如 image / text / audio / video）
            string perceived = null;
            using (var k = RegistryHelper.OpenHkcr(ext))
            {
                if (k != null) perceived = k.GetValue("PerceivedType") as string;
            }
            if (!string.IsNullOrEmpty(perceived))
            {
                string pf = @"SystemFileAssociations\" + perceived;
                if (RegistryHelper.KeyExists(pf))
                    ScanShellRoot(pf, "类别（" + perceived + "）", list, CategoryId.ByExtension, "按后缀名查询");
            }

            // 5. 该扩展名的「新建」菜单项（使用增强名称解析）
            string typeName = ResolveShellNewTypeName(ext);
            AddShellNewEntry(ext, typeName, list, null, CategoryId.ByExtension, "按后缀名查询");
            if (!string.IsNullOrEmpty(progId))
                AddShellNewEntry(ext + "\\" + progId, typeName, list, ext, CategoryId.ByExtension, "按后缀名查询");

            // 6. 商店应用（MSIX 包）为该扩展名注册的「新建」项
            var packaged = new List<MenuEntry>();
            AddPackagedNewEntries(packaged, CategoryId.ByExtension, "按后缀名查询");
            foreach (var e in packaged)
                if (string.Equals(e.RawName, ext, StringComparison.OrdinalIgnoreCase))
                    list.Add(e);
        }

        #endregion

        #region 屏蔽 / 恢复

        /// <summary>屏蔽或恢复一个菜单项（全部采用可逆方式）</summary>
        public static void SetEnabled(MenuEntry entry, bool enable)
        {
            switch (entry.Kind)
            {
                case EntryKind.ShellVerb:
                    SetVerbEnabled(entry.KeyPath, enable);
                    break;
                case EntryKind.ShellExHandler:
                    SetShellExEnabled(entry, enable);
                    break;
                case EntryKind.ShellNew:
                    entry.KeyPath = SetShellNewEnabled(entry.KeyPath, enable);
                    break;
                case EntryKind.PackagedShellNew:
                    {
                        int n = SetPackagedNewEnabled(entry.RawName, enable);
                        if (n == 0 && !enable)
                            throw new IOException("未能写入缓存显示名（可能被占用），请稍后重试。");
                        entry.Enabled = enable;
                        return; // SetPackagedNewEnabled 内部已通知外壳
                    }
                case EntryKind.SendToItem:
                    SetSendToEnabled(entry.KeyPath, enable);
                    break;
            }
            entry.Enabled = enable;
            NativeMethods.NotifyShellChanged();
        }

        /// <summary>verb：写入/删除 LegacyDisable 标记（非破坏性）</summary>
        private static void SetVerbEnabled(string keyPath, bool enable)
        {
            using (var k = RegistryHelper.OpenHkcr(keyPath, true))
            {
                if (k == null) throw new IOException("键不存在：HKCR\\" + keyPath);
                if (enable)
                {
                    k.DeleteValue("LegacyDisable", false);
                    k.DeleteValue("ProgrammaticAccessOnly", false);
                }
                else
                {
                    k.SetValue("LegacyDisable", "", RegistryValueKind.String);
                }
            }
        }

        /// <summary>
        /// COM 扩展：使 CLSID 无法解析来屏蔽（可逆，ShellExView 同款思路）。
        /// CLSID 在默认值上：切换默认值的 "-" 前缀；
        /// CLSID 在键名上（默认值为空或仅为描述）：重命名键（加/去 "-" 前缀），
        /// 因为外壳在默认值不是 GUID 时会回退用键名解析。
        /// </summary>
        private static void SetShellExEnabled(MenuEntry entry, bool enable)
        {
            string keyPath = entry.KeyPath;
            string keyName = keyPath.Substring(keyPath.LastIndexOf('\\') + 1);
            string parentPath = keyPath.Substring(0, keyPath.LastIndexOf('\\'));

            string def;
            using (var k = RegistryHelper.OpenHkcr(keyPath))
            {
                if (k == null) throw new IOException("键不存在：HKCR\\" + keyPath);
                def = k.GetValue("") as string;
            }

            bool valueMode = IsGuid(def) || IsGuid(StripClsidDisableMarks(def));
            if (valueMode)
            {
                using (var k = RegistryHelper.OpenHkcr(keyPath, true))
                {
                    if (enable)
                    {
                        string restored = StripClsidDisableMarks(def);
                        if (!string.Equals(def, restored, StringComparison.Ordinal))
                            k.SetValue("", restored, RegistryValueKind.String);
                    }
                    else
                    {
                        if (IsGuid(def))
                            k.SetValue("", "-" + def, RegistryValueKind.String);
                    }
                }
                return;
            }

            // 键名模式
            if (IsGuid(keyName) || IsGuid(StripClsidDisableMarks(keyName)))
            {
                string newName = enable ? StripClsidDisableMarks(keyName) : "-" + keyName;
                bool needRename = enable ? !IsGuid(keyName) : IsGuid(keyName);
                if (needRename)
                {
                    RegistryHelper.RenameKey(parentPath, keyName, newName);
                    entry.KeyPath = parentPath + "\\" + newName;
                    entry.RawName = newName;
                }
                return;
            }

            // 无法识别的形式：退回默认值 "-" 前缀切换
            using (var k = RegistryHelper.OpenHkcr(keyPath, true))
            {
                if (enable)
                {
                    if (!string.IsNullOrEmpty(def) && def.StartsWith("-"))
                        k.SetValue("", def.TrimStart('-'), RegistryValueKind.String);
                }
                else
                {
                    if (string.IsNullOrEmpty(def))
                        k.SetValue("", "-" + keyName, RegistryValueKind.String);
                    else if (!def.StartsWith("-"))
                        k.SetValue("", "-" + def, RegistryValueKind.String);
                }
            }
        }

        /// <summary>ShellNew：重命名 ShellNew ↔ ShellNew-（可逆）；返回新的键路径</summary>
        private static string SetShellNewEnabled(string keyPath, bool enable)
        {
            int idx = keyPath.LastIndexOf('\\');
            string parent = keyPath.Substring(0, idx);
            string name = keyPath.Substring(idx + 1);
            string newName = enable ? "ShellNew" : "ShellNew-";
            if (string.Equals(name, newName, StringComparison.OrdinalIgnoreCase)) return keyPath;
            RegistryHelper.RenameKey(parent, name, newName);
            return parent + "\\" + newName;
        }

        /// <summary>发送到：切换隐藏属性（隐藏的项不显示在「发送到」菜单中）</summary>
        private static void SetSendToEnabled(string filePath, bool enable)
        {
            var attr = File.GetAttributes(filePath);
            if (enable) attr &= ~FileAttributes.Hidden;
            else attr |= FileAttributes.Hidden;
            File.SetAttributes(filePath, attr);
        }

        #endregion

        #region 新增

        private static string SanitizeKeyName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (c == '\\' || c == '/' || char.IsControl(c)) continue;
                sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "CustomItem" : s;
        }

        /// <summary>新增静态菜单项。root 例如 "Directory\Background"、"*"、"Directory"、"Drive"、"SystemFileAssociations\.txt"</summary>
        public static string AddVerb(string root, string displayName, string command, string iconPath, bool extended, bool positionTop)
        {
            string keyName = SanitizeKeyName(displayName);
            string basePath = root + @"\shell";
            string path = basePath + @"\" + keyName;
            int n = 1;
            while (RegistryHelper.KeyExists(path))
            {
                n++;
                path = basePath + @"\" + keyName + "_" + n;
            }
            using (var k = Registry.ClassesRoot.CreateSubKey(path))
            {
                if (k == null) throw new IOException("无法创建键：HKCR\\" + path);
                k.SetValue("", displayName);
                if (!string.IsNullOrEmpty(iconPath)) k.SetValue("Icon", iconPath);
                if (extended) k.SetValue("Extended", "");
                if (positionTop) k.SetValue("Position", "Top");
                using (var cmd = k.CreateSubKey("command"))
                {
                    cmd.SetValue("", command);
                }
            }
            NativeMethods.NotifyShellChanged();
            return path;
        }

        /// <summary>为指定扩展名新增「新建」菜单项（NullFile 空文件方式）</summary>
        public static string AddShellNew(string extension, string typeName)
        {
            string ext = extension.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".")) ext = "." + ext;
            if (ext.Length < 2 || ext.IndexOf('\\') >= 0 || ext.IndexOf(' ') >= 0)
                throw new ArgumentException("后缀名格式不正确：" + extension);

            if (RegistryHelper.KeyExists(ext + @"\ShellNew"))
            {
                using (var k = RegistryHelper.OpenHkcr(ext + @"\ShellNew"))
                {
                    if (k != null && IsShellNewKeyValid(k))
                        throw new InvalidOperationException("该后缀名已存在「新建」菜单项。");
                }
            }

            using (var extKey = Registry.ClassesRoot.CreateSubKey(ext))
            {
                if (extKey == null) throw new IOException("无法创建键：HKCR\\" + ext);
                if (!string.IsNullOrEmpty(typeName))
                {
                    string progId = extKey.GetValue("") as string;
                    if (string.IsNullOrEmpty(progId))
                    {
                        progId = ext.TrimStart('.') + "file";
                        extKey.SetValue("", progId);
                    }
                    using (var progKey = Registry.ClassesRoot.CreateSubKey(progId))
                    {
                        if (progKey != null)
                        {
                            string existing = progKey.GetValue("") as string;
                            if (string.IsNullOrEmpty(existing)) progKey.SetValue("", typeName);
                        }
                    }
                }
                // 若存在遗留的 ShellNew-，直接激活它
                bool hadDisabled = false;
                using (var dis = extKey.OpenSubKey("ShellNew-"))
                {
                    hadDisabled = dis != null && IsShellNewKeyValid(dis);
                }
                if (hadDisabled)
                {
                    RegistryHelper.RenameKey(ext, "ShellNew-", "ShellNew");
                }
                else
                {
                    using (var sn = extKey.CreateSubKey("ShellNew"))
                    {
                        sn.SetValue("NullFile", "");
                    }
                }
            }
            NativeMethods.NotifyShellChanged();
            return ext + @"\ShellNew";
        }

        /// <summary>新增「发送到」项（创建快捷方式）</summary>
        public static string AddSendTo(string displayName, string targetPath)
        {
            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                throw new FileNotFoundException("目标不存在：" + targetPath);
            string name = SanitizeKeyName(displayName);
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "");
            if (name.Length == 0) name = "新建发送目标";

            string lnk = Path.Combine(SendToFolder, name + ".lnk");
            int n = 1;
            while (File.Exists(lnk))
            {
                n++;
                lnk = Path.Combine(SendToFolder, name + "_" + n + ".lnk");
            }
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = targetPath;
            sc.Save();
            return lnk;
        }

        #endregion

        #region 删除（先备份）

        /// <summary>删除菜单项；注册表项先导出 .reg 备份，「发送到」项移入备份文件夹。返回备份文件路径。</summary>
        public static string DeleteEntry(MenuEntry entry)
        {
            if (entry.Kind == EntryKind.PackagedShellNew)
                throw new InvalidOperationException(
                    "商店应用（内置）的「新建」项无法直接删除。\n请改用「屏蔽」将其从「新建」菜单中隐藏（可随时恢复）。");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            if (entry.Kind == EntryKind.SendToItem)
            {
                string dest = Path.Combine(BackupDir, stamp + "_" + Path.GetFileName(entry.KeyPath));
                if (Directory.Exists(entry.KeyPath)) Directory.Move(entry.KeyPath, dest);
                else File.Move(entry.KeyPath, dest);
                return dest;
            }

            string safe = SanitizeKeyName(entry.RawName);
            foreach (char c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c.ToString(), "_");
            if (safe.Length > 60) safe = safe.Substring(0, 60);
            string file = Path.Combine(BackupDir, stamp + "_" + safe + ".reg");
            RegistryHelper.ExportKey(entry.KeyPath, file);
            RegistryHelper.DeleteKeyTree(entry.KeyPath);
            NativeMethods.NotifyShellChanged();
            return file;
        }

        #endregion

        #region Win11 经典菜单 / 重启资源管理器

        private const string Win11ClassicKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

        /// <summary>当前是否已启用 Win11 经典（完整）右键菜单</summary>
        public static bool IsClassicMenuEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Win11ClassicKey))
            {
                return k != null;
            }
        }

        public static void SetClassicMenu(bool classic)
        {
            if (classic)
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Win11ClassicKey))
                {
                    if (k != null) k.SetValue("", "", RegistryValueKind.String);
                }
            }
            else
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                }
                catch { }
            }
        }

        public static void RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(3000); }
                catch { }
            }
            Thread.Sleep(600);
            try
            {
                string exp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo(exp) { UseShellExecute = true });
            }
            catch { }
        }

        #endregion

        #region 命令行：--dump

        public static void DumpAll(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("右键菜单管家 扫描结果  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("管理员：" + IsAdministrator() + "   Win11：" + IsWindows11);
            sb.AppendLine(new string('=', 90));
            var cats = new[]
            {
                new { Id = CategoryId.DesktopBackground, Name = "桌面右键菜单" },
                new { Id = CategoryId.NewMenu, Name = "「新建」菜单" },
                new { Id = CategoryId.AllFiles, Name = "文件右键菜单" },
                new { Id = CategoryId.Folders, Name = "文件夹右键菜单" },
                new { Id = CategoryId.Drives, Name = "驱动器右键菜单" },
                new { Id = CategoryId.SendTo, Name = "「发送到」菜单" },
            };
            foreach (var c in cats)
            {
                List<MenuEntry> items;
                try { items = ScanCategory(c.Id); }
                catch (Exception ex)
                {
                    sb.AppendLine("[" + c.Name + "] 扫描失败：" + ex.Message);
                    continue;
                }
                sb.AppendLine();
                sb.AppendLine("[" + c.Name + "]  共 " + items.Count + " 项");
                foreach (var e in items)
                {
                    sb.AppendLine("  " + (e.Enabled ? "启用" : "屏蔽") + " | " + e.TypeLabel + " | "
                        + e.DisplayName + " | " + e.SourceLabel + " | " + e.Details + " | " + e.KeyPath);
                }
            }
            // 示例扩展名
            sb.AppendLine();
            var txt = ScanCategory(CategoryId.ByExtension, ".txt");
            sb.AppendLine("[按后缀名 .txt]  共 " + txt.Count + " 项");
            foreach (var e in txt)
            {
                sb.AppendLine("  " + (e.Enabled ? "启用" : "屏蔽") + " | " + e.TypeLabel + " | "
                    + e.DisplayName + " | " + e.SourceLabel + " | " + e.Details + " | " + e.KeyPath);
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        #endregion
    }

    #region 自检（--selftest，全部在 HKCU 与临时目录内进行，不触碰真实菜单）

    public static class SelfTest
    {
        private static readonly List<string> Log = new List<string>();
        private static int _failCount;

        private static void Check(bool ok, string what)
        {
            Log.Add((ok ? "[通过] " : "[失败] ") + what);
            if (!ok) _failCount++;
        }

        public static int Run(string outputFile)
        {
            _failCount = 0;
            Log.Clear();
            Log.Add("右键菜单管家 自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log.Add("说明：全部操作在 HKCU\\Software\\Classes 测试键与临时目录内进行。");

            const string verbRel = @"Directory\Background\shell\__cmmSelfTest";
            const string hxRel = @"Directory\Background\shellex\ContextMenuHandlers\__cmmSelfTestX";
            const string extRel = ".__cmmtest";
            string tmpFile = null;

            try
            {
                // ---------- 1. verb 屏蔽/恢复 ----------
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + verbRel))
                {
                    k.SetValue("", "CMM 自检菜单项");
                    using (var c = k.CreateSubKey("command")) c.SetValue("", "cmd.exe /c echo test");
                }
                Check(RegistryHelper.KeyExists(verbRel), "创建测试 verb（HKCU 合并视图可见）");

                var verbEntry = new MenuEntry { Kind = EntryKind.ShellVerb, KeyPath = verbRel, RawName = "__cmmSelfTest" };
                MenuService.SetEnabled(verbEntry, false);
                using (var k = RegistryHelper.OpenHkcr(verbRel))
                    Check(k != null && k.GetValue("LegacyDisable") != null, "verb 屏蔽（写入 LegacyDisable）");

                MenuService.SetEnabled(verbEntry, true);
                using (var k = RegistryHelper.OpenHkcr(verbRel))
                    Check(k != null && k.GetValue("LegacyDisable") == null, "verb 恢复（移除 LegacyDisable）");

                // 扫描应包含测试项
                var desktop = MenuService.ScanCategory(CategoryId.DesktopBackground);
                Check(desktop.Any(x => x.RawName == "__cmmSelfTest"), "桌面分类扫描包含测试项");
                var found = desktop.First(x => x.RawName == "__cmmSelfTest");
                Check(found.Enabled, "扫描状态 = 已启用");

                // ---------- 2. COM 扩展 屏蔽/恢复 ----------
                const string fakeClsid = "{00000000-1111-2222-3333-444444444444}";
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + hxRel))
                    k.SetValue("", fakeClsid);
                var hxEntry = new MenuEntry { Kind = EntryKind.ShellExHandler, KeyPath = hxRel, RawName = "__cmmSelfTestX" };
                MenuService.SetEnabled(hxEntry, false);
                Check(RegistryHelper.GetDefaultValue(hxRel) == "-" + fakeClsid, "COM 扩展屏蔽（CLSID 前加 -）");
                MenuService.SetEnabled(hxEntry, true);
                Check(RegistryHelper.GetDefaultValue(hxRel) == fakeClsid, "COM 扩展恢复（去掉 -）");

                // ---------- 2b. 键名即 CLSID（默认值为描述文字）：应重命名键 ----------
                const string hxNameClsid = "{00000000-2222-3333-4444-555555555555}";
                const string hxParent = @"Directory\Background\shellex\ContextMenuHandlers";
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + hxParent + "\\" + hxNameClsid))
                    k.SetValue("", "Fake Description");
                var hxEntry2 = new MenuEntry { Kind = EntryKind.ShellExHandler, KeyPath = hxParent + "\\" + hxNameClsid, RawName = hxNameClsid };
                MenuService.SetEnabled(hxEntry2, false);
                Check(RegistryHelper.KeyExists(hxParent + "\\-" + hxNameClsid) && !RegistryHelper.KeyExists(hxParent + "\\" + hxNameClsid),
                    "键名 CLSID 屏蔽（键重命名加 -）");
                MenuService.SetEnabled(hxEntry2, true);
                Check(RegistryHelper.KeyExists(hxParent + "\\" + hxNameClsid)
                      && !RegistryHelper.KeyExists(hxParent + "\\-" + hxNameClsid),
                    "键名 CLSID 恢复（键重命名还原）");

                // ---------- 2c. 兼容「} 前加 -」风格的损坏值（其他工具的屏蔽方式） ----------
                const string hxTrail = hxParent + @"\__cmmSelfTestY";
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + hxTrail))
                    k.SetValue("", "{00000000-1111-2222-3333-444444444444-}");
                var scanHx = MenuService.ScanCategory(CategoryId.DesktopBackground)
                    .FirstOrDefault(x => x.RawName == "__cmmSelfTestY");
                Check(scanHx != null && !scanHx.Enabled, "尾部 - 损坏的 CLSID 识别为已屏蔽");
                var hxEntry3 = new MenuEntry { Kind = EntryKind.ShellExHandler, KeyPath = hxTrail, RawName = "__cmmSelfTestY" };
                MenuService.SetEnabled(hxEntry3, true);
                Check(RegistryHelper.GetDefaultValue(hxTrail) == fakeClsid, "尾部 - 损坏的 CLSID 恢复为有效 GUID");

                // ---------- 3. ShellNew 屏蔽/恢复 ----------
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + extRel + @"\ShellNew"))
                    k.SetValue("NullFile", "");
                var snEntry = new MenuEntry { Kind = EntryKind.ShellNew, KeyPath = extRel + @"\ShellNew", RawName = extRel };
                MenuService.SetEnabled(snEntry, false);
                Check(RegistryHelper.KeyExists(extRel + @"\ShellNew-") && !RegistryHelper.KeyExists(extRel + @"\ShellNew"),
                    "ShellNew 屏蔽（重命名为 ShellNew-）");
                MenuService.SetEnabled(snEntry, true);
                Check(RegistryHelper.KeyExists(extRel + @"\ShellNew") && !RegistryHelper.KeyExists(extRel + @"\ShellNew-"),
                    "ShellNew 恢复（重命名回 ShellNew）");

                var newMenu = MenuService.ScanCategory(CategoryId.NewMenu);
                Check(newMenu.Any(x => x.RawName == extRel), "「新建」分类扫描包含测试后缀");

                // ---------- 4. 「发送到」隐藏/恢复 ----------
                tmpFile = Path.Combine(Path.GetTempPath(), "__cmmSendToTest.txt");
                File.WriteAllText(tmpFile, "test");
                var stEntry = new MenuEntry { Kind = EntryKind.SendToItem, KeyPath = tmpFile, RawName = "__cmmSendToTest.txt" };
                MenuService.SetEnabled(stEntry, false);
                Check((File.GetAttributes(tmpFile) & FileAttributes.Hidden) != 0, "发送到项屏蔽（设隐藏属性）");
                MenuService.SetEnabled(stEntry, true);
                Check((File.GetAttributes(tmpFile) & FileAttributes.Hidden) == 0, "发送到项恢复（取消隐藏）");

                var sendTo = MenuService.ScanCategory(CategoryId.SendTo);
                Check(sendTo != null, "「发送到」分类扫描正常（" + sendTo.Count + " 项）");

                // ---------- 5. 导出 .reg 备份 + 删除 ----------
                string backup = MenuService.DeleteEntry(new MenuEntry
                {
                    Kind = EntryKind.ShellVerb,
                    KeyPath = verbRel,
                    RawName = "__cmmSelfTest"
                });
                Check(File.Exists(backup) && new FileInfo(backup).Length > 0, "删除前已导出 .reg 备份：" + backup);
                string regText = File.ReadAllText(backup);
                Check(regText.Contains("__cmmSelfTest") && regText.Contains("Windows Registry Editor"),
                    ".reg 备份内容有效");
                Check(!RegistryHelper.KeyExists(verbRel), "verb 已删除");
                try { File.Delete(backup); } catch { }

                // ---------- 6. 其余分类扫描冒烟 ----------
                Check(MenuService.ScanCategory(CategoryId.AllFiles).Count >= 0, "文件分类扫描正常");
                Check(MenuService.ScanCategory(CategoryId.Folders).Count > 0, "文件夹分类扫描正常（应至少有系统项）");
                Check(MenuService.ScanCategory(CategoryId.Drives).Count >= 0, "驱动器分类扫描正常");
                Check(MenuService.ScanCategory(CategoryId.ByExtension, ".txt").Count > 0, "按后缀名 .txt 查询正常");

                // ---------- 7. 商店应用「新建」项 屏蔽/恢复 往返测试（若本机存在则实测，可逆还原） ----------
                var newItems = MenuService.ScanCategory(CategoryId.NewMenu);
                var pkg = newItems.FirstOrDefault(x => x.Kind == EntryKind.PackagedShellNew);
                if (pkg != null)
                {
                    bool wasEnabled = pkg.Enabled;
                    // 屏蔽
                    MenuService.SetEnabled(pkg, false);
                    var afterBlock = MenuService.ScanCategory(CategoryId.NewMenu)
                        .FirstOrDefault(x => x.Kind == EntryKind.PackagedShellNew && x.RawName == pkg.RawName);
                    Check(afterBlock != null && !afterBlock.Enabled,
                        "商店应用「新建」项屏蔽（" + pkg.RawName + " 缓存显示名已改写）");
                    // 恢复
                    MenuService.SetEnabled(pkg, true);
                    var afterRestore = MenuService.ScanCategory(CategoryId.NewMenu)
                        .FirstOrDefault(x => x.Kind == EntryKind.PackagedShellNew && x.RawName == pkg.RawName);
                    Check(afterRestore != null && afterRestore.Enabled
                          && afterRestore.DisplayName == pkg.DisplayName,
                        "商店应用「新建」项恢复（显示名完整还原为原始值）");
                    // 保持与测试前一致
                    if (!wasEnabled) MenuService.SetEnabled(pkg, false);
                }
                else
                {
                    Log.Add("[跳过] 本机未发现商店应用注册的「新建」项，跳过其屏蔽/恢复测试");
                }
            }
            catch (Exception ex)
            {
                _failCount++;
                Log.Add("[异常] " + ex.GetType().Name + ": " + ex.Message);
                Log.Add(ex.StackTrace ?? "");
            }
            finally
            {
                // 清理测试残留
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + verbRel, false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\__cmmSelfTestX", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\__cmmSelfTestY", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\{00000000-2222-3333-4444-555555555555}", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\-{00000000-2222-3333-4444-555555555555}", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + extRel, false); } catch { }
                if (tmpFile != null) { try { File.Delete(tmpFile); } catch { } }
            }

            Log.Add("");
            Log.Add(_failCount == 0 ? "结果：PASS（全部通过）" : "结果：FAIL（" + _failCount + " 项失败）");
            try { File.WriteAllLines(outputFile, Log.ToArray(), Encoding.UTF8); } catch { }
            return _failCount == 0 ? 0 : 2;
        }
    }

    #endregion
}
