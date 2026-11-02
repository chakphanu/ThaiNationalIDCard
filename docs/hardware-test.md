# Hardware test

**Nothing in this project has been tested on real hardware yet.** Every read sequence, status word, P2 rule and field
layout in v3 comes from public code and from reasoning about it. The test suite runs against a simulator, so it shows
that the logic matches its assumptions and not that the assumptions are right. v3 stays at `3.0.0-preview.N` until a real
card and reader have confirmed them.

`thaiid-diagnose` exists to collect that evidence. It reads a card and prints what happened, with no card content.

## Before you run it

- Use it only on your own card, or on a card whose holder has agreed. The tool prints this notice too and does not ask
  for confirmation, because it also has to run unattended.
- `--mode private` reads extra areas of the chip. Public sources disagree on what they hold, and they may include
  sensitive data. Thai law (Section 12 of the Identity Card Act, and the PDPA) may apply to reading them without consent.
  This was noted from project research on 2026-11-02, has not been reviewed by a lawyer, and is not legal advice.
- The tool never sends a PIN or VERIFY command, never reads the fingerprint areas and never writes. A guard below the
  read logic refuses those commands in every mode.
- If the first P2 value fails, the tool re-selects and reads once more with the other value. No public source reports
  side effects from this, but no public source tested it either. For a first try, prefer an expired card whose holder
  accepts that risk.

## Run it

Linux needs the PC/SC service: `sudo apt install pcscd libpcsclite1` (Debian, Ubuntu) or `sudo dnf install pcsc-lite`
(Fedora), then `sudo systemctl start pcscd`. Windows has it built in. Nothing is published yet, so build it from this
repository for now.

With a .NET 10 SDK:

```
dotnet run --project tools/ThaiNationalIDCard.Diagnose -c Release -- --mode public
```

`dotnet tool` also needs an SDK. For a machine without one, publish a self-contained binary on any machine with the SDK,
to a folder outside the repository, and copy the one binary over:

```
dotnet publish tools/ThaiNationalIDCard.Diagnose -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ../thaiid-diagnose-linux-x64
dotnet publish tools/ThaiNationalIDCard.Diagnose -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ../thaiid-diagnose-win-x64
```

Then run `./thaiid-diagnose` or `thaiid-diagnose.exe`. The Linux binary still needs the PC/SC service and library above.

| Option | Meaning |
|---|---|
| `--mode public` | Default. Reads what is printed on the card. |
| `--mode private` | Also probes the private areas for status and length. The tool lists them before reading. Chip data is not read: see below. |
| `--reader NAME` | Use this reader, by its full name or as the report prints it. Default: first reader holding a card. |
| `--json` | One JSON document on stdout, with the notice on stderr. |

No option prints card data, and none is planned.

| Exit code | Meaning |
|---|---|
| 0 | The read finished. Fields may still have failed: the report says which. |
| 1 | Internal failure |
| 2 | Bad arguments |
| 3 | No PC/SC service |
| 4 | Native PC/SC library missing |
| 5 | No reader, or the named reader does not exist |
| 6 | No card in the reader |
| 7 | Unsupported card: the card or reader failed at transport level, or the card refused the expected applet |
| 8 | Unknown read behaviour: no citizen ID came back with any allowed attempt |
| 9 | Card removed during the read |
| 10 | Other communication failure |
| 130 | Cancelled |

## What to send back

Run `--mode public` first, then `--mode private` only if the holder agrees, and send:

- the whole output of each run, with the exit code
- the year the card was issued and whether it is expired, if the holder is fine with sharing that
- how the reader is connected, for example USB, over RDP, or from a service

The report has the tool and library version, OS, reader name, protocol, ATR, each command's status word and length, P2 used
and pass or fail per field. It never has card content. Serials are removed from reader names, on a best effort basis, since
real names have not been seen here: read the `reader:` line before you share it. Do not add values from the card to a report
or an issue, including to help explain it.

