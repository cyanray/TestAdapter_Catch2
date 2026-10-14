using System;
using System.IO;
using System.Linq;
using System.Xml;
using Catch2Interface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UT_OpenFolder
{
    [TestClass]
    public class ConfigurationTests
    {
        private string root;
        private readonly string temporaryBase = Path.Combine(Path.GetTempPath(), "Catch2OpenFolderTests");
        [TestInitialize]
        public void Initialize()
        {
            root = Path.Combine(temporaryBase, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
        }
        [TestCleanup]
        public void Cleanup()
        {
            var fullRoot = Path.GetFullPath(root);
            Assert.IsTrue(fullRoot.StartsWith(Path.GetFullPath(temporaryBase) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
            Directory.Delete(fullRoot, true);
        }
        private void WriteSettings(string enabled = "true", string filter = @"^SurfVisual\..+\.UnitTest$",
            string directories = "<SearchDirectory>bin</SearchDirectory>", string disabled = "false")
        {
            File.WriteAllText(Path.Combine(root, ".runsettings"),
                "<RunSettings><RunConfiguration><TargetPlatform>x64</TargetPlatform></RunConfiguration>" +
                "<Catch2Adapter disabled=\"" + disabled + "\"><FilenameFilter>" + filter + "</FilenameFilter>" +
                "<OpenFolder><Enabled>" + enabled + "</Enabled>" + directories + "</OpenFolder></Catch2Adapter></RunSettings>");
        }
        private string AddFile(string relative)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "not an executable; scanner must not launch files");
            return Path.GetFullPath(path);
        }
        [TestMethod]
        public void MissingConfigurationDoesNotEnableDiscovery()
        {
            AddFile("bin/SurfVisual.Base.UnitTest.exe");
            Assert.IsNull(OpenFolderConfiguration.Load(root));
        }
        [TestMethod]
        public void OpenFolderRequiresExplicitOptIn()
        {
            WriteSettings(enabled: "false");
            Assert.IsNull(OpenFolderConfiguration.Load(root));
        }
        [TestMethod]
        public void DisabledAdapterOverridesOpenFolderOptIn()
        {
            WriteSettings(disabled: "true");
            Assert.IsNull(OpenFolderConfiguration.Load(root));
        }
        [TestMethod]
        public void InvalidFilenameFilterDoesNotEnableDiscovery()
        {
            WriteSettings(filter: "[");
            Assert.IsNull(OpenFolderConfiguration.Load(root));
        }
        [TestMethod]
        public void ExplicitSearchDirectoryIsRequired()
        {
            WriteSettings(directories: "");
            Assert.IsNull(OpenFolderConfiguration.Load(root));
        }
        [TestMethod]
        public void MatchesOnlyConfiguredDirectoriesAndTestFilenames()
        {
            WriteSettings();
            var expected = AddFile("bin/SurfVisual.Base.UnitTest.exe");
            AddFile("bin/SurfVisual.Demo.exe");
            AddFile("another-preset/bin/SurfVisual.Base.UnitTest.exe");
            CollectionAssert.AreEqual(new[] { expected }, OpenFolderConfiguration.Load(root).FindExecutables().ToArray());
        }
        [TestMethod]
        public void FindsNestedBuildOutputsWithoutDuplicatingOverlappingDirectories()
        {
            WriteSettings(directories: "<SearchDirectory>bin</SearchDirectory><SearchDirectory>bin/Release</SearchDirectory>");
            var expected = AddFile("bin/Release/SurfVisual.Scene.UnitTest.exe");
            CollectionAssert.AreEqual(new[] { expected }, OpenFolderConfiguration.Load(root).FindExecutables().ToArray());
        }
        [TestMethod]
        public void MissingBuildDirectoryIsAllowedUntilFirstBuild()
        {
            WriteSettings(directories: "<SearchDirectory>future-build</SearchDirectory>");
            Assert.AreEqual(0, OpenFolderConfiguration.Load(root).FindExecutables().Count);
            AddFile("future-build/SurfVisual.Gui.UnitTest.exe");
            Assert.AreEqual(1, OpenFolderConfiguration.Load(root).FindExecutables().Count);
        }
        [TestMethod]
        public void HostWithoutRunsettingsCanReadOptedInWorkspaceSettings()
        {
            WriteSettings();
            var source = AddFile("bin/SurfVisual.Base.UnitTest.exe");
            var settings = OpenFolderConfiguration.FindSettings(new[] { source });
            Assert.IsNotNull(settings);
            Assert.IsTrue(settings.General.FilenameFilter.IsMatch("SurfVisual.Base.UnitTest"));
        }
        [TestMethod]
        public void FallbackDoesNotUseSettingsForAnotherBuildDirectory()
        {
            WriteSettings();
            var source = AddFile("unconfigured/SurfVisual.Base.UnitTest.exe");
            Assert.IsNull(OpenFolderConfiguration.FindSettings(new[] { source }));
        }
        [TestMethod]
        public void SettingsChangesAndBinaryRemovalAreObservedOnRescan()
        {
            WriteSettings();
            var source = AddFile("bin/SurfVisual.Base.UnitTest.exe");
            Assert.AreEqual(1, OpenFolderConfiguration.Load(root).FindExecutables().Count);
            WriteSettings(filter: "^NoSuchProgram$");
            Assert.AreEqual(0, OpenFolderConfiguration.Load(root).FindExecutables().Count);
            WriteSettings();
            File.Delete(source);
            Assert.AreEqual(0, OpenFolderConfiguration.Load(root).FindExecutables().Count);
        }
        [TestMethod]
        [ExpectedException(typeof(XmlException))]
        public void RejectsExternalXmlEntities()
        {
            File.WriteAllText(Path.Combine(root, ".runsettings"),
                "<!DOCTYPE x [<!ENTITY remote SYSTEM 'file:///nonexistent'>]><RunSettings>&remote;</RunSettings>");
            OpenFolderConfiguration.Load(root);
        }
    }
}
