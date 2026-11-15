# Catch2 CodeLens experiment

This is an isolated two-test fixture for the experimental VS 2026 Open Folder
CodeLens integration. It uses the Catch2 2.13.10 header already in this repository.
The native debugger requires a Debug build with its PDB alongside the executable.

From this directory:

```powershell
cmake --preset codelens
cmake --build out --config Debug
```

Install the fork VSIX into an experimental VS profile, then open this directory
with **File > Open > Folder**. Open `tests.cpp`. If necessary, open Test Explorer
to initialize discovery. Above each TEST_CASE, select the status icon to access
**Run**, **Debug**, and **Show in Test Explorer**. Run each test separately.

Both tests pass by default. To demonstrate a failure and recovery without rebuilding:

```powershell
Set-Content fail.flag 'Intentional CodeLens failure'
# Run only "CodeLens toggles failure" from its source CodeLens: red status.
Rename-Item fail.flag fail.disabled
# Run the same test again: green status.
```

Set a breakpoint on `volatile int answer = 42`, then select **Debug** from the
first test's CodeLens. It should break inside that test; step over to inspect
`answer = 42`, then continue. See [the experiment report](../../Docs/CodeLens.md)
for verified behavior and remaining limitations.

To exercise the debug-result wait and parsing code without attaching VS:

```powershell
dotnet run --project DebugRunnerChecks.csproj --configuration Release -- "$PWD/bin/Debug/Catch2CodeLensTests.exe"
```

This runs seven checks using a process-launch delegate. Actual debugger attachment
and breakpoint behavior must still be validated in the IDE.
