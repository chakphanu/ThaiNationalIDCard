# release/2.x maintenance policy

This branch (`release/2.x`) is the maintenance line of the 2.x library. The `master` branch README still says that
the original repository is not maintained; that wording is unchanged. This page only describes what happens on
the `release/2.x` branch.

## Policy

- Bug fixes only. No new features.
- Source and behavior compatible with 2.0.0: no renames, no API changes, no removed members, same return types,
  same property names and same decoded string content.
- .NET Framework only. The library targets net40, net45, net452, net46 and net461 (it uses WPF and System.Drawing).
- Dependencies: PCSC 5.0.0 and PCSC.Iso7816 5.0.0 as NuGet package references.
- A larger rewrite (netstandard, new API, Dispose pattern) is out of scope for this line.

## 2.0.1

Bug-fix release, see `CHANGELOG.md` for the list. Build system notes:

- SDK-style multi-target project (`ThaiNationalIDCard/ThaiNationalIDCard.csproj`). The old `release-40/45/...`
  configurations only changed the output path and no longer exist; every target is built by `Release`.
- The NuGet package now contains `lib/net40`, `lib/net45`, `lib/net452`, `lib/net46` and `lib/net461`, and depends on
  PCSC 5.0.0. The 2.0.0 package only had `lib/net40` and bundled an old `pcsc-sharp.dll`.
- AssemblyVersion stays `2.0.0.0` (binding redirects keep working); the package and file version are 2.0.1.
  `ThaiIDCard.Version()` reports the assembly version.

## Build, test, pack

```
dotnet build ThaiNationalIDCard.sln -c Release
dotnet test tests/ThaiNationalIDCard.Tests/ThaiNationalIDCard.Tests.csproj -c Release
dotnet pack ThaiNationalIDCard/ThaiNationalIDCard.csproj -c Release -o artifacts
```

On Linux or macOS set `EnableWindowsTargeting=true` and build the library only; the .NET Framework targets compile
against `Microsoft.NETFramework.ReferenceAssemblies` but cannot be executed there.

## Tests

`tests/ThaiNationalIDCard.Tests` (xUnit, net8.0) loads the net461 build of the library on modern .NET and drives it
with a fake PC/SC reader through an internal constructor (`InternalsVisibleTo`). Only synthetic card data is used.
The tests never touch real hardware. Behavior on a real card is not covered; check a real reader by hand before a release.

## CI

`.github/workflows/ci.yml` builds, tests and packs on `windows-latest` and cross-checks the library build on
`ubuntu-latest`. It does not publish anything. Publishing a package is a manual step.

## Release checklist

1. Update `CHANGELOG.md` and the `Version` in `ThaiNationalIDCard/ThaiNationalIDCard.csproj`.
2. CI is green on `release/2.x`.
3. Smoke test with a real reader and card (read, read with photo, card inserted/removed events).
4. Download the `nupkg` artifact, inspect it (`lib/net40` ... `lib/net461`, dependency PCSC 5.0.0) and publish by hand.
