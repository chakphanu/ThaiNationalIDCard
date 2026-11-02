# Architecture

Everything here was built and tested against a simulator. No part of it has run against a real card.

The aim is a library that survives new card generations without code changes as far as that is possible: the card's own
behaviour decides how to read it, and what cannot be decided that way lives in data. A moved layout, a new AID or mandatory
authentication cannot be handled automatically and need a new release.

## Layers

```
Public    ThaiIdCardReader, IThaiIdCardReader, ThaiIdCardMonitor, models, ReadOptions, ReadDiagnostics, exceptions
Workflow  CardReadWorkflow, ReadRun, ReadPolicy
Parsing   CardDataParser, ThaiCardTextDecoder, JpegScanner, FieldValidators
Card      CardProfile, MoiProfile, NhsoProfile, AdmProfile, MoiCardCommands
Protocol  ApduExchange, ApduResponse
Hardware  IRawCardChannel, GuardedCardConnection, CommandPolicy, IReaderMonitor, ReaderSelector
          Hardware/Pcsc: PcscRawCardChannel, the only code that knows PCSC
```

Everything below the public types is `internal`. `CardProfile` is internal too in this first version, so a moved field or a
new AID needs a release until it is opened up. It can become public later without a breaking change.

**Rule: only `Hardware/Pcsc` knows PCSC.** No type in another namespace mentions a PCSC type, in a signature or in a method
body, and no public signature exposes one. A PC/SC failure leaves that layer as a `ThaiIdCardException`.

`ArchitectureTests` enforces the layer rules on the built assembly, including the IL of method bodies, and has control tests to
show the scan finds what it looks for. It also checks that:

- `Card` and `Parsing` do not depend on `Hardware` or `Protocol`
- `Workflow` has no way to wait except an injected delay
- the library has no way to log
- the monitor has no way to read a card

`ThaiNationalIDCard.Windows` is separate and depends on nothing here. It only turns JPEG bytes into Windows image types.

## Guard

`GuardedCardConnection` is sealed and is the only way to send a command. `ApduExchange` accepts nothing else, so PC/SC and the
simulator both sit under the same guard, and every test exercises the real one. It tracks the selected applet and whether a
GET RESPONSE is due, because READ ranges belong to one applet: `015D` is an NHSO field but sits inside an off limits MOI range.

`CommandPolicy` is an allowlist with no way to add a raw command.

| Mode | Allowed |
|---|---|
| `Public` | SELECT of MOI, GET RESPONSE `00 C0 00 {00,01}` when one is due, `80 B0` inside the public ranges of the profile |
| `PublicAndPrivate` | Adds SELECT of NHSO and ADM, the private MOI ranges, the NHSO ranges, and `80 00 00 00 07` in ADM |
| Refused in every mode | `80 20`, `80 B4`, `B0 34`, ISO write and reference data instructions, SELECT with no AID, `80 CA`, and anything not listed |

A resend after `6C xx` goes through the guard like any other command, so a card cannot widen a read beyond its declared range.
A profile cannot widen what is off limits either: a readable range that overlaps a never-read range fails when the profile is
built, before any card access.

## Read sequence

1. List the readers with their state and pick one. `ThaiIdCardReader` takes one read per reader at a time within the process.
2. Connect in Shared mode. A retry happens outside the transaction after a delay, for `CardReset` and `SharingViolation`, and for
   `CardRemoved` only while connecting. There is no retry after a timeout, and the fixed 1500 ms delay of 2.x is gone.
3. Open the transaction. Nothing sleeps inside it, because Windows resets a card after 5 seconds idle in a transaction.
4. SELECT MOI. Anything but `90 00` or `61 xx` raises `UnsupportedCardException`. After `61 xx`, a GET RESPONSE is sent and its
   bytes are dropped unread.
5. CID gate, `80 B0 00 04 02 00 0D`:
   - 13 digits and the check digit fits: lock this P2.
   - 13 digits but it does not fit: re-SELECT and read again with the same P2, once. If it still does not fit, go on and mark the
     field `Suspect`.
   - Anything else: re-SELECT and try the other P2, once per read. If no GET RESPONSE was sent, P2 played no part, so the same read
     is tried once more instead. If that fails too, raise `UnknownReadBehaviourException`.
6. Read the fields of the profile. A status other than `90 00` fails that field, and its bytes are not decoded as text.
7. In private mode, read the private MOI ranges, then SELECT NHSO and ADM. A failure here costs only those fields.
8. Close the transaction in `finally`.

`ApduExchange` is the SW1 state machine: `90 00` ends, `61 xx` sends GET RESPONSE (`00` means 256) up to a round limit, `6C xx`
resends once, and anything else returns the status. This is how direct-answer readers and `61 xx` readers share one code path
without knowing the reader model.

The ATR only orders the P2 attempts: `3B 67` gives `[01, 00]` and anything else `[00, 01]`. A short ATR is normal, and the
trace records when the rule was used. In the public evidence no Thai card shows `3B 67`, and the rule is kept because there is
no card to prove that removing it is safe.

## Parsing

- The photo is found by walking JPEG markers, not by trimming, as `0x20` can be image data. No start or end marker means no
  photo and a `PhotoFailure`.
- A labelled address part such as `Road` is set only when a part starts with that Thai label. `RawParts` keeps every part.
- `ThaiCardDate` fills nothing in. `AgeReferenceDate` is separate and only for counting an age.
- `Sex` is cut out as a byte before decoding, and never guessed from the prefix.

## Diagnostics

`TraceEntry` is a readonly struct of enums and numbers, so it cannot hold card data by construction, which a reflection test
checks. It travels on the result and on the exception, and no state stays behind between reads.

## Monitor

`ThaiIdCardMonitor` runs one PC/SC monitor per reader, so one missing reader does not stop the others. Monitor threads only
queue: caller code runs on a thread pool thread, one event at a time. It reports the ATR and never connects to a card.

## Where a real-card finding gets changed

Most findings should change data or policy and nothing else.

| Finding | Change |
|---|---|
| P2 behaviour, retry budget and delays, whether to use a transaction, whether to drain SELECT | `ReadPolicy` (`Workflow/ReadPolicy.cs`) |
| A field moved, a length differs, a new AID, a range to read or to stop reading | `MoiProfile`, `NhsoProfile`, `AdmProfile` (`Card/CardProfile.cs`) |
| What a Laser answer looks like | `AdmProfile` constants, marked `ASSUMPTION` |
| A status word needs new handling | `ApduExchange`, with a simulator scenario |
| A value turns out to have a confirmed meaning | A new property in the model, and in `PublicAPI.Unshipped.txt` |

Tests that pin a behaviour to a card fact say so. The golden sequences in `tests/ThaiNationalIDCard.Tests/Golden` state what 2.x
sent, and not what a card needs, so a real-card finding may rightly change them.

`docs/hardware-test.md` lists the open questions.
