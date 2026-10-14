using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.TestWindow.Extensibility.Model;

namespace Catch2OpenFolder
{
    internal sealed class ExecutableTestContainer : ITestContainer
    {
        private readonly long version;
        private readonly Architecture architecture;
        public ExecutableTestContainer(ITestContainerDiscoverer discoverer, string source,
            long version, Architecture architecture)
        {
            Discoverer = discoverer;
            Source = source;
            this.version = version;
            this.architecture = architecture;
        }
        public ITestContainerDiscoverer Discoverer { get; }
        public string Source { get; }
        public IEnumerable<Guid> DebugEngines => new[] { new Guid("3B476D35-A401-11D2-AAD4-00C04F990171") };
        public FrameworkVersion TargetFramework => FrameworkVersion.None;
        public Architecture TargetPlatform => architecture;
        public bool IsAppContainerTestContainer => false;
        public IDeploymentData DeployAppContainer() => null;
        public ITestContainer Snapshot() => new ExecutableTestContainer(Discoverer, Source, version, architecture);
        public int CompareTo(ITestContainer other)
        {
            var container = other as ExecutableTestContainer;
            if (container == null) return -1;
            var sourceComparison = StringComparer.OrdinalIgnoreCase.Compare(Source, container.Source);
            return sourceComparison != 0 ? sourceComparison : version.CompareTo(container.version);
        }
    }
}
