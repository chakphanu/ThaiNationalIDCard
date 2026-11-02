# Migrating from 2.x to 3.0

v3 is a rewrite with no compatibility layer. If you need .NET Framework 4.0 to 4.6.1, or cannot change code now, stay on
[`release/2.x`](https://github.com/chakphanu/ThaiNationalIDCard/tree/release/2.x), which still gets bug fixes.

**v3 is a preview that has not been tested on a real card.** 2.x has a longer history in the field. Check v3 with your own
reader and card before you depend on it.

v3 targets `net10.0`, `net8.0` and `netstandard2.0`. .NET Framework can use the `netstandard2.0` asset. CI covers it on `net48` only.

## Shape of the change

```csharp
// 2.x
var idcard = new ThaiIDCard();
Personal personal = idcard.readAll();
if (personal != null)
{
    Console.WriteLine(personal.Citizenid);
}
else if (idcard.ErrorCode() > 0)
{
    Console.WriteLine(idcard.Error());
}

// 3.0
var reader = new ThaiIdCardReader();
try
{
    ThaiIdCardData card = await reader.ReadAsync(cancellationToken);
    Console.WriteLine(card.CitizenId);
}
catch (ThaiIdCardException ex)
{
    Console.WriteLine(ex.Message);
}
```

## `ThaiIDCard` to `ThaiIdCardReader` and `ThaiIdCardMonitor`

| 2.x | 3.0 |
|---|---|
| `new ThaiIDCard()` | `new ThaiIdCardReader()`, or the `IThaiIdCardReader` interface for mocks |
| `GetReaders()` | `GetReaderNames()`. Empty when there are none, where 2.x threw |
| `readAll()` | `ReadAsync(token)`, or `ReadAsync(name, options, token)` |
| `readAll(true)`, `readAllPhoto()` | `ReadAsync(name, new ReadOptions { IncludePhoto = true }, token)` |
| `readAll(with_photo, readerName)` | `ReadAsync(readerName, new ReadOptions { IncludePhoto = with_photo }, token)` |
| `readCitizenid()` | No equivalent. A read covers all public fields |
| `Open()`, `Close()` | Removed. Each read connects and disconnects on its own |
| `ErrorCode()`, `Error()` | Exceptions |
| `Version()` | Removed. Use the assembly or package version |
| `MonitorStart(name)` | `new ThaiIdCardMonitor(names)`, the events, then `monitor.Start()` |
| `MonitorStop(name)` | `monitor.Stop()` or `monitor.Dispose()` |
| `eventCardInserted`, `eventCardInsertedWithPhoto` | `CardInserted`, with `ReaderName` and `AtrHex`. It carries no card data: call `ReadAsync` from your handler |
| `eventCardRemoved` | `CardRemoved`, with `ReaderName` |
| `eventPhotoProgress` | `ReadOptions.PhotoProgress`, an `IProgress<PhotoReadProgress>` with `BytesRead` and `TotalBytes` |
| None | `MonitorError` tells when watching a reader failed |

## `Personal` to `ThaiIdCardData`

| 2.x | 3.0 |
|---|---|
| `Citizenid` | `CitizenId`, plus `IsValidCitizenId` and `PersonCategoryCode` |
| `Birthday` | `BirthDate`: a `ThaiCardDate`, see below |
| `BirthdayYearString` | `BirthDate.Year`, nullable and Gregorian |
| `Sex` (text from the card) | `Sex`, an enum `CardSex`, and `SexRaw` for the byte |
| `Th_Prefix`, `Th_Firstname`, `Th_Middlename`, `Th_Lastname` | `ThaiName.Prefix`, `.FirstName`, `.MiddleName`, `.LastName` |
| `En_Prefix`, `En_Firstname`, `En_Middlename`, `En_Lastname` | `EnglishName.Prefix`, `.FirstName`, `.MiddleName`, `.LastName` |
| `Info` | Removed. `ThaiName.Raw` and `EnglishName.Raw` hold the raw names |
| `Address` | `Address.Full` |
| `addrHouseNo` | `Address.RawParts`. No property, as nothing labels this part |
| `addrVillageNo` | `Address.Moo` |
| `addrLane` | `Address.Soi` or `Address.Trok`, see below |
| `addrRoad` | `Address.Road`, see below |
| `addrTambol` | `Address.Tambon` or `Address.Khwaeng` |
| `addrAmphur` | `Address.Amphoe` or `Address.Khet` |
| `addrProvince` | `Address.Province` |
| `Issue` | `IssueDate`: a `ThaiCardDate` |
| `Expire`, `ExpireString` | `ExpiryDate`: a `ThaiCardDate` |
| `Issue_Expire` | Removed |
| `Issuer` | `Issuer` |
| `PhotoRaw` | `GetPhoto()`, which returns a copy |
| `PhotoBitmap` | `GetPhoto()?.ToImage()` from the `ThaiNationalIDCard.Windows` package. The core library has no Windows dependency now |

`Personal` had setters because the library filled it in. `ThaiIdCardData` is read-only, and has a public constructor for mocks.

## 2.x quirks that changed on purpose

Code that relied on these needs a closer look than a rename.

| 2.x did | 3.0 does | Why |
|---|---|---|
| `Birthday` with year `0000` gave the **current year** | `BirthDate.Year` is null | A made-up year looks like a real birth date, and a wrong age can follow from it |
| `Birthday`, `Issue` and `Expire` with month or day `00` gave `1` | `Month` and `Day` are null, and `Raw` keeps what the card holds | Same. Public sources describe `00` as unknown |
| `Expire` for `99999999` gave `9456-12-31` | `ExpiryDate.IsLifetime` is true, and `Year`, `Month` and `Day` are null | Public sources describe `99999999` as a card with no expiry. The date was only a product of clamping |
| Monitor handlers got a **null** `Personal` on failure, with the reason in `Error()` | The monitor reads nothing. `ReadAsync` raises a typed exception in your own handler | The failure now reaches you with its type and trace. A shared error field did not say which read it belonged to |
| `readAll` returned **null** on failure | Exceptions | Same |
| A bad status could come back as data and be decoded as text | A status other than `90 00` fails that one field. See `Quality` | Status bytes must not end up as text |
| `addrLane` was part 2 and `addrRoad` part 3, by position | A labelled property is set only when a part starts with that Thai label | Public code disagrees on the order, and only one card's shape has been published. Without a label the value stays in `RawParts` |

For age, `BirthDate.AgeReferenceDate` follows Thai Civil Code section 16 for an unknown month or day. It is a date to count from,
and not the birth date.

There is no `IsExpired`. An English translation of the Identity Card Act has a rule that a card not yet expired when the holder
turns 70 stays valid for life, so comparing dates alone could be wrong. That rule has not been confirmed against the Thai text.

## Also different

- **Choosing what to read:** `ReadOptions.Mode` is `Public` by default. 2.x had no such choice. See `docs/privacy.md`.
- **Bytes 0177-0178:** some 2.x versions read these along with the issue and expiry dates. v3 reads them only in private mode,
  raw, as `Private.MoiRaw0177`. Public sources disagree on what they mean.
- **Lengths:** the address is read as 160 bytes, where 2.x read 100, and the photo as 5118, where 2.x read 5100. The new values
  come from public sources agreeing, and are not confirmed on a real card by this project.
- **Photo:** a photo without a valid JPEG start and end is not given at all, and `PhotoFailure` says why.
- **Threads:** the `Async` methods run the blocking PC/SC calls on a thread pool thread and do not touch your synchronization
  context. Marshal monitor events and progress to your UI thread yourself.
- **Readers:** a read never falls back to another reader when you name one. `ReadAsync(token)` picks the first reader holding a
  readable card, and with several readers in use it is better to name one.
- **Logging:** `ToString()` of the data types and exception messages hold no card data. Log those instead of field values.
- **Identity:** reading is not proof of identity, and v3 has no `IsVerified`. See `docs/privacy.md`.
