# ThaiNationalIDCard

A .NET library that reads a Thai national ID card through PC/SC, and lets the caller choose how much to read.

## Status

**3.0.0-preview. Not tested on a real card.** There is no reader or card behind this project yet. The read sequence, the
field layout and the error handling come from public code and from reasoning about it. The tests run against a simulator,
so they show the code matches those assumptions and not that the assumptions are right. Versions stay at
`3.0.0-preview.N` until real cards have confirmed them, and this README makes no claim about which cards or readers work.
You can help: see [Diagnostics](#diagnostics).

| Line | Branch | For |
|---|---|---|
| 3.x preview | `v3` | `net10.0`, `net8.0`, `netstandard2.0` |
| 2.x | [`release/2.x`](https://github.com/chakphanu/ThaiNationalIDCard/tree/release/2.x) | Compatibility with .NET Framework 4.0 to 4.6.1. Bug fixes only. |

v3 is a breaking rewrite: see the
[migration guide](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/migration-v2-to-v3.md).

## Install

Nothing of v3 is on NuGet yet, so for now add a project reference to `src/ThaiNationalIDCard`. Once a preview is published:

```
dotnet add package ThaiNationalIDCard --version 3.0.0-preview.1
```

## Quick start: public mode

Public is the default. It reads the fields below and nothing else, and the photo only when asked for.

```csharp
using ThaiNationalIDCard;

var reader = new ThaiIdCardReader();

// The first reader holding a readable card, in public mode, without the photo.
ThaiIdCardData card = await reader.ReadAsync(cancellationToken);

// Or a reader you name, with options.
string name = reader.GetReaderNames()[0];
ThaiIdCardData withPhoto = await reader.ReadAsync(
    name,
    new ReadOptions { Mode = CardReadMode.Public, IncludePhoto = true },
    cancellationToken);

if (card.Quality.CitizenId == FieldQuality.Ok && card.IsValidCitizenId)
{
    Console.WriteLine(card.ThaiName.FirstName);
}
```

- A value is what the card gave. `card.Quality` says per field whether it is `Ok`, `Suspect`, `Failed` or `NotRead`.
- `ThaiCardDate` keeps `Raw`, and `Year`, `Month` and `Day` are null when the card holds `00` or `0000`. Nothing is filled in.
  `ExpiryDate.IsLifetime` is true for `99999999`.
- `IsValidCitizenId` checks length, digits and the check digit, and nothing more.
- `ToString()` on these types and exception messages never hold card data, so logging them cannot leak it.
- `ReadAsync` holds a thread pool thread for the whole read, as PC/SC is blocking.

## Quick start: private mode

Private mode is a separate, explicit choice. Read the [consent section](#consent-and-legal) first.

```csharp
ThaiIdCardData full = await reader.ReadAsync(
    name,
    new ReadOptions { Mode = CardReadMode.PublicAndPrivate },
    cancellationToken);

ThaiIdCardPrivateData extra = full.Private!;
FieldQuality laserQuality = extra.LaserIdQuality;
byte[]? raw0177 = extra.MoiRaw0177.GetBytes();
```

Values whose meaning public sources disagree on are raw bytes named by position, such as `MoiRaw0177`. A private field that
fails, or an applet the card does not have, shows up as that field's quality and costs the public fields nothing.
[docs/privacy.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md) lists every field.

## Monitor

`ThaiIdCardMonitor` watches any number of readers and only notifies. It never connects to a card, so reading stays a decision
of your code. Events carry the reader name.

```csharp
using var monitor = new ThaiIdCardMonitor(reader.GetReaderNames());
monitor.CardInserted += (sender, e) => _ = ReadAndShowAsync(e.ReaderName);
monitor.CardRemoved += (sender, e) => ClearScreen(e.ReaderName);
monitor.MonitorError += (sender, e) => ReportProblem(e.ReaderName, e.Failure);
monitor.Start();
```

Events arrive on a thread pool thread, one at a time. A UI has to marshal them itself, for example with `Control.BeginInvoke`.
An exception from a handler is dropped, so catch your own. A card already in the reader at `Start()` raises `CardInserted`
too. A reader plugged in later is not noticed until `Start()` runs again.

## Errors

| Exception | Meaning |
|---|---|
| `SmartCardServiceUnavailableException` | No PC/SC service. `NativeLibraryMissing` tells when the native library could not load. |
| `ReaderNotFoundException` | No reader, or not the one named. |
| `CardNotPresentException` | The reader holds no usable card. |
| `CardRemovedException` | The card left during the read. |
| `UnsupportedCardException` | The reader cannot talk to the card, or the card refused the applet. |
| `UnknownReadBehaviourException` | No citizen ID came back with any allowed attempt. The trace is what the next release needs. |
| `CardCommunicationException` | Any other failure. `Failure` tells which, `StatusWord` gives the status when there was one. |
| `ReadCanceledException` | The read was cancelled. It is an `OperationCanceledException`. |

All but the last derive from `ThaiIdCardException`. `Diagnostics` on the exception and on the result holds the trace: status
words, lengths and pass or fail, with no card data.

## Platforms

The package uses [PCSC](https://www.nuget.org/packages/PCSC) over the PC/SC service of the system. Only the Linux test suite
and the build were run during development, always without a reader, and none of the below says anything about a card.

| OS | What it needs |
|---|---|
| Windows | `winscard.dll` is part of Windows. The Smart Card service has to be running: `net start SCardSvr`. |
| Linux | `pcscd` and libpcsclite: `sudo apt install pcscd libpcsclite1` (Debian, Ubuntu) or `sudo dnf install pcsc-lite` (Fedora), then `sudo systemctl start pcscd`. |
| macOS | The PCSC package calls the system `PCSC.framework`, so nothing extra should be needed. It has not been run on macOS. |

`ThaiNationalIDCard.Windows` is a small Windows-only package that turns `GetPhoto()` bytes into a `System.Drawing.Image` or a
WPF `BitmapSource`, with `card.GetPhoto()?.ToImage()` and `card.GetPhoto()?.ToBitmapSource()`.

Samples are in `samples/`, with no real data: `ConsoleSample` runs anywhere and masks the citizen ID by default, and
`WinFormsSample` shows the monitor, marshalling, the photo and private mode as an opt-in.

## Diagnostics

`thaiid-diagnose` reads a card and prints what happened: status words, lengths, the P2 used and pass or fail per field. It has
no option that prints card data. This output is what moves the project past preview.

```
dotnet run --project tools/ThaiNationalIDCard.Diagnose -c Release -- --mode public
```

Use it only on your own card or with the holder's consent. What to run and what to send back is in
[docs/hardware-test.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/hardware-test.md).

## Consent and legal

*Written November 2026. This is not legal advice and it has not been reviewed by a lawyer.*

- **You are responsible for consent.** This library is a tool in between. By the decision of the project owner, the people who
  use it are responsible for the holder's consent and for the law that applies, and they choose the mode for each read.
- Section 12 of the Thai Identity Card Act sets criminal penalties for accessing or disclosing data in the card memory, other
  than the general data shown on the card, without the holder's consent. The PDPA may apply as well, and its section 26 names
  religion and health data as sensitive.
- **Reading a card is not proof of identity.** A successful read and a valid check digit show that a card answered. The library
  has no `IsVerified` or `IsGenuine`, on purpose.
- PIN and VERIFY commands, fingerprint commands, write commands and some chip areas are refused in every mode, by a guard
  under the read logic.

The citations and what is still open are in
[docs/privacy.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md).

## Documentation

- [Architecture](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/architecture.md)
- [Migration from 2.x](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/migration-v2-to-v3.md)
- [Privacy and law](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md)
- [Hardware test](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/hardware-test.md)
- [Changelog](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/CHANGELOG.md)

## Credit

- The original APDU commands came from Mr.Manoi: <http://hosxp.net/index.php?option=com_smf&topic=22496>
- The 3B 78 card type came from the [kolry wiki](https://github.com/kolry/ThaiNationalIDCard/wiki/_pages).
- With thanks to the .NET Thailand community, and their fork at
  [dotnetthailand/ThaiNationalIDCard](https://github.com/dotnetthailand/ThaiNationalIDCard).

BSD 2-Clause license: see [LICENSE](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/LICENSE).
