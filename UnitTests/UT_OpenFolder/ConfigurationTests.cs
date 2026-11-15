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
        public void CodeLensRequiresExplicitOptIn()
        {
            WriteSettings();
            Assert.IsFalse(OpenFolderConfiguration.Load(root).CodeLensEnabled);
            foreach (var value in new[] { "false", "invalid" })
            {
                WriteSettings(directories: "<SearchDirectory>bin</SearchDirectory><CodeLens>" + value + "</CodeLens>");
                Assert.IsFalse(OpenFolderConfiguration.Load(root).CodeLensEnabled);
            }
        }

        [TestMethod]
        public void CodeLensCanBeEnabledWithoutChangingContainerScope()
        {
            var expected = AddFile("bin/SurfVisual.Base.UnitTest.exe");
            AddFile("other/SurfVisual.Scene.UnitTest.exe");
            WriteSettings(directories: "<SearchDirectory>bin</SearchDirectory><CodeLens>true</CodeLens>");
            var configuration = OpenFolderConfiguration.Load(root);
            Assert.IsTrue(configuration.CodeLensEnabled);
            CollectionAssert.AreEqual(new[] { expected }, configuration.FindExecutables().ToArray());
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
        public void StarMatchesOnlyOnePresetDirectorySegment()
        {
            WriteSettings(directories: "<SearchDirectory>out/build/*/bin</SearchDirectory>");
            var first = AddFile("out/build/debug/bin/SurfVisual.Base.UnitTest.exe");
            var second = AddFile("out/build/release/bin/SurfVisual.Scene.UnitTest.exe");
            AddFile("out/build/nested/preset/bin/SurfVisual.Gui.UnitTest.exe");
            AddFile("out/build/debug/bin/NotCatch2.exe");
            CollectionAssert.AreEqual(new[] { first, second }, OpenFolderConfiguration.Load(root).FindExecutables().ToArray());
        }
        [TestMethod]
        public void QuestionMarkMatchesExactlyOneCharacter()
        {
            WriteSettings(directories: "<SearchDirectory>build-?/bin</SearchDirectory>");
            var expected = AddFile("build-a/bin/SurfVisual.Base.UnitTest.exe");
            AddFile("build-ab/bin/SurfVisual.Scene.UnitTest.exe");
            AddFile("build-/bin/SurfVisual.Gui.UnitTest.exe");
            CollectionAssert.AreEqual(new[] { expected }, OpenFolderConfiguration.Load(root).FindExecutables().ToArray());
        }
        [TestMethod]
        public void DoubleStarMatchesZeroOrSeveralDirectoryLevels()
        {
            WriteSettings(directories: "<SearchDirectory>build/**/bin</SearchDirectory>");
            var first = AddFile("build/bin/SurfVisual.Base.UnitTest.exe");
            var second = AddFile("build/one/two/bin/SurfVisual.Scene.UnitTest.exe");
            CollectionAssert.AreEquivalent(new[] { first, second }, OpenFolderConfiguration.Load(root).FindExecutables().ToArray());
        }
        [TestMethod]
        public void AbsolutePatternsAndWindowsSeparatorsAreSupported()
        {
            WriteSettings(directories: "<SearchDirectory>" + Path.Combine(root, @"build\*\bin") + "</SearchDirectory>");
            var expected = AddFile("build/debug/bin/SurfVisual.Base.UnitTest.exe");
            CollectionAssert.AreEqual(new[] { expected }, OpenFolderConfiguration.Load(root).FindExecutables().ToArray());
        }
        [TestMethod]
        public void PatternWithNoMatchesCanDiscoverAFutureBuild()
        {
            WriteSettings(directories: "<SearchDirectory>out/build/*/bin</SearchDirectory>");
            var configuration = OpenFolderConfiguration.Load(root);
            Assert.IsNotNull(configuration);
            Assert.AreEqual(0, configuration.FindExecutables().Count);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(root, "out/build")),
                configuration.WatchDirectories.Single().TrimEnd(Path.DirectorySeparatorChar));
            AddFile("out/build/new-preset/bin/SurfVisual.Base.UnitTest.exe");
            Assert.AreEqual(1, OpenFolderConfiguration.Load(root).FindExecutables().Count);
        }
        [TestMethod]
        public void OverlappingGlobAndLiteralDirectoriesDoNotDuplicateExecutables()
        {
            WriteSettings(directories: "<SearchDirectory>build/*/bin</SearchDirectory><SearchDirectory>build/debug/bin</SearchDirectory>");
            var expected = AddFile("build/debug/bin/SurfVisual.Base.UnitTest.exe");
            CollectionAssert.AreEqual(new[] { expected }, OpenFolderConfiguration.Load(root).FindExecutables().ToArray());
        }
        [TestMethod]
        public void ActiveBuildScopeExcludesOtherPresetsAndSiblingPrefixes()
        {
            WriteSettings(directories: "<SearchDirectory>out/build/*/bin</SearchDirectory>");
            var debug = AddFile("out/build/debug/bin/SurfVisual.Base.UnitTest.exe");
            var release = AddFile("out/build/release/bin/SurfVisual.Scene.UnitTest.exe");
            AddFile("out/build/debug-old/bin/SurfVisual.Gui.UnitTest.exe");
            var configuration = OpenFolderConfiguration.Load(root);
            CollectionAssert.AreEqual(new[] { debug }, configuration.FindExecutables(new[] { Path.Combine(root, "out/build/debug") }).ToArray());
            CollectionAssert.AreEqual(new[] { release }, configuration.FindExecutables(new[] { Path.Combine(root, "out/build/release") }).ToArray());
            Assert.AreEqual(0, configuration.FindExecutables(new string[0]).Count);
        }
        [TestMethod]
        public void ActiveBuildScopeCanBeInsideABroaderSearchDirectory()
        {
            WriteSettings(directories: "<SearchDirectory>out/build</SearchDirectory>");
            var expected = AddFile("out/build/debug/bin/SurfVisual.Base.UnitTest.exe");
            AddFile("out/build/release/bin/SurfVisual.Scene.UnitTest.exe");
            CollectionAssert.AreEqual(new[] { expected }, OpenFolderConfiguration.Load(root)
                .FindExecutables(new[] { Path.Combine(root, "out/build/debug") }).ToArray());
        }
        [TestMethod]
        public void ActiveConfigurationOnlyIsExplicitAndInvalidValuesDisableDiscovery()
        {
            WriteSettings(directories: "<SearchDirectory>build/*/bin</SearchDirectory><ActiveConfigurationOnly>true</ActiveConfigurationOnly>");
            Assert.IsTrue(OpenFolderConfiguration.Load(root).ActiveConfigurationOnly);
            WriteSettings(directories: "<SearchDirectory>build/*/bin</SearchDirectory><ActiveConfigurationOnly>invalid</ActiveConfigurationOnly>");
            Assert.IsNull(OpenFolderConfiguration.Load(root));
        }
        [TestMethod]
        public void FallbackSettingsCanResolveWildcardDirectories()
        {
            WriteSettings(directories: "<SearchDirectory>out/build/*/bin</SearchDirectory>");
            var source = AddFile("out/build/debug/bin/SurfVisual.Base.UnitTest.exe");
            Assert.IsNotNull(OpenFolderConfiguration.FindSettings(new[] { source }));
        }
        [TestMethod]
        public void ActiveConfigurationCanSupplyTheSearchDirectory()
        {
            WriteSettings(directories: "<ActiveConfigurationOnly>true</ActiveConfigurationOnly>");
            var expected = AddFile("out/build/debug/bin/SurfVisual.Base.UnitTest.exe");
            AddFile("out/build/release/bin/SurfVisual.Scene.UnitTest.exe");
            var configuration = OpenFolderConfiguration.Load(root);
            Assert.IsNotNull(configuration);
            Assert.IsFalse(configuration.HasSearchPatterns);
            Assert.AreEqual(0, configuration.FindExecutables().Count);
            CollectionAssert.AreEqual(new[] { expected }, configuration
                .FindExecutables(new[] { Path.Combine(root, "out/build/debug") }).ToArray());
        }
        [TestMethod]
        public void UnmatchedExplicitPatternDoesNotFallBackToTheEntireActiveBuild()
        {
            WriteSettings(directories: "<ActiveConfigurationOnly>true</ActiveConfigurationOnly><SearchDirectory>missing/*/bin</SearchDirectory>");
            AddFile("out/build/debug/bin/SurfVisual.Base.UnitTest.exe");
            var configuration = OpenFolderConfiguration.Load(root);
            Assert.IsTrue(configuration.HasSearchPatterns);
            Assert.AreEqual(0, configuration.FindExecutables(new[] { Path.Combine(root, "out/build/debug") }).Count);
        }
        [TestMethod]
        public void AutomaticDirectoryFallbackRequiresThisWorkspacesCMakeCache()
        {
            WriteSettings(directories: "<ActiveConfigurationOnly>true</ActiveConfigurationOnly>");
            var source = AddFile("out/build/debug/bin/SurfVisual.Base.UnitTest.exe");
            Assert.IsNull(OpenFolderConfiguration.FindSettings(new[] { source }));
            File.WriteAllText(Path.Combine(root, "out/build/debug/CMakeCache.txt"),
                "CMAKE_HOME_DIRECTORY:INTERNAL=" + root);
            Assert.IsNotNull(OpenFolderConfiguration.FindSettings(new[] { source }));
            File.WriteAllText(Path.Combine(root, "out/build/debug/CMakeCache.txt"),
                "CMAKE_HOME_DIRECTORY:INTERNAL=" + Path.Combine(root, "another-workspace"));
            Assert.IsNull(OpenFolderConfiguration.FindSettings(new[] { source }));
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
