using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextMenuManager
{
    internal static class NativeMethods
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        /// <summary>解析 "@dll,-123" 形式的本地化资源字符串；失败时原样返回</summary>
        public static string LoadIndirectString(string source)
        {
            if (string.IsNullOrEmpty(source) || source[0] != '@') return source;
            try
            {
                var sb = new StringBuilder(1024);
                int hr = SHLoadIndirectString(source, sb, sb.Capacity, IntPtr.Zero);
                if (hr == 0 && sb.Length > 0) return sb.ToString();
            }
            catch { }
            return source;
        }

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        /// <summary>通知资源管理器关联信息已变化（多数修改无需重启资源管理器即可生效）</summary>
        public static void NotifyShellChanged()
        {
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
            catch { }
        }
    }
}