## What a real card must answer

The tool answers some of these directly. Others need the holder to compare results with what they can see on their own
card, and only the conclusion should be sent back, never the value.

| Question | Tool output shows it? |
|---|---|
| What does a wrong P2 really answer, and is the pending response kept? | Yes, if the first P2 fails |
| Does retrying P2 have side effects? | No: the holder has to watch for a card that stops working |
| Do `3B 67` cards exist? Do they need P2 01? | Yes: ATR, `AtrP2RuleUsed` and P2 used |
| Does SELECT answer `61 0A`, on which generations? | Yes: status word |
| What are the 10 bytes after SELECT `61 0A`? Does leaving them unread shift later data? | Partly: they are drained unread and never printed, so only the effect on later reads shows |
| What does `0177-0178` mean? | No: compare with the holder's knowledge, in a separate approved test |
| Do direct-answer readers exist as assumed? | Yes: no GET RESPONSE in the trace, P2 used `none` |
| Does a direct-answer card send extra bytes? They cannot be prevented, only dropped. | Yes: `ExcessDropped` flag |
| Does the transaction work on real readers, and on Windows over the 5 second idle limit? | Yes: transaction events, `CardReset` failures |
| Which address parts sit in which position? Is the 8 part shape general? | Partly: `AddressShape` shows the count fits or not, order needs a separate approved test |
| Do real name, date, sex and address shapes pass the validators? | Yes: `checks_failed` per field |
| Do cards with a checksum mismatch exist? | Yes: `ChecksumMismatch` |
| Is the photo always `FF D8` to `FF D9` inside 5118 bytes? | Yes: `JpegStart`, `JpegEnd` |
| Does a card answer `61 xx` after all bytes of a field, as with `61 12` for Le 10? | Yes: `MoreOffered` |
| Does the next command work after leaving such bytes on the card? | Yes: the next field's status |
| Does a card ask `6C 12` for issue and expiry? The guard refuses that read, so the field fails. | Yes: `verdict=OutsideDeclaredRange` |
| Do warning status words (`62 xx`, `63 xx`) come with useful data? They count as failures today. | Partly: status word and length, not the data |
| Do ADM and NHSO exist across generations, and what answers if not? | Yes: `AppletUnavailable` and the status word, in private mode |
| Does `80 00 00 00 07` answer 7 bytes and `61 10`, then 16 bytes? Other shapes keep the bytes and give no value. | Yes: the trace of `AppletData` and `GetResponse`, `received=` and `AppletDataShape` |
| Does a card ask `6C 17` for that command? The guard refuses the resend, so the field fails. | Yes: `sw=6C17` and `FollowUpRejected` |
| Is the value in that answer the Laser ID, 12 letters or digits with 4 NUL? | Partly: `AppletDataShape` shows the shape fits, the meaning needs a separate approved test |
| What does SELECT with no AID (`00 A4 04 00`) choose, and is it safe to follow with `80 CA 9F 7F`? Chip data stays out until a separate approved test answers this. | No: the guard refuses both in every mode |
| Do `CardInserted` and `CardRemoved` arrive for every insert and removal on a real reader, again and again? Issue #16 reports a card read only once. Its cause is not known, and the monitor tests do not show it is gone. | No: the tool does not use `ThaiIdCardMonitor`, so this needs a separate run |
| Does the real monitor report a card already in the reader at the start, and what does unplugging a reader raise? | No: same |
| Are real reader names and the serial removal right? | Yes: check the `reader:` line against the real name |
| Are the retry delays (100, 300, 900 ms) enough? | Partly: `Retry` events |

## If a finding changes something

Read behaviour belongs in `ReadPolicy` (`src/ThaiNationalIDCard/Workflow/ReadPolicy.cs`) and layout in the profiles
(`src/ThaiNationalIDCard/Card/CardProfile.cs`). Most findings should need a change there and nowhere else.
