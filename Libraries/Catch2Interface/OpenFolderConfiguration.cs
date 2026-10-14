using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Catch2Interface
{
    // Open Folder is opt-in: neither the folder provider nor fallback settings
    // may start executables without both this configuration and FilenameFilter.
    public sealed class OpenFolderConfiguration
    {
        public SettingsManager Settings { get; private set; }
        public string SettingsFile { get; private set; }
        public string WorkspaceRoot { get; private set; }
        public IReadOnlyList<string> SearchDirectories { get; private set; }
        public string TargetPlatform { get; private set; }
        public DateTime SettingsTimestamp { get; private set; }

        public static OpenFolderConfiguration Load(string workspaceRoot)
        {
            var settingsFile = Path.Combine(workspaceRoot, ".runsettings");
            if (!File.Exists(settingsFile)) return null;

            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(settingsFile, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            }))
            {
                document.Load(reader);
            }
            var adapter = document.SelectSingleNode("/RunSettings/Catch2Adapter");
            var enabled = adapter?.SelectSingleNode("OpenFolder/Enabled")?.InnerText;
            if (!bool.TryParse(enabled, out var isEnabled) || !isEnabled) return null;
            var settings = SettingsManager.Extract(adapter);
            if (settings.IsDisabled || settings.General.IsExeDiscoveryDisabled) return null;

            var directories = adapter.SelectNodes("OpenFolder/SearchDirectory")
                .Cast<XmlNode>()
                .Select(node => node.InnerText.Trim())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => Path.GetFullPath(Path.IsPathRooted(path)
                    ? path : Path.Combine(workspaceRoot, path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (directories.Length == 0) return null;

            return new OpenFolderConfiguration
            {
                Settings = settings,
                SettingsFile = settingsFile,
                WorkspaceRoot = Path.GetFullPath(workspaceRoot),
                SearchDirectories = directories,
                TargetPlatform = document.SelectSingleNode("/RunSettings/RunConfiguration/TargetPlatform")?.InnerText,
                SettingsTimestamp = File.GetLastWriteTimeUtc(settingsFile)
            };
        }

        public IReadOnlyList<string> FindExecutables()
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in SearchDirectories)
            {
                ScanDirectory(directory, files);
            }
            return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void ScanDirectory(string directory, HashSet<string> files)
        {
            if (!Directory.Exists(directory)) return;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                foreach (var source in Directory.EnumerateFiles(directory, "*.exe"))
                {
                    var settings = Settings.GetSourceSettings(source);
                    if (!settings.Disabled && !settings.IsExeDiscoveryDisabled &&
                        settings.FilenameFilter.IsMatch(Path.GetFileNameWithoutExtension(source)))
                    {
                        files.Add(Path.GetFullPath(source));
                    }
                }
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    ScanDirectory(child, files);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static SettingsManager FindSettings(IEnumerable<string> sources)
        {
            // An explicit VS runsettings selection always takes priority. This
            // fallback only applies when the test host supplies no adapter node.
            foreach (var source in sources ?? Enumerable.Empty<string>())
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(source));
                while (!string.IsNullOrEmpty(directory))
                {
                    try
                    {
                        var configuration = Load(directory);
                        if (configuration != null && configuration.FindExecutables()
                            .Contains(Path.GetFullPath(source), StringComparer.OrdinalIgnoreCase))
                        {
                            return configuration.Settings;
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (XmlException) { }
                    catch (ArgumentException) { }
                    directory = Path.GetDirectoryName(directory);
                }
            }
            return null;
        }
    }
}
