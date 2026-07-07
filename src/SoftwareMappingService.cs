using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ContextMenuManager
{
    /// <summary>软件关键字映射服务：管理菜单项与关联软件的匹配规则</summary>
    public static class SoftwareMappingService
    {
        private static readonly Dictionary<string, string> _mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _configFilePath;

        static SoftwareMappingService()
        {
            _configFilePath = Path.Combine(MenuService.AppDir, "SoftwareMapping.xml");
            LoadMappings();
        }

        /// <summary>获取配置文件路径（供UI显示）</summary>
        public static string ConfigFilePath { get { return _configFilePath; } }

        /// <summary>加载映射配置（初始化时自动调用，也可手动重新加载）</summary>
        public static void LoadMappings()
        {
            _mappings.Clear();

            // 内置默认映射（系统初始化时自带）
            var builtInMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "qingshellext", "WPS网盘/云文档" },
                { "AABdzCtx", "Bandizip" },
                { "YunShellExplorerCommand", "百度网盘" },
                { "YunShellExt", "百度网盘" },
                { "ModernSharing", "共享" },
                { "WinRAR", "WinRAR" },
                { "7-Zip", "7-Zip" },
                { "Notepad++", "Notepad++" },
                { "GitExtensions", "Git Extensions" },
                { "TortoiseGit", "TortoiseGit" },
                { "TortoiseSVN", "TortoiseSVN" },
                { "Everything", "Everything" },
                { "VSCode", "Visual Studio Code" },
                { "PotPlayer", "PotPlayer" },
            };

            foreach (var kv in builtInMappings)
                _mappings[kv.Key] = kv.Value;

            // 从配置文件加载用户自定义映射（覆盖内置）
            if (File.Exists(_configFilePath))
            {
                try
                {
                    var doc = XDocument.Load(_configFilePath);
                    var root = doc.Root;
                    if (root != null && root.Name == "SoftwareMappings")
                    {
                        foreach (var item in root.Elements("Mapping"))
                        {
                            string keyword = item.Attribute("Keyword")?.Value;
                            string software = item.Attribute("Software")?.Value;
                            
                            if (!string.IsNullOrWhiteSpace(keyword) && !string.IsNullOrWhiteSpace(software))
                            {
                                _mappings[keyword.Trim()] = software.Trim();
                            }
                        }
                    }
                }
                catch
                {
                    /* 读取失败时使用内置映射 */
                }
            }
            else
            {
                // 配置文件不存在时，创建默认配置文件
                try
                {
                    SaveMappings();
                }
                catch { /* 创建失败不影响程序运行 */ }
            }
        }

        /// <summary>保存当前映射到配置文件</summary>
        public static void SaveMappings()
        {
            try
            {
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "yes"),
                    new XComment("右键菜单关联软件映射配置"),
                    new XComment("Keyword: 用于匹配的关键字（不区分大小写），程序会在菜单项的名称、命令、DLL路径等信息中搜索"),
                    new XComment("Software: 关联的软件名称，将显示在「关联软件」列中"),
                    new XElement("SoftwareMappings",
                        _mappings.OrderBy(x => x.Key).Select(kv =>
                            new XElement("Mapping",
                                new XAttribute("Keyword", kv.Key),
                                new XAttribute("Software", kv.Value)
                            )
                        )
                    )
                );

                // 使用 UTF-8 with BOM 保存，确保中文正确显示
                using (var writer = new StreamWriter(_configFilePath, false, new UTF8Encoding(true)))
                {
                    doc.Save(writer);
                }
            }
            catch (Exception ex)
            {
                throw new IOException("保存配置文件失败：" + ex.Message);
            }
        }

        /// <summary>为菜单项匹配关联软件名称</summary>
        public static string MatchSoftware(MenuEntry entry)
        {
            if (entry == null) return null;

            // 在这些字段中搜索关键字
            var searchFields = new[]
            {
                entry.RawName,
                entry.DisplayName,
                entry.KeyPath,
                entry.Details,
                entry.SourceLabel
            };

            foreach (var kv in _mappings)
            {
                string keyword = kv.Key;
                foreach (var field in searchFields)
                {
                    if (!string.IsNullOrEmpty(field) &&
                        field.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return kv.Value;
                    }
                }
            }

            return null;
        }

        /// <summary>批量为菜单项列表匹配关联软件</summary>
        public static void MatchSoftwareForEntries(IEnumerable<MenuEntry> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
            {
                entry.AssociatedSoftware = MatchSoftware(entry);
            }
        }

        /// <summary>获取所有映射规则（供UI编辑）</summary>
        public static Dictionary<string, string> GetAllMappings()
        {
            return new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>添加或更新映射规则</summary>
        public static void SetMapping(string keyword, string softwareName)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                throw new ArgumentException("关键字不能为空");
            if (string.IsNullOrWhiteSpace(softwareName))
                throw new ArgumentException("软件名称不能为空");

            _mappings[keyword.Trim()] = softwareName.Trim();
        }

        /// <summary>删除映射规则</summary>
        public static bool RemoveMapping(string keyword)
        {
            return _mappings.Remove(keyword);
        }

        /// <summary>检查关键字是否已存在</summary>
        public static bool ContainsKeyword(string keyword)
        {
            return _mappings.ContainsKey(keyword);
        }
    }
}
