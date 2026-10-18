# Open Folder support

This fork adds a Visual Studio MEF `ITestContainerDiscoverer` which registers
Catch2 executable test containers while an `IVsFolderWorkspaceService` workspace
is open. It reuses the existing Catch2 adapter, XML source locations, execution,
and native debugger integration. No generated Visual Studio solution or CTest
backtrace patch is required.

The first package targets Visual Studio 2026 (18.x). VS 2022 has not been validated.
It retains the upstream VSIX identity so installing it replaces/upgrades the
original Catch2 adapter rather than loading two versions of the same executor.

## Configure a folder

Add this to the folder root `.runsettings`:

```xml
<RunSettings>
  <RunConfiguration>
    <TargetPlatform>x64</TargetPlatform>
    <CollectSourceInformation>true</CollectSourceInformation>
  </RunConfiguration>
  <Catch2Adapter>
    <FilenameFilter>^SurfVisual\.(Base|Gui|Presentation|Scene)\.UnitTest$</FilenameFilter>
    <DiscoverTimeout>10000</DiscoverTimeout>
    <OpenFolder>
      <Enabled>true</Enabled>
      <ActiveConfigurationOnly>true</ActiveConfigurationOnly>
    </OpenFolder>
  </Catch2Adapter>
</RunSettings>
```

Open the folder and build its tests. With `ActiveConfigurationOnly=true`, VS's
active build directory is the default scan root and no `SearchDirectory` is
required. Only matching `.exe` files under that directory are registered.
Optional `SearchDirectory` entries narrow discovery to the intersection of the
configured patterns and the active build directory. To discover all configured
directories in a plain folder, omit `ActiveConfigurationOnly` and supply explicit
search paths instead. Missing/invalid settings do not enable discovery. Changing
configuration or rebuilding executables refreshes containers; closing the folder
clears them. Directory junctions are not traversed.

Relative directories resolve against the folder root. Existing absolute external
directories are also supported. For external outputs, select this `.runsettings`
explicitly in Test > Configure Run Settings; the adapter's fallback can only find
settings in the executable's ancestor directories. Explicit VS settings take
priority over the root file fallback.

## CTest duplicates

When OpenFolder is enabled, matching executables are claimed through Visual
Studio's External Build Framework subordinate discoverer contract. Their tests
are routed to the Catch2 adapter instead of also appearing as generic CTest tests.
No Test Explorer search filter is required. Unrelated CTest tests remain visible.
CMake's generated CTest files and command-line/CI behavior are unchanged.

The routing contract is supplied by the installed VS host and is not bundled in
the VSIX. This integration currently targets VS 2026; later host updates require
validation. After enabling/disabling OpenFolder in an already-open workspace,
reopen the folder to refresh the CMake provider's ownership.
Invalid or disabled configuration leaves ownership with CTest.

Catch2 tests also carry the `Adapter=Catch2` trait, which remains available as an
optional `Trait:Catch2` search filter in mixed-framework workspaces.

## Build

Build on Windows with Visual Studio 2026's MSBuild. Set `VsInstallRoot` to the
installation directory so the MEF project references that instance's workspace
and Test Window contracts without bundling VS host assemblies:

```powershell
& "$env:VSINSTALLDIR/MSBuild/Current/Bin/amd64/MSBuild.exe" `
  VSIX/VSTestAdapterCatch2/VSTestAdapterCatch2.csproj -restore `
  /p:Configuration=Release "/p:VsInstallRoot=$env:VSINSTALLDIR" `
  /p:DeployExtension=false
