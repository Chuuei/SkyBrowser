# Contributing to SkyBrowser

Bug reports, documentation improvements and focused pull requests are welcome.
For a substantial feature, discuss the intended behavior in an issue first.

## Build and test

Use Windows, PowerShell 7 and a .NET 10 SDK with C# 14 support. Rider and Visual
Studio are optional. Follow [development setup](docs/development.md) to initialize
the pinned XISFLib source submodule without downloading large image fixtures.

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path .\artifacts
dotnet test tests\SkyBrowser.Tests -c Release --artifacts-path .\artifacts
```

Normal tests generate their own small scientific fixtures. Large external-image
checks are optional. Desktop tests use real WPF windows and require Windows.
Never test against a person's real settings or preview database.

## Changes and pull requests

- Follow `.editorconfig` and the scientific/ownership contracts in `AGENTS.md`.
- Keep Core independent of WPF. Preserve source samples, metadata, alpha,
  coordinate conventions, cancellation and stale-callback protection.
- Add a focused regression for an observable behavior change. Keep changes small
  enough to review; explain the problem, resulting behavior and validation.
- Update the user manual when user-visible behavior changes. Follow
  [manual maintenance](docs/manual/README.md) for anonymous screenshots and PDF QA.
- Keep images, sessions, settings, logs, build output and caches out of commits.
  Sanitize reports before sharing them. See the [security policy](SECURITY.md).
- Update a submodule deliberately and record why its new revision is needed.
  Commit and publish the dependency change before updating the parent gitlink.

## Licensing and conduct

Contributions are provided under Apache-2.0, the same license as the project.
Retain upstream copyright/license notices and identify adapted code. New
dependencies need a license and redistribution review, including native/transitive
components; update [licensing](docs/licensing.md) and third-party notices.
The bundled OpenNGC-derived catalogue remains CC-BY-SA-4.0 independently of code.
See the [code of conduct](CODE_OF_CONDUCT.md).
