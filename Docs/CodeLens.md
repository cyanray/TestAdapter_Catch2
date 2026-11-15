# Catch2 source CodeLens experiment (2.1.6)

The experiment adds a native Visual Studio test status indicator above discovered
Catch2 test declarations in Open Folder / CMake workspaces. Select the indicator
to run or debug that test, view its last result and duration, inspect a failure,
or locate it in Test Explorer. The feature is opt-in.

## Enable

Install the fork VSIX in an experimental Visual Studio 2026 profile. In the
workspace root `.runsettings`, add `CodeLens` alongside the existing OpenFolder
settings:

```xml
<OpenFolder>
  <Enabled>true</Enabled>
  <ActiveConfigurationOnly>true</ActiveConfigurationOnly>
  <CodeLens>true</CodeLens>
</OpenFolder>
```

Keep the existing Catch2 FilenameFilter and discovery settings. Explicit
SearchDirectory entries can also be used, as in the standalone fixture.
Build the test executable, open Test Explorer if discovery has not started,
and open its original C++ source. CodeLens must be enabled in VS editor options.
Source paths and line numbers reported by Catch2 must match the open source file.
Use a Debug build with matching PDBs for native breakpoints.

## Implementation

`Catch2CodeLensTagger` implements the public editor ICodeLensTag3 and descriptor
context interfaces. For each discovered source location it supplies Function,
OutputFilePath and FullyQualifiedName to VS's existing TestStatus CodeLens provider.
That provider queries Test Explorer results and owns the Run and Debug commands.
No second results database, custom runner, or alternate debugger is introduced.
The descriptor context convention is coupled to this VS host and remains experimental.

An additional Catch2 discovery pass runs on the workspace refresh worker only
when CodeLens is opted in. It uses the same configured executables and active
build scope as the test containers. There is no executable launch on the editor
thread. Tags follow buffer edits with tracking spans, refresh on rediscovery,
and are disposed when the view closes. Rebuild after changing test declarations.

The executable adapter now waits for a debugger-launched process to exit and
parses its XML report. Previously it immediately recorded None and discarded
all debug results. Waiting at a breakpoint does not trigger normal run timeouts.
Cancelled or incomplete combined debug runs do not automatically re-launch tests.
The DLL executor's original debug behavior is outside this experiment.

## Verified on 2026-10-18

Host: Visual Studio Community 2026, 18.10.12210.168, isolated Catch2Folder profile.
The user's daily VS profile was not upgraded.

Two Catch2 2.13.10 TEST_CASE declarations were built with MSVC x64 Debug in a
separate CMake Open Folder workspace. The repeatable source and settings are in
[Experiments/CodeLens](../Experiments/CodeLens/README.md).

- Both tests displayed native TestStatus icons above their source declarations.
- Source CodeLens Run selected exactly one test. The other test stayed Not Run.
- A passing run changed the source indicator to green.
- A deliberate failure changed it to red and exposed the failed assertion and line.
- Removing the failure condition and running the same test changed red to green.
- CodeLens Debug launched only the selected Catch2 test and hit a native breakpoint.
  Stepping over showed `answer = 42` in Locals; Call Stack showed the Catch2 test.
- The debug input file contained only `"CodeLens passes"`.
- 27 configuration/scanner regression tests passed.
- Seven process-launch/report checks passed for single and combined pass/fail,
  exact selection, no automatic rerun, pre-cancellation, and report cleanup.

With the updated executable adapter installed, the breakpoint debug run stayed
active while paused and finished as exactly one Passed test after Continue.
A second CodeLens Debug run of the deliberately failing test finished as exactly
one Failed test. Source icons and Test Explorer agreed in both cases.

## Scope

Validated for local Windows executables and distinct source locations. Template
instantiations sharing a source line, the same source compiled into several
executables, remote workspaces, DLL tests, and VS 2022 have not been validated.
The experiment adds a discovery pass per executable/settings refresh; large
workspaces should be measured before enabling it broadly.

## References

- [Microsoft: C++ unit testing and CodeLens](https://learn.microsoft.com/en-us/visualstudio/test/writing-unit-tests-for-c-cpp?view=visualstudio)
- [Microsoft: CodeLens extension interfaces](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/editor/walkthroughs/codelens?view=visualstudio)

## Multiline test declarations (2.1.6)

Catch2 can report the closing line of a multiline macro (confirmed with the
bundled 2.13.10 fixture). The source tagger now locates the containing test macro
and anchors its indicator above the opening line. It ignores comments, string
and character literals, raw strings, and nested parentheses. Unknown wrapper
macros keep the reported location rather than guessing an earlier declaration.
The tracking span covers only the anchor character, so wrapping the argument
list after discovery does not stretch the tag across lines.

The reproducible fixture's first TEST_CASE deliberately puts its tags on a
second line. All 52 configuration/scanner/declaration regression cases pass.
