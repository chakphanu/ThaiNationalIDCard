# การย้ายจาก 2.x ไป 3.0

v3 เขียนใหม่และไม่มีชั้นความเข้ากันได้ ถ้าต้องใช้ .NET Framework 4.0 ถึง 4.6.1 หรือยังแก้โค้ดตอนนี้ไม่ได้ ให้อยู่ที่ [`release/2.x`](https://github.com/chakphanu/ThaiNationalIDCard/tree/release/2.x) ซึ่งยังได้รับการแก้บั๊ก

**v3 เป็น preview ที่ยังไม่เคยทดสอบกับบัตรจริง** 2.x มีประวัติการใช้งานยาวนานกว่า แต่เอกสารนี้ไม่ได้รับรองว่าสายใดใช้ได้กับบัตรหรือเครื่องอ่านรุ่นใด ตรวจสอบ v3 กับเครื่องอ่านและบัตรของคุณเองก่อนจะพึ่งพามัน

ยังไม่มีการเผยแพร่แพ็กเกจรุ่น 2.0.1 หรือ 3.x

v3 target `net10.0`, `net8.0` และ `netstandard2.0` .NET 8 จะหมดระยะการสนับสนุนในวันที่ 10 พฤศจิกายน 2026 .NET Framework ใช้ asset ของ `netstandard2.0` ได้ CI ทดสอบกรณีนี้บน `net48` เท่านั้น และ CI ใช้ตัวจำลอง การที่ CI ผ่านจึงไม่ใช่หลักฐานด้านฮาร์ดแวร์

## ภาพรวมการเปลี่ยนแปลง

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

## จาก `ThaiIDCard` ไปเป็น `ThaiIdCardReader` และ `ThaiIdCardMonitor`

| 2.x | 3.0 |
|---|---|
| `new ThaiIDCard()` | `new ThaiIdCardReader()` หรือ interface `IThaiIdCardReader` สำหรับ mock |
| `GetReaders()` | `GetReaderNames()` คืนรายการว่างเมื่อไม่มีเครื่องอ่าน ขณะที่ 2.x โยน exception |
| `readAll()` | `ReadAsync(token)` หรือ `ReadAsync(name, options, token)` |
| `readAll(true)`, `readAllPhoto()` | `ReadAsync(name, new ReadOptions { IncludePhoto = true }, token)` |
| `readAll(with_photo, readerName)` | `ReadAsync(readerName, new ReadOptions { IncludePhoto = with_photo }, token)` |
| `readCitizenid()` | ไม่มีตัวแทน การอ่านหนึ่งครั้งครอบคลุมทุก field ของโหมด public |
| `Open()`, `Close()` | ถอดออก การอ่านแต่ละครั้งเชื่อมต่อและตัดการเชื่อมต่อเอง |
| `ErrorCode()`, `Error()` | Exception |
| `Version()` | ถอดออก ใช้เวอร์ชันของ assembly หรือแพ็กเกจ |
| `MonitorStart(name)` | `new ThaiIdCardMonitor(names)`, event ต่าง ๆ แล้ว `monitor.Start()` |
| `MonitorStop(name)` | `monitor.Stop()` หรือ `monitor.Dispose()` |
| `eventCardInserted`, `eventCardInsertedWithPhoto` | `CardInserted` พร้อม `ReaderName` และ `AtrHex` ไม่มีข้อมูลบัตรมาด้วย: เรียก `ReadAsync` จาก handler ของคุณ |
| `eventCardRemoved` | `CardRemoved` พร้อม `ReaderName` |
| `eventPhotoProgress` | `ReadOptions.PhotoProgress` เป็น `IProgress<PhotoReadProgress>` ที่มี `BytesRead` และ `TotalBytes` |
| ไม่มี | `MonitorError` แจ้งเมื่อการเฝ้าดูเครื่องอ่านล้มเหลว |

## จาก `Personal` ไปเป็น `ThaiIdCardData`

| 2.x | 3.0 |
|---|---|
| `Citizenid` | `CitizenId` พร้อม `IsValidCitizenId` และ `PersonCategoryCode` |
| `Birthday` | `BirthDate`: เป็น `ThaiCardDate` ดูด้านล่าง |
| `BirthdayYearString` | `BirthDate.Year` เป็น nullable และเป็นปีคริสต์ศักราช |
| `Sex` (ข้อความจากบัตร) | `Sex` เป็น enum `CardSex` และ `SexRaw` สำหรับไบต์ |
| `Th_Prefix`, `Th_Firstname`, `Th_Middlename`, `Th_Lastname` | `ThaiName.Prefix`, `.FirstName`, `.MiddleName`, `.LastName` |
| `En_Prefix`, `En_Firstname`, `En_Middlename`, `En_Lastname` | `EnglishName.Prefix`, `.FirstName`, `.MiddleName`, `.LastName` |
| `Info` | ถอดออก `ThaiName.Raw` และ `EnglishName.Raw` เก็บชื่อแบบดิบ |
| `Address` | `Address.Full` |
| `addrHouseNo` | `Address.RawParts` ไม่มี property เพราะส่วนนี้ไม่มีป้ายกำกับ |
| `addrVillageNo` | `Address.Moo` |
| `addrLane` | `Address.Soi` หรือ `Address.Trok` ดูด้านล่าง |
| `addrRoad` | `Address.Road` ดูด้านล่าง |
| `addrTambol` | `Address.Tambon` หรือ `Address.Khwaeng` |
| `addrAmphur` | `Address.Amphoe` หรือ `Address.Khet` |
| `addrProvince` | `Address.Province` |
| `Issue` | `IssueDate`: เป็น `ThaiCardDate` |
| `Expire`, `ExpireString` | `ExpiryDate`: เป็น `ThaiCardDate` |
| `Issue_Expire` | ถอดออก |
| `Issuer` | `Issuer` |
| `PhotoRaw` | `GetPhoto()` ซึ่งคืนสำเนา |
| `PhotoBitmap` | `GetPhoto()?.ToImage()` จากแพ็กเกจ `ThaiNationalIDCard.Windows` ตอนนี้ไลบรารีหลักไม่ขึ้นกับ Windows แล้ว |

`Personal` มี setter เพราะไลบรารีเป็นผู้เติมค่า `ThaiIdCardData` เป็นแบบอ่านอย่างเดียว และมี public constructor สำหรับ mock

## พฤติกรรมเฉพาะของ 2.x ที่ตั้งใจเปลี่ยน

โค้ดที่พึ่งพาเรื่องเหล่านี้ต้องตรวจละเอียดกว่าการเปลี่ยนชื่อ

| 2.x | 3.0 | เหตุผล |
|---|---|---|
| `Birthday` ที่ปีเป็น `0000` ให้**ปีปัจจุบัน** | `BirthDate.Year` เป็น null | ปีที่แต่งขึ้นดูเหมือนวันเกิดจริง และอาจทำให้อายุผิดตามไปด้วย |
| `Birthday`, `Issue` และ `Expire` ที่เดือนหรือวันเป็น `00` ให้ `1` | `Month` และ `Day` เป็น null และ `Raw` เก็บค่าที่บัตรมี | เหตุผลเดียวกัน แหล่งข้อมูลสาธารณะอธิบายว่า `00` คือไม่ทราบ |
| `Expire` ของ `99999999` ให้ `9456-12-31` | `ExpiryDate.IsLifetime` เป็น true และ `Year`, `Month`, `Day` เป็น null | แหล่งข้อมูลสาธารณะอธิบายว่า `99999999` คือบัตรที่ไม่มีวันหมดอายุ วันที่นั้นเป็นแค่ผลจากการ clamp |
| Monitor handler ได้ `Personal` เป็น **null** เมื่อล้มเหลว โดยเหตุผลอยู่ใน `Error()` | Monitor ไม่อ่านอะไร `ReadAsync` โยน typed exception ใน handler ของคุณ | ความล้มเหลวมาถึงคุณพร้อม type และ trace ส่วน error field ที่ใช้ร่วมกันไม่ได้บอกว่าเป็นของการอ่านครั้งไหน |
| `readAll` คืน **null** เมื่อล้มเหลว | Exception | เหตุผลเดียวกัน |
| สถานะที่ไม่ถูกต้องอาจกลับมาเป็นข้อมูลและถูกถอดรหัสเป็นข้อความ | สถานะอื่นที่ไม่ใช่ `90 00` ทำให้ field นั้น field เดียวล้มเหลว ดู `Quality` | ไบต์สถานะต้องไม่กลายเป็นข้อความ |
| `addrLane` คือส่วนที่ 2 และ `addrRoad` คือส่วนที่ 3 ตามตำแหน่ง | property ที่มีป้ายกำกับจะมีค่าเฉพาะเมื่อส่วนนั้นขึ้นต้นด้วยป้ายภาษาไทยนั้น | โค้ดสาธารณะบอกลำดับไม่ตรงกัน และมีการเผยแพร่รูปแบบจากบัตรเพียงใบเดียว ถ้าไม่มีป้ายค่าจะคงอยู่ใน `RawParts` |

สำหรับการนับอายุ `BirthDate.AgeReferenceDate` ใช้ตามประมวลกฎหมายแพ่งและพาณิชย์ มาตรา 16 เมื่อไม่ทราบเดือนหรือวัน ค่านี้เป็นวันที่ใช้เริ่มนับ ไม่ใช่วันเกิด

ไม่มี `IsExpired` คำแปลภาษาอังกฤษของ พ.ร.บ.บัตรประจำตัวประชาชน พ.ศ. 2526 มีกฎว่าบัตรที่ยังไม่หมดอายุเมื่อผู้ถือบัตรอายุครบ 70 ปีใช้ได้ตลอดชีพ การเทียบวันที่อย่างเดียวจึงอาจผิด กฎนี้ยังไม่ได้ยืนยันกับตัวบทภาษาไทย

## ความแตกต่างอื่น ๆ

- **การเลือกสิ่งที่จะอ่าน:** `ReadOptions.Mode` เป็น `Public` โดยค่าเริ่มต้น 2.x ไม่มีตัวเลือกนี้ ดู `docs/privacy.md`
- **ไบต์ 0177-0178:** 2.x บางเวอร์ชันอ่านไบต์เหล่านี้พร้อมกับวันออกบัตรและวันหมดอายุ v3 อ่านเฉพาะในโหมด private เป็นค่าดิบใน `Private.MoiRaw0177` แหล่งข้อมูลสาธารณะตีความความหมายไม่ตรงกัน
- **ความยาว:** ที่อยู่อ่าน 160 ไบต์ ขณะที่ 2.x อ่าน 100 และรูปถ่ายอ่าน 5118 ขณะที่ 2.x อ่าน 5100 ค่าใหม่มาจากแหล่งข้อมูลสาธารณะที่ตรงกัน และโปรเจกต์นี้ยังไม่ได้ยืนยันกับบัตรจริง
- **รูปถ่าย:** รูปที่ไม่มีจุดเริ่มต้นและจุดสิ้นสุด JPEG ที่ถูกต้องจะไม่ถูกส่งให้เลย และ `PhotoFailure` บอกเหตุผล
- **Thread:** เมธอด `Async` รันการเรียก PC/SC แบบ blocking บน thread pool thread และไม่แตะ synchronization context ของคุณ ต้อง marshal event ของ monitor และ progress ไปที่ UI thread เอง
- **เครื่องอ่าน:** เมื่อระบุชื่อเครื่องอ่าน การอ่านจะไม่เปลี่ยนไปใช้เครื่องอื่น `ReadAsync(token)` เลือกเครื่องแรกที่มีบัตรที่อ่านได้ ถ้าใช้หลายเครื่องควรระบุชื่อ
- **การ log:** `ToString()` ของ type ข้อมูลและข้อความของ exception ไม่มีข้อมูลบัตร ให้ log สิ่งเหล่านี้แทนค่าใน field
- **การพิสูจน์ตัวตน:** การอ่านไม่ใช่การพิสูจน์ตัวตน และ v3 ไม่มี `IsVerified` ดู `docs/privacy.md`
