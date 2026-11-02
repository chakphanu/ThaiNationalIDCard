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
- `SendCommand` on readers that answer with data and `90 xx` on the first transmit (inferred from issue #25; #15 shows
  the same corruption, but from a reporter's own concatenation loop, so it is not evidence of such readers): the first
  receive buffer is now 258 bytes, so `readAllPhoto` no longer fails with "data buffer too small", and the trailing 2
  status bytes are removed, so they no longer end up in text fields or between photo chunks. Other status words and
  the `61 xx` + GET RESPONSE path are unchanged. Behavior notes: a first response of only `90 xx` now gives an empty
  result; a first response over 256 bytes with another status word is now returned instead of failing. This reader
  behavior is inferred from issue #25; it has not been checked on hardware.
- `Open` without a reader name (issue #14) tries the readers in order and moves on when `Connect` fails. If all fail,
  the error of the first reader is reported, so single-reader users see the same error as before. An explicit reader
  name never falls through. Only `Connect` failures move on: if the next reader connects but its card fails the
  applet select, `Open` reports that failure (error code 1) instead of the first reader's error.
- `MonitorStart` cancels the previous monitor before creating a new one (it is not disposed, so its context stays open until exit). Behavior note: calling it twice
  on one `ThaiIDCard` used to leave both monitors running; only the last one runs now. Use one instance per reader.
- Added a test for the raw lifetime expire value `99999999` (issue #3). `Expire` still returns 31 Dec 9456 for it.
- `EF_CARD_ISSUE_EXPIRE` now asks for 16 bytes (Le `0x10`, was `0x12`), so card bytes 0177-0178 are no longer read.
  `Personal` only uses issue (8 bytes) and expire (8 bytes), and public projects disagree whether those 2 bytes are
  card type, version or religion. `Issue`, `Expire` and `ExpireString` are unchanged. Behavior note: the public
  `EF_CARD_ISSUE_EXPIRE` property returns the new command. The new length has not been checked on hardware.

### Previously unreleased changes

The published 2.0.0 package depended on PCSC 3.7.0 (and also bundled `pcsc-sharp.dll`); it was built on 2017-07-13.
2.0.1 depends on PCSC 5.0.0 and PCSC.Iso7816 5.0.0, and it is the first package to include these changes from the
repository history:

- 2017-12: new `Personal` getters `Th_Middlename`, `En_Middlename`, `BirthdayYearString` and `ExpireString`.
- 2018-05: RAW protocol support for legacy readers (`1da1edf`), upgrade to PCSC 4.0.0 and PCSC.Iso7816 4.0.0.
- 2020-02: `Birthday` no longer throws for year, month or day `00` (`df52b19`, PR #22); upgrade to PCSC 5.0.0 and
  PCSC.Iso7816 5.0.0 (`caba0d1`).

### Performance

- `Personal` splits the address string once and caches the trimmed parts instead of calling `Split` in every
  `addr*` getter. The cache is reset when `Address` is set.
- The photo `MemoryStream` is pre-sized.

### Build and packaging

- SDK-style project multi-targeting net40, net45, net452, net46 and net461. The NuGet package contains all five
  `lib/` folders (2.0.0 only had `lib/net40`) and depends on PCSC 5.0.0 and PCSC.Iso7816 5.0.0 (2.0.0 depended on PCSC 3.7.0 and
  bundled an old `pcsc-sharp.dll`). The hint paths that pointed outside the repository are gone.
- The example WinForms project is SDK-style, targets net461 and uses PCSC 5.0.0 through the library project.
- `AssemblyVersion` stays 2.0.0.0; package and file version are 2.0.1.
- Removed the committed `.vs` folder and `.nupkg` files; `.gitignore` updated.
- Added xUnit tests (synthetic data, fake PC/SC reader) and a GitHub Actions workflow (build, test, pack; no publish).