```

The installable artifact is
`VSIX/VSTestAdapterCatch2/bin/Release/VSTestAdapterCatch2.vsix`.
Configuration/scanner regression tests are in `UnitTests/UT_OpenFolder`.
Use an experimental VS configuration for the first IDE test.

References:

- [Upstream CMake limitation](https://github.com/JohnnyHendriks/TestAdapter_Catch2/issues/3).
- [Microsoft's Node.js workspace test discoverer](https://github.com/microsoft/nodejstools/blob/main/Nodejs/Product/Nodejs/Workspace/PackageJsonTestContainerDiscoverer.cs).

## Verified behavior

Validated on Visual Studio Community 2026 (18.x) in the isolated `Catch2Folder`
experimental configuration:

- Opened a CMake folder using a Ninja preset, without a generated solution.
- Discovered and passed 19 tests from the four real SurfVisual Catch2 executables.
- Double-clicking Catch2 tests opened their original source files at the test line.
- A separate built-in CTest smoke test remained available; all 20 tests passed.
- `Trait:Catch2` displayed 19/20 tests, excluding the CTest test.
- Changing the root filename filter to Base only automatically reduced discovery
  to one Catch2 test; restoring the filter restored all 19.
- All 12 configuration/scanner regression tests passed.

The IDE smoke folder used copies of the built SurfVisual executables and DLLs.
The daily Visual Studio profile retained the upstream 2.0.0 extension; only the
experimental profile was upgraded to this fork's 2.1.0 package.

## Experimental installation

Do not double-click the VSIX when targeting an experimental configuration.
Create the configuration using the installed SDK's `CreateExpInstance.exe`, then
invoke that VS installation's `VSIXInstaller.exe` with all arguments, including
the quoted package path. For example, adapt the instance ID to your installation:

```powershell
& "C:/Program Files/Microsoft Visual Studio/18/Community/Common7/IDE/VSIXInstaller.exe" `
  /quiet /instanceIds:909075e9 /rootSuffix:Catch2Folder `
  /logFile:Catch2FolderInstall.log `
  "D:/AgentWorkspace/TestAdapter_Catch2/VSIX/VSTestAdapterCatch2/bin/Release/VSTestAdapterCatch2.vsix"
```

An installer Usage dialog means the arguments were not accepted; it does not
confirm installation. Check the installation log in the user's temporary
directory for the completed installation and target extension path.

### Version 2.1.1: CTest ownership

The VS 2026 IDE smoke workspace additionally registered two Catch2 executables
through CTest, alongside an unrelated CTest test. With OpenFolder enabled and no
Test Explorer filter, only 19 native Catch2 tests plus the unrelated CTest test
were displayed. Both CTest duplicates were excluded. All 20 IDE tests passed;
all three original CTest registrations still passed from the command line.

When upgrading the experimental configuration from an older package, run
`devenv.exe /RootSuffix Catch2Folder /UpdateConfiguration` with the experimental
instance closed if VS retains the previous extension location. A stale MEF or
extension index can otherwise reference a directory removed by VSIX Installer.

Disabling OpenFolder and building restored the three CTest entries. Re-enabling
OpenFolder in the same session requires reopening the folder; building alone
did not refresh the CMake provider's ownership in this validation.

### Version 2.1.2: directory patterns and the active preset

SearchDirectory supports `*` (zero or more characters in one directory segment),
`?` (one character), and an entire `**` segment (zero or more directory levels).
Relative patterns resolve against the workspace root. Absolute patterns and both
Windows path separators are accepted. Each matched directory is scanned
recursively for configured executables; overlapping matches are deduplicated.
Reparse points are not traversed. An unmatched pattern remains valid and can
match a directory created by a later build.

Set `ActiveConfigurationOnly` to `true` to intersect matched paths with the current
VS configuration's `GenOutputDirectory`, obtained from the workspace configuration
service. Configuration change notifications refresh containers. Until VS supplies
an active build directory, no Catch2 containers are registered or claimed from
CTest. This option defaults to `false` for compatibility with existing configs
and non-CMake folders. It controls IDE discovery, not explicit CLI source lists.

SearchDirectory can be omitted entirely in active-configuration mode. In that
case the test host fallback validates the executable's ancestor CMakeCache.txt
against this workspace's CMAKE_HOME_DIRECTORY before reusing settings. Explicit
patterns that match nothing never fall back to scanning the entire active build.

The filter distinguishes preset build directories. Multiple configurations from
the same multi-configuration generator share a build directory; limit the search
pattern to the intended configuration subdirectory if necessary.

The package builds successfully and all 25 scanner/configuration regression tests
pass, including pattern matching, missing/future directories, scope changes,
deduplication and fallback settings. Version 2.1.2 has not been installed in the
user's VS profile; preset switching in the IDE still needs runtime validation.