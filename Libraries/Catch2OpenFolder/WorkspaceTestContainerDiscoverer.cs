using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catch2Interface;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Catch2OpenFolder
{
    [Export(typeof(ITestContainerDiscoverer))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public sealed class WorkspaceTestContainerDiscoverer : ITestContainerDiscoverer, IDisposable
    {
        private readonly IVsFolderWorkspaceService workspaceService;
        private readonly object sync = new object();
        private readonly Timer refreshTimer;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private ITestContainer[] containers = new ITestContainer[0];
        private string root;
        private long generation;
        private long revision;
        private bool disposed;
        private int refreshing;
        private string fingerprint;

        [ImportingConstructor]
        public WorkspaceTestContainerDiscoverer(IVsFolderWorkspaceService workspaceService)
        {
            this.workspaceService = workspaceService;
            refreshTimer = new Timer(Refresh, null, Timeout.Infinite, Timeout.Infinite);
            workspaceService.OnActiveWorkspaceChanged += WorkspaceChangedAsync;
            ConfigureWorkspace();
        }
        public Uri ExecutorUri => new Uri("executor://Catch2TestExecutor");
        public IEnumerable<ITestContainer> TestContainers
        {
            get { lock (sync) { return containers.ToArray(); } }
        }
        public event EventHandler TestContainersUpdated;

        private Task WorkspaceChangedAsync(object sender, EventArgs args)
        {
            ConfigureWorkspace();
            return Task.CompletedTask;
        }
        private void ConfigureWorkspace()
        {
            lock (sync)
            {
                if (disposed) return;
                generation++;
                DisposeWatchers();
                root = workspaceService.CurrentWorkspace?.Location;
                fingerprint = null;
                containers = new ITestContainer[0];
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) Watch(root);
                refreshTimer.Change(100, Timeout.Infinite);
            }
            TestContainersUpdated?.Invoke(this, EventArgs.Empty);
        }
        private void Watch(string directory)
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += FileChanged;
            watcher.Created += FileChanged;
            watcher.Deleted += FileChanged;
            watcher.Renamed += FileChanged;
            watcher.Error += (sender, args) => ScheduleRefresh();
            watchers.Add(watcher);
            watcher.EnableRaisingEvents = true;
        }
        private void FileChanged(object sender, FileSystemEventArgs args)
        {
            if (args.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                args.FullPath.EndsWith(".runsettings", StringComparison.OrdinalIgnoreCase) ||
                args.ChangeType == WatcherChangeTypes.Renamed ||
                args.ChangeType == WatcherChangeTypes.Created ||
                args.ChangeType == WatcherChangeTypes.Deleted)
            {
                ScheduleRefresh();
            }
        }
        private void ScheduleRefresh()
        {
            lock (sync)
            {
                if (!disposed) refreshTimer.Change(750, Timeout.Infinite);
            }
        }
        private void Refresh(object state)
        {
            if (Interlocked.Exchange(ref refreshing, 1) != 0)
            {
                ScheduleRefresh();
                return;
            }
            try
            {
                string workspaceRoot;
                long workspaceGeneration;
                lock (sync)
                {
                    if (disposed) return;
                    workspaceRoot = root;
                    workspaceGeneration = generation;
                }
                var configuration = string.IsNullOrEmpty(workspaceRoot)
                    ? null : OpenFolderConfiguration.Load(workspaceRoot);
                var executables = configuration?.FindExecutables() ?? new string[0];
                var newFingerprint = (configuration?.SettingsTimestamp.Ticks.ToString() ?? "") + "|" +
                    string.Join("|", executables.Select(path => path + ":" +
                        File.GetLastWriteTimeUtc(path).Ticks + ":" + new FileInfo(path).Length));
                lock (sync)
                {
                    if (disposed || workspaceGeneration != generation || fingerprint == newFingerprint) return;
                    fingerprint = newFingerprint;
                    revision++;
                    var architecture = string.Equals(configuration?.TargetPlatform, "x86", StringComparison.OrdinalIgnoreCase)
                        ? Architecture.X86 : Architecture.X64;
                    containers = executables.Select(path => (ITestContainer)new ExecutableTestContainer(
                        this, path, revision, architecture)).ToArray();
                    // The workspace watcher already covers normal build directories.
                    // Explicit external directories receive their own watcher.
                    DisposeWatchers();
                    if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) Watch(root);
                    foreach (var directory in configuration?.SearchDirectories ?? new string[0])
                    {
                        if (Directory.Exists(directory) && !IsUnderRoot(directory, root)) Watch(directory);
                    }
                }
                TestContainersUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is System.Xml.XmlException || error is ArgumentException)
            {
                Trace.TraceWarning("Catch2 Open Folder: " + error.Message);
                lock (sync)
                {
                    if (!disposed) { fingerprint = null; containers = new ITestContainer[0]; }
                }
                TestContainersUpdated?.Invoke(this, EventArgs.Empty);
            }
            finally { Interlocked.Exchange(ref refreshing, 0); }
        }
        private static bool IsUnderRoot(string directory, string workspaceRoot)
        {
            return string.Equals(directory, workspaceRoot, StringComparison.OrdinalIgnoreCase) ||
                directory.StartsWith(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
        private void DisposeWatchers()
        {
            foreach (var watcher in watchers) watcher.Dispose();
            watchers.Clear();
        }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                generation++;
                workspaceService.OnActiveWorkspaceChanged -= WorkspaceChangedAsync;
                DisposeWatchers();
                refreshTimer.Dispose();
                containers = new ITestContainer[0];
            }
        }
    }
}
