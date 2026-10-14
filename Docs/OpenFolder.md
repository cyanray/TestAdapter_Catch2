# Open Folder support (cyanray fork)

This fork adds a Visual Studio MEF `ITestContainerDiscoverer` which registers
Catch2 executable test containers while an `IVsFolderWorkspaceService` workspace
is open. It reuses the existing Catch2 adapter, XML source locations, execution,
and native debugger integration. No generated Visual Studio solution or CTest
backtrace patch is required.

The first package targets Visual Studio 2026 (18.x). VS 2022 has not been validated.
It retains the upstream VSIX identity so installing it replaces/upgrades the
original Catch2 adapter rather than loading two versions of the same executor.

## Configure a folder

Add this to the folder root `.runsettings`, adapting the search directory:

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
      <SearchDirectory>out/build/x64-release-custom/bin</SearchDirectory>
    </OpenFolder>
  </Catch2Adapter>
</RunSettings>
```

Open the folder and build its tests. Only matching `.exe` files in the specified
search directories (and their descendants) are registered. Additional
`SearchDirectory` entries are allowed; specify the active build rather than
several presets containing the same tests. Disabled/missing/invalid settings do
not enable discovery. Changing the configuration or rebuilding executables
refreshes containers; closing the folder clears them. Directory junctions are
not traversed.

Relative directories resolve against the folder root. Existing absolute external
directories are also supported. For external outputs, select this `.runsettings`
explicitly in Test > Configure Run Settings; the adapter's fallback can only find
settings in the executable's ancestor directories. Explicit VS settings take
priority over the root file fallback.

## CTest duplicates

The built-in CTest integration is independent. A CMake project that registers
individual Catch2 tests may also show those tests through CTest. This fork does
not modify CMake/CTest registration or suppress another provider. Catch2 tests
carry the `Adapter=Catch2` trait. Enter `Trait:Catch2` in Test Explorer's search
box to display only these tests, then use Run All In View. This hides CTest
duplicates in the view without disabling CTest for command-line/CI runs.

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