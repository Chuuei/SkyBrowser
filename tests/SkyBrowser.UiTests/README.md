# Desktop accessibility and user interaction tests

This project uses [FlaUI.UIA3](https://github.com/FlaUI/FlaUI) 5.0.0 with xUnit.
It launches the actual `SkyBrowser.exe` and uses the Windows accessibility tree,
the same interface used by assistive technologies. Tests locate controls by
accessible **role and name**, then send real keyboard or mouse input. They do not
construct WPF windows, use `FindName`, raise events, or call application handlers.

The existing `SkyBrowser.Tests` WPF scenario remains useful for rendering,
cancellation and detailed application-state assertions. This suite complements it
with checks at the running application's user interface boundary.

## Run

Use an unlocked, interactive Windows desktop and the .NET 10 Desktop Runtime.
Avoid using the keyboard or mouse while the tests run. Do not run other desktop
tests at the same time; keyboard input and foreground focus are shared resources.
The project disables xUnit parallelization and runs after the WPF suite in CI.

```powershell
dotnet test tests/SkyBrowser.UiTests -c Release --artifacts-path ./artifacts
```

The project builds the desktop executable through a project reference without
referencing its assembly. By default it finds that executable in the sibling
`SkyBrowser.Desktop` artifacts folder for the current configuration. To test a
specific build, set `SKYBROWSER_UI_APP_PATH` to its absolute executable path.

Each test sets `SKYBROWSER_DATA_DIRECTORY` in the **child process only**, pointing
to a new profile under `artifacts/ui-tests`. Application startup resolves settings,
the library and default caches there. Restart checks reuse only that test's
profile. Existing settings, image folders and previews are never used. The
environment variable also allows a separate application profile outside tests;
when unset, the ordinary application uses its existing default data location.

## Coverage

- First-run stretch choice, keyboard activation, persistence and application restart.
- Escape deferral without saving, with setup shown again on the next launch.
- Options typing, saving, reopening and cancellation without persisted changes.
- Invalid-input disabled state and recovery using the keyboard.
- Arrow-key selection of every Options tab and Tab access to its settings.
- Nonempty accessible names, keyboard-focus eligibility and standard Invoke,
  Value, Toggle, Selection and ExpandCollapse patterns for exposed settings controls.
- Mouse opening/cancelling the library, named folder search/list and disabled removal.
- Readable sky/world-map values, F6 regions, F1 help and dialog focus restoration.
- Keyboard planning/chart samples and exact-coordinate sky measurements.
- Real UI Automation live-region events for validation errors.
- Persisted 200% interface size, high contrast and reduced motion with keyboard reachability.

The helper activates only the top-level window. `TabTo` traverses the real tab
order rather than forcing focus onto an element. Waits poll observable UI state
with bounded timeouts. Scrollbars and other composite controls are checked at
their public control boundary, rather than auditing their template parts as
standalone inputs.

Failures retain a process-scoped accessibility-tree dump and window screenshots
beside the isolated profile; the test output prints that directory. CI uploads
the diagnostics with TRX results. Captures stay under ignored `artifacts/` and are
not approved documentation screenshots.

These are focused accessibility regressions, not a complete accessibility audit.
Actual Narrator/NVDA speech, magnification, custom contrast themes, multiple DPI
settings and independent task completion still require manual assessment; see
[support and evidence](../../docs/accessibility.md).
FlaUI and its dependencies are confined to this test project and are not shipped
with the application.
