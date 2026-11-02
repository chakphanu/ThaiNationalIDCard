# Changelog

Changes of the 3.x line. The 2.x line has its own changelog on
[`release/2.x`](https://github.com/chakphanu/ThaiNationalIDCard/tree/release/2.x).

## 3.0.0-preview.1 (not published yet)

**Preview: nothing here has been tested on a real card or reader.** The read sequence and field layout come from public code and
from reasoning about it, and the tests run against a simulator. Please run `thaiid-diagnose` if you can: see
`docs/hardware-test.md`.

### Breaking changes

See `docs/migration-v2-to-v3.md` for the member-by-member table.

- **Targets:** now `net10.0`, `net8.0` and `netstandard2.0`. `net40` to `net461` stay on `release/2.x`. PCSC goes to 7.0.1.
- **Removed:** `ThaiIDCard`, `Personal`, `APDU_THAILAND_IDCARD` and its types, and `IAPDU_THAILAND_IDCARD`. There is no
  compatibility layer.
- **New entry points:** `ThaiIdCardReader` with `ReadAsync`, returning `ThaiIdCardData`, and `ThaiIdCardMonitor`.
- **Failures are exceptions:** `readAll` returning null, null passed to monitor handlers and `ErrorCode()`/`Error()` are gone, replaced
  by typed exceptions under `ThaiIdCardException`, plus `ReadCanceledException`.
- **The monitor no longer reads.** `CardInserted` gives the reader name and ATR, and your handler calls `ReadAsync`.
- **Events are not marshalled.** Monitor events run on a thread pool thread, one at a time.
- **Dates:** `ThaiCardDate` replaces `DateTime`. A birth year `0000` is null, where 2.x gave the current year. Month or day `00` is
  null, where 2.x gave 1. Expiry `99999999` is `IsLifetime`, where 2.x gave `9456-12-31`.
- **Address:** labelled properties are set only when a part carries that Thai label, and `RawParts` keeps every part. Position-based
  `addrLane` and `addrRoad` are gone.
- **Sex:** the `CardSex` enum and `SexRaw` replace the text value.
- **Photo:** `PhotoRaw` becomes `GetPhoto()`, which returns a copy. `PhotoBitmap` moves to the new `ThaiNationalIDCard.Windows`
  package. A photo with no valid JPEG start and end is not given, and `PhotoFailure` says why.
- **Removed members with no replacement:** `readCitizenid`, `Open`, `Close`, `Version`, `Info`, `Issue_Expire`, `ExpireString` and
  `BirthdayYearString`.

### Added

- `ReadOptions.Mode`: `CardReadMode.Public` is the default, and `CardReadMode.PublicAndPrivate` is an explicit choice. Private mode adds the
  value taken as the Laser ID, nine NHSO ranges and four raw MOI ranges. Values whose meaning public sources disagree on stay raw and
  are named by position. `ChipDataRaw` is in the API but is never filled: the guard refuses chip data commands in every mode.
- A command guard under the read logic. It refuses PIN, VERIFY, fingerprint and write commands, and the ranges `1627-173D` and
  `015A-0166`, in every mode.
- `ThaiIdCardQuality`: `Ok`, `NotRead`, `Suspect` or `Failed` per field.
- `ReadDiagnostics` and `TraceEntry`: a trace on the result and on exceptions, of enums and numbers only.
- `ThaiIdCardMonitor`: any number of readers, the reader name on each event, and `MonitorError`.
- `IThaiIdCardReader`, for mocks.
- `thaiid-diagnose`: a tool that prints status words, lengths and pass or fail, and never card data.
- `ThaiNationalIDCard.Windows`: `ToImage()` and `ToBitmapSource()` for the JPEG bytes from `GetPhoto()`.
- `samples/ConsoleSample` and `samples/WinFormsSample`, rewritten, with no real data.
- `docs/architecture.md`, `docs/migration-v2-to-v3.md`, `docs/privacy.md`, `docs/hardware-test.md`, and CI on Linux and Windows.

### Changed

These values come from public sources and are not confirmed on a real card by this project.

- The address is read as 160 bytes, where 2.x read 100.
- The photo is read as 5118 bytes, where 2.x read 5100, by adding the chunk `80 B0 15 67 02 00 12`.
- Bytes `0177-0178` are read only in private mode.
- The ATR only orders the P2 attempts. One try of the other P2 is allowed per read, decided by whether the citizen ID comes back with 13
  digits and a fitting check digit.
- The SW1 state machine covers `90 00`, `61 xx`, `61 00` as 256, chained `61 xx`, and `6C xx` with one resend. Readers that answer directly
  and readers that answer `61 xx` share one code path.
- A status other than `90 00` fails that field, and its bytes are not decoded as text.
- The read runs in a transaction with no sleep inside, and with retries outside it. The fixed 1500 ms delay is gone.
- `ToString()` of the data types and exception messages hold no card data.

### Not done

- No test on a real card or reader. `docs/hardware-test.md` lists what only a real card can answer.
- `CardProfile` is internal, so a moved field or a new AID needs a new release.
- The legal position has not been reviewed by a lawyer. See `docs/privacy.md`.
