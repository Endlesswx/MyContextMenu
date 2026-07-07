using System;
using System.Diagnostics;
using System.Windows;

namespace ContextMenuManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            string[] args = e.Args ?? new string[0];

            // 命令行模式：--selftest [输出文件] / --dump [输出文件]
            string selftestPath = GetArgValue(args, "--selftest");
            if (selftestPath != null)
            {
                int code;
                try { code = SelfTest.Run(selftestPath.Length == 0 ? DefaultPath("selftest_result.txt") : selftestPath); }
                catch { code = 3; }
                Environment.Exit(code);
                return;
            }
            string dumpPath = GetArgValue(args, "--dump");
            if (dumpPath != null)
            {
                int code = 0;
                try { MenuService.DumpAll(dumpPath.Length == 0 ? DefaultPath("menu_dump.txt") : dumpPath); }
                catch { code = 3; }
                Environment.Exit(code);
                return;
            }

            // 建议以管理员身份运行（系统级菜单位于 HKLM，普通权限无法修改）
            bool noElevate = HasFlag(args, "--noelevate");
            if (!MenuService.IsAdministrator() && !noElevate)
            {
                var r = MessageBox.Show(
                    "修改系统右键菜单通常需要管理员权限。\n\n" +
                    "是否立即以管理员身份重新启动本程序？\n\n" +
                    "选择“否”将以普通权限继续运行：可以浏览全部菜单，\n但对系统级（HKLM）菜单项的修改将会失败。",
                    "右键菜单管家", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                            UseShellExecute = true,
                            Verb = "runas"
                        });
                        Shutdown();
                        return;
                    }
                    catch
                    {
                        // 用户取消了 UAC，继续普通权限运行
                    }
                }
            }

            new MainWindow().Show();
        }

        private static string DefaultPath(string fileName)
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>返回 null = 无此参数；返回 "" = 有参数但未跟路径</summary>
        private static string GetArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) return args[i + 1];
                    return "";
                }
            }
            return null;
        }
    }
}
