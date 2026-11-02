# release/2.x maintenance policy

This branch (`release/2.x`) is the compatibility line of the 2.x library. v3 is developed separately. This page only
describes what happens on the `release/2.x` branch.

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
  PCSC 5.0.0. The 2.0.0 package only had `lib/net40`, depended on PCSC 3.7.0 and bundled an old `pcsc-sharp.dll`.
- AssemblyVersion stays `2.0.0.0` (binding redirects keep working); the package and file version are 2.0.1.
  `ThaiIDCard.Version()` reports the assembly version.

## Questions from the issue tracker

**Does it work on .NET Framework 3.5? (#6)** No. PCSC 5.0.0 starts at net40, so that is the lowest target of 2.x.

**Why do I get "'TIS-620' is not a supported encoding name"? (#7)** That error appears on .NET Core. The 2.x line
targets .NET Framework only. Use v3 for modern .NET.

**Can I read the religion or the request number under the photo? (#12)** Not with this library. In #12 the maintainer
answered that the card does not store religion, then added that some card version might, which would need a sample
card and a scan of every address. The location of the request number is unknown.

**Why is my photo corrupt when I concatenate the 20 chunks? (#15)** Each APDU response ends in 2 status bytes (`90 00`
on success). They are not image data, so remove them if you concatenate responses yourself. Or use
`Personal.PhotoRaw` from `readAllPhoto()`, which already holds only the image bytes.

**How do I tell which reader raised an event? (#18)** The 2.x events do not carry the reader name. Use one
`ThaiIDCard` instance per reader, keep the instances referenced, and capture the name in the handler. Reporting the
reader in the events is planned for v3.

```csharp
var cards = new List<ThaiIDCard>();
foreach (string name in new ThaiIDCard().GetReaders())
{
    string reader = name;
    var card = new ThaiIDCard();
    card.eventCardInserted += personal => OnInserted(reader, personal);
    card.eventCardRemoved += () => OnRemoved(reader);
    card.MonitorStart(reader);
    cards.Add(card);
}
```

**Which NuGet packages do I need? (#20)** From 2.0.1 install only `ThaiNationalIDCard`. NuGet installs PCSC 5.0.0 and
PCSC.Iso7816 5.0.0 as dependencies.

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
