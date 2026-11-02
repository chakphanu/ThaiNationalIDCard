# Changelog

All notable changes of the 2.x maintenance line (`release/2.x`). The line is bug-fix only and stays source and
behavior compatible with 2.0.0.

## 2.0.1

### Fixed

- `ThaiIDCard` photo read: each photo chunk was transmitted to the card twice (`SendCommand` was called twice per
  chunk). It is now called once, which halves the APDU traffic of `readAll(true)` / `readAllPhoto`.
- `ThaiIDCard` GET RESPONSE: the result of the second `Transmit` was never checked. A failed GET RESPONSE
  now throws `PCSCException` (as a failed first `Transmit` already did) instead of silently returning a zero-filled
  buffer that was decoded as card data. Behavior note: callers of `readAll`/`readCitizenid` can now get a
  `PCSCException` where they used to get corrupt data; on the monitor path (`eventCardInserted*`) the exception is
  caught and reported through `ErrorCode()`/`Error()`.
- `GetReaders`: rethrows with `throw;` (keeps the original stack trace) and always releases its PC/SC context.
  It no longer leaves a released context in the internal context field.
- `Open`: when anything fails after connecting (no ATR, applet select fails or throws, unsupported protocol) the
  reader is disconnected and the context released before returning `false`. Return values and error codes are
  unchanged. `readAll`/`readCitizenid` likewise disconnect when a read throws (the exception still propagates).
- Card inserted monitor callback: with both `eventCardInserted` and `eventCardInsertedWithPhoto` subscribed the card is
  now read once (with photo) and the same `Personal` is passed to both handlers (it was read twice). Handlers still
  receive `null` when `Open` failed, exactly as before (the reason is also available in `ErrorCode()`/`Error()`), and
  exceptions can no longer escape on the PC/SC monitor thread: they are caught and stored in the error state
  (error code 256 for `PCSCException`, 512 for any other exception).
- `Personal.Issue` and `Personal.Expire` no longer throw for month or day `00`; month/day are clamped like
  `Birthday` does (below 1 -> 1) and like `Expire` did (above 12 -> 12, above 31 -> 31). A day beyond the month
  length (for example 31 April) is clamped to the last day of the month. `Personal.Birthday` and
  `BirthdayYearString` are unchanged on purpose (year 0 still maps to the current year, see PR #22).

### Performance

- `Personal` splits the address string once and caches the trimmed parts instead of calling `Split` in every
  `addr*` getter. The cache is reset when `Address` is set.
- The photo `MemoryStream` is pre-sized.

### Build and packaging

- SDK-style project multi-targeting net40, net45, net452, net46 and net461. The NuGet package contains all five
  `lib/` folders (2.0.0 only had `lib/net40`) and depends on PCSC 5.0.0 and PCSC.Iso7816 5.0.0 (2.0.0 bundled an old
  `pcsc-sharp.dll`). The hint paths that pointed outside the repository are gone.
- The example WinForms project is SDK-style, targets net461 and uses PCSC 5.0.0 through the library project.
- `AssemblyVersion` stays 2.0.0.0; package and file version are 2.0.1.
- Removed the committed `.vs` folder and `.nupkg` files; `.gitignore` updated.
- Added xUnit tests (synthetic data, fake PC/SC reader) and a GitHub Actions workflow (build, test, pack; no publish).
