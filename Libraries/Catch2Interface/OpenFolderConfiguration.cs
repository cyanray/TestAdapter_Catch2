using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        public IReadOnlyList<string> WatchDirectories { get; private set; }
        public bool ActiveConfigurationOnly { get; private set; }
        public bool HasSearchPatterns { get; private set; }
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

            var patterns = adapter.SelectNodes("OpenFolder/SearchDirectory")
                .Cast<XmlNode>()
                .Select(node => node.InnerText.Trim())
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var activeSetting = adapter.SelectSingleNode("OpenFolder/ActiveConfigurationOnly")?.InnerText;
            var activeOnly = false;
            if (activeSetting != null && !bool.TryParse(activeSetting, out activeOnly)) return null;
            if (patterns.Length == 0 && !activeOnly) return null;
            var directories = patterns.SelectMany(pattern => ExpandSearchDirectory(workspaceRoot, pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var watchDirectories = patterns.Select(pattern => GetPatternRoot(workspaceRoot, pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            return new OpenFolderConfiguration
            {
                Settings = settings,
                SettingsFile = settingsFile,
                WorkspaceRoot = Path.GetFullPath(workspaceRoot),
                SearchDirectories = directories,
                WatchDirectories = watchDirectories,
                ActiveConfigurationOnly = activeOnly,
                HasSearchPatterns = patterns.Length != 0,
                TargetPlatform = document.SelectSingleNode("/RunSettings/RunConfiguration/TargetPlatform")?.InnerText,
                SettingsTimestamp = File.GetLastWriteTimeUtc(settingsFile)
            };
        }

        public IReadOnlyList<string> FindExecutables() => FindExecutables(null);

        public IReadOnlyList<string> FindExecutables(IEnumerable<string> activeBuildDirectories)
        {
            IEnumerable<string> scanDirectories = SearchDirectories;
            if (activeBuildDirectories != null)
            {
                var buildDirectories = activeBuildDirectories.Where(path => !string.IsNullOrEmpty(path))
                    .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                scanDirectories = !HasSearchPatterns ? buildDirectories : SearchDirectories.SelectMany(directory => buildDirectories.Select(build =>
                    IsUnder(directory, build) ? directory : IsUnder(build, directory) ? build : null))
                    .Where(path => path != null).Distinct(StringComparer.OrdinalIgnoreCase);
            }
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in scanDirectories)
            {
                ScanDirectory(directory, files);
            }
            return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool IsUnder(string path, string root)
        {
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPatternRoot(string workspaceRoot, string pattern)
        {
            pattern = pattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var wildcard = pattern.IndexOfAny(new[] { '*', '?' });
            var prefix = pattern;
            if (wildcard >= 0)
            {
                var separator = pattern.LastIndexOf(Path.DirectorySeparatorChar, wildcard);
                prefix = separator < 0 ? "." : pattern.Substring(0, separator + 1);
            }
            return Path.GetFullPath(Path.IsPathRooted(prefix) ? prefix : Path.Combine(workspaceRoot, prefix));
        }

        private static IEnumerable<string> ExpandSearchDirectory(string workspaceRoot, string pattern)
        {
            pattern = pattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var wildcard = pattern.IndexOfAny(new[] { '*', '?' });
            var searchRoot = GetPatternRoot(workspaceRoot, pattern);
            if (wildcard < 0) return new[] { searchRoot };
            var separator = pattern.LastIndexOf(Path.DirectorySeparatorChar, wildcard);
            var segments = pattern.Substring(separator + 1)
                .Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment == "." || segment == ".."))
            {
                throw new ArgumentException("Dot segments after a directory wildcard are not supported.", nameof(pattern));
            }
            var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExpandDirectory(searchRoot, segments, 0, matches);
            return matches.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void ExpandDirectory(string directory, string[] segments, int index, HashSet<string> matches)
        {
            try
            {
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                if (index == segments.Length)
                {
                    matches.Add(Path.GetFullPath(directory));
                    return;
                }
                var segment = segments[index];
                if (segment == "**")
                {
                    ExpandDirectory(directory, segments, index + 1, matches);
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        ExpandDirectory(child, segments, index, matches);
                    }
                }
                else
                {
                    var expression = "^" + Regex.Escape(segment).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        if (Regex.IsMatch(Path.GetFileName(child), expression,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                        {
                            ExpandDirectory(child, segments, index + 1, matches);
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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

        private bool ContainsSourceForSettings(string source)
        {
            if (HasSearchPatterns)
            {
                return FindExecutables().Contains(source, StringComparer.OrdinalIgnoreCase);
            }
            if (!ActiveConfigurationOnly || !File.Exists(source) ||
                !string.Equals(Path.GetExtension(source), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
            var settings = Settings.GetSourceSettings(source);
            if (settings.Disabled || settings.IsExeDiscoveryDisabled ||
                !settings.FilenameFilter.IsMatch(Path.GetFileNameWithoutExtension(source))) return false;

            // The separate test host cannot query VS's active configuration.
            // Only reuse automatic-directory settings for a CMake output whose
            // cache identifies this source workspace. VS limits the containers.
            var directory = Path.GetDirectoryName(source);
            while (!string.IsNullOrEmpty(directory) && IsUnder(directory, WorkspaceRoot))
            {
                var cache = Path.Combine(directory, "CMakeCache.txt");
                if (File.Exists(cache))
                {
                    foreach (var line in File.ReadLines(cache))
                    {
                        if (!line.StartsWith("CMAKE_HOME_DIRECTORY:", StringComparison.Ordinal)) continue;
                        var separator = line.IndexOf('=');
                        return separator >= 0 && string.Equals(Path.GetFullPath(line.Substring(separator + 1)),
                            WorkspaceRoot, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                }
                directory = Path.GetDirectoryName(directory);
            }
            return false;
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
                        if (configuration != null && configuration.ContainsSourceForSettings(Path.GetFullPath(source)))
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
