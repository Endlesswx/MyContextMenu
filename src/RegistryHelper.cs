using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ContextMenuManager
{
    /// <summary>HKEY_CLASSES_ROOT（合并视图）注册表操作辅助</summary>
    public static class RegistryHelper
    {
        /// <summary>打开 HKCR 下的子键；不存在返回 null</summary>
        public static RegistryKey OpenHkcr(string path, bool writable = false)
        {
            return Registry.ClassesRoot.OpenSubKey(path, writable);
        }

        public static bool KeyExists(string path)
        {
            using (var k = OpenHkcr(path)) return k != null;
        }

        public static string GetDefaultValue(string path)
        {
            using (var k = OpenHkcr(path))
            {
                return k == null ? null : k.GetValue("") as string;
            }
        }

        /// <summary>递归复制键（值 + 子键）</summary>
        public static void CopyKeyTree(RegistryKey src, RegistryKey dst)
        {
            foreach (var name in src.GetValueNames())
            {
                object v = src.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                dst.SetValue(name, v, src.GetValueKind(name));
            }
            foreach (var sub in src.GetSubKeyNames())
            {
                using (var s = src.OpenSubKey(sub))
                using (var d = dst.CreateSubKey(sub))
                {
                    if (s != null && d != null) CopyKeyTree(s, d);
                }
            }
        }

        private const string ClassesPrefix = @"Software\Classes\";

        private static bool ExistsInBranch(RegistryKey branchRoot, string classesRelPath)
        {
            using (var k = branchRoot.OpenSubKey(ClassesPrefix + classesRelPath))
            {
                return k != null;
            }
        }

        /// <summary>
        /// 重命名 HKCR 下的键（复制后删除源；目标已存在时先清除目标）。
        /// 注意：通过 HKCR 合并视图创建新键总是落到 HKLM，因此这里显式定位键
        /// 实际所在的分支（优先 HKCU\Software\Classes，其次 HKLM\Software\Classes）。
        /// </summary>
        public static void RenameKey(string parentPath, string oldName, string newName)
        {
            string fullOld = parentPath + "\\" + oldName;
            RegistryKey branchRoot;
            if (ExistsInBranch(Registry.CurrentUser, fullOld)) branchRoot = Registry.CurrentUser;
            else if (ExistsInBranch(Registry.LocalMachine, fullOld)) branchRoot = Registry.LocalMachine;
            else throw new IOException("键不存在：HKCR\\" + fullOld);

            using (var parent = branchRoot.OpenSubKey(ClassesPrefix + parentPath, true))
            {
                if (parent == null) throw new IOException("父键不存在：" + parentPath);
                using (var src = parent.OpenSubKey(oldName))
                {
                    if (src == null) throw new IOException("键不存在：" + parentPath + "\\" + oldName);
                    try { parent.DeleteSubKeyTree(newName); }
                    catch (ArgumentException) { }
                    using (var dst = parent.CreateSubKey(newName))
                    {
                        CopyKeyTree(src, dst);
                    }
                }
                parent.DeleteSubKeyTree(oldName);
            }
        }

        /// <summary>
        /// 删除 HKCR 下的键（递归）。在 HKCU 与 HKLM 两个实际分支中删除所有存在的副本，
        /// 避免删除后另一分支的同名键“复活”。
        /// </summary>
        public static void DeleteKeyTree(string path)
        {
            int idx = path.LastIndexOf('\\');
            if (idx <= 0) throw new ArgumentException("非法路径：" + path);
            string parentPath = path.Substring(0, idx);
            string name = path.Substring(idx + 1);

            bool deleted = false;
            Exception firstError = null;
            var roots = new[] { Registry.CurrentUser, Registry.LocalMachine };
            foreach (var root in roots)
            {
                try
                {
                    if (!ExistsInBranch(root, path)) continue;
                    using (var parent = root.OpenSubKey(ClassesPrefix + parentPath, true))
                    {
                        if (parent == null) continue;
                        parent.DeleteSubKeyTree(name);
                        deleted = true;
                    }
                }
                catch (Exception ex)
                {
                    if (firstError == null) firstError = ex;
                }
            }
            if (!deleted)
            {
                if (firstError != null) throw firstError;
                throw new IOException("键不存在：HKCR\\" + path);
            }
        }

        #region .reg 导出

        /// <summary>将 HKCR 下的键导出为 .reg 文件（UTF-16LE，regedit 可直接导入还原）</summary>
        public static void ExportKey(string hkcrRelativePath, string filePath)
        {
            using (var key = OpenHkcr(hkcrRelativePath))
            {
                if (key == null) throw new IOException("键不存在：HKCR\\" + hkcrRelativePath);
                var sb = new StringBuilder();
                sb.AppendLine("Windows Registry Editor Version 5.00");
                sb.AppendLine();
                ExportKeyRecursive(key, @"HKEY_CLASSES_ROOT\" + hkcrRelativePath, sb);
                File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
            }
        }

        private static void ExportKeyRecursive(RegistryKey key, string fullName, StringBuilder sb)
        {
            sb.AppendLine("[" + fullName + "]");
            foreach (var name in key.GetValueNames())
            {
                string label = name.Length == 0 ? "@" : "\"" + EscapeRegString(name) + "\"";
                sb.AppendLine(label + "=" + FormatRegValue(key, name));
            }
            sb.AppendLine();
            foreach (var sub in key.GetSubKeyNames())
            {
                using (var s = key.OpenSubKey(sub))
                {
                    if (s != null) ExportKeyRecursive(s, fullName + "\\" + sub, sb);
                }
            }
        }

        private static string EscapeRegString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatRegValue(RegistryKey key, string name)
        {
            RegistryValueKind kind = key.GetValueKind(name);
            object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            switch (kind)
            {
                case RegistryValueKind.String:
                    return "\"" + EscapeRegString(Convert.ToString(value)) + "\"";
                case RegistryValueKind.DWord:
                    return "dword:" + ((uint)Convert.ToInt64(value)).ToString("x8");
                case RegistryValueKind.QWord:
                    return "hex(b):" + BytesToHex(BitConverter.GetBytes(Convert.ToInt64(value)));
                case RegistryValueKind.ExpandString:
                    return "hex(2):" + BytesToHex(Encoding.Unicode.GetBytes(Convert.ToString(value) + "\0"));
                case RegistryValueKind.MultiString:
                    {
                        var parts = value as string[] ?? new string[0];
                        var joined = string.Join("\0", parts) + "\0\0";
                        return "hex(7):" + BytesToHex(Encoding.Unicode.GetBytes(joined));
                    }
                case RegistryValueKind.Binary:
                default:
                    {
                        var bytes = value as byte[] ?? new byte[0];
                        return "hex:" + BytesToHex(bytes);
                    }
            }
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        #endregion

        /// <summary>在注册表编辑器中定位到指定完整路径</summary>
        public static void JumpToRegedit(string fullPath)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
            {
                if (k != null) k.SetValue("LastKey", fullPath);
            }
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
    }
}
