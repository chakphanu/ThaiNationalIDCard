# ThaiNationalIDCard

ไลบรารี .NET สำหรับอ่านบัตรประจำตัวประชาชนไทยผ่าน PC/SC โดยผู้เรียกใช้เลือกได้ว่าจะอ่านข้อมูลมากน้อยเพียงใด

## สถานะ

**3.0.0-preview ยังไม่เคยทดสอบกับบัตรจริง** โปรเจกต์นี้ยังไม่มีเครื่องอ่านหรือบัตรสำหรับทดสอบ ลำดับการอ่าน โครงสร้าง field และการจัดการข้อผิดพลาดมาจากโค้ดสาธารณะและการให้เหตุผลจากโค้ดนั้น การทดสอบรันกับตัวจำลอง จึงแสดงได้ว่าโค้ดตรงกับสมมติฐาน ไม่ได้แสดงว่าสมมติฐานถูกต้อง เวอร์ชันจะอยู่ที่ `3.0.0-preview.N` จนกว่าบัตรจริงจะยืนยันสมมติฐานเหล่านั้น และ README นี้ไม่ได้อ้างว่าใช้ได้กับบัตรหรือเครื่องอ่านรุ่นใด คุณช่วยได้: ดู[การวินิจฉัย](#การวินิจฉัย)

| สาย | Branch | สำหรับ |
|---|---|---|
| 3.x preview | `v3` | `net10.0`, `net8.0`, `netstandard2.0` |
| 2.x | [`release/2.x`](https://github.com/chakphanu/ThaiNationalIDCard/tree/release/2.x) | ความเข้ากันได้กับ .NET Framework 4.0 ถึง 4.6.1 แก้บั๊กเท่านั้น |

v3 เขียนใหม่และมี breaking change: ดู[คู่มือการย้ายเวอร์ชัน](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/migration-v2-to-v3.md)

.NET 8 จะหมดระยะการสนับสนุนในวันที่ 10 พฤศจิกายน 2026

Repository นี้เลิกสถานะ archive เมื่อวันที่ 2 พฤศจิกายน 2026 ยังไม่มีการเผยแพร่แพ็กเกจรุ่น 2.0.1 หรือ 3.x และยังไม่ได้เผยแพร่ `thaiid-diagnose`

## การติดตั้ง

v3 ยังไม่มีบน NuGet ตอนนี้จึงต้องเพิ่ม project reference ไปที่ `src/ThaiNationalIDCard` เมื่อเผยแพร่ preview แล้วจึงใช้คำสั่งนี้ได้:

```
dotnet add package ThaiNationalIDCard --version 3.0.0-preview.1
```

## เริ่มต้นใช้งาน: โหมด public

ค่าเริ่มต้นคือโหมด public ซึ่งอ่านเฉพาะ field ด้านล่างเท่านั้น และอ่านรูปถ่ายเมื่อขอเท่านั้น

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

- ค่าที่ได้คือสิ่งที่บัตรส่งมา `card.Quality` บอกสถานะของแต่ละ field ว่าเป็น `Ok`, `Suspect`, `Failed` หรือ `NotRead`
- `ThaiCardDate` เก็บ `Raw` ไว้ ส่วน `Year`, `Month` และ `Day` เป็น null เมื่อบัตรเก็บค่า `00` หรือ `0000` ไลบรารีไม่เติมค่าให้ `ExpiryDate.IsLifetime` เป็น true เมื่อค่าคือ `99999999`
- `IsValidCitizenId` ตรวจความยาว ตัวเลข และเลขตรวจสอบ ไม่ได้ตรวจอย่างอื่น
- `ToString()` ของ type เหล่านี้และข้อความของ exception ไม่มีข้อมูลบัตร การ log ค่าเหล่านี้จึงไม่ทำให้ข้อมูลบัตรรั่ว
- `ReadAsync` ใช้ thread pool thread ตลอดการอ่าน เพราะ PC/SC เป็นการเรียกแบบ blocking

## เริ่มต้นใช้งาน: โหมด private

โหมด private เป็นทางเลือกแยกต่างหากที่ต้องระบุเอง อ่าน[หัวข้อความยินยอม](#ความยินยอมและกฎหมาย)ก่อน

```csharp
ThaiIdCardData full = await reader.ReadAsync(
    name,
    new ReadOptions { Mode = CardReadMode.PublicAndPrivate },
    cancellationToken);

ThaiIdCardPrivateData extra = full.Private!;
FieldQuality laserQuality = extra.LaserIdQuality;
byte[]? raw0177 = extra.MoiRaw0177.GetBytes();
```

ค่าที่แหล่งข้อมูลสาธารณะตีความไม่ตรงกันจะเป็นไบต์ดิบและตั้งชื่อตามตำแหน่ง เช่น `MoiRaw0177` ถ้า field ในโหมด private อ่านไม่สำเร็จ หรือบัตรไม่มี applet นั้น จะแสดงเป็น quality ของ field นั้นเท่านั้น และไม่กระทบ field ของโหมด public [docs/privacy.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md) มีรายการ field ทั้งหมด

## การเฝ้าดูเครื่องอ่าน

`ThaiIdCardMonitor` เฝ้าดูเครื่องอ่านกี่เครื่องก็ได้ และทำหน้าที่แจ้งเตือนเท่านั้น ไม่เคยเชื่อมต่อกับบัตร การอ่านจึงเป็นการตัดสินใจของโค้ดของคุณ ทุก event มีชื่อเครื่องอ่าน

```csharp
using var monitor = new ThaiIdCardMonitor(reader.GetReaderNames());
monitor.CardInserted += (sender, e) => _ = ReadAndShowAsync(e.ReaderName);
monitor.CardRemoved += (sender, e) => ClearScreen(e.ReaderName);
monitor.MonitorError += (sender, e) => ReportProblem(e.ReaderName, e.Failure);
monitor.Start();
```

Event มาบน thread pool thread ทีละตัว UI ต้อง marshal เอง เช่น ด้วย `Control.BeginInvoke` Exception จาก handler จะถูกทิ้ง จึงต้องดักจับเอง บัตรที่อยู่ในเครื่องอ่านตอนเรียก `Start()` ก็ทำให้เกิด `CardInserted` เช่นกัน เครื่องอ่านที่เสียบภายหลังจะยังไม่ถูกเฝ้าดูจนกว่าจะเรียก `Start()` อีกครั้ง

พฤติกรรมเหล่านี้ทดสอบกับตัวจำลองเท่านั้น ยังไม่เคยรันกับเครื่องอ่านจริง

## ข้อผิดพลาด

| Exception | ความหมาย |
|---|---|
| `SmartCardServiceUnavailableException` | ไม่มีบริการ PC/SC `NativeLibraryMissing` บอกว่าโหลด native library ไม่ได้หรือไม่ |
| `ReaderNotFoundException` | ไม่มีเครื่องอ่าน หรือไม่มีเครื่องที่ระบุชื่อ |
| `CardNotPresentException` | เครื่องอ่านไม่มีบัตรที่ใช้ได้ |
| `CardRemovedException` | บัตรถูกดึงออกระหว่างการอ่าน |
| `UnsupportedCardException` | เครื่องอ่านสื่อสารกับบัตรไม่ได้ หรือบัตรปฏิเสธ applet |
| `UnknownReadBehaviourException` | ไม่ได้เลขประจำตัวประชาชนจากความพยายามทุกครั้งที่อนุญาต trace นี้คือสิ่งที่รุ่นถัดไปต้องการ |
| `CardCommunicationException` | ความล้มเหลวอื่น ๆ `Failure` บอกว่าเป็นแบบใด `StatusWord` ให้สถานะเมื่อมี |
| `ReadCanceledException` | การอ่านถูกยกเลิก เป็น `OperationCanceledException` |

ทุกตัวยกเว้นตัวสุดท้ายสืบทอดจาก `ThaiIdCardException` `Diagnostics` บน exception และบนผลลัพธ์เก็บ trace ได้แก่ status word ความยาว และผ่านหรือไม่ผ่าน โดยไม่มีข้อมูลบัตร

## แพลตฟอร์ม

แพ็กเกจนี้ใช้ [PCSC](https://www.nuget.org/packages/PCSC) ผ่านบริการ PC/SC ของระบบ

CI ผ่านบน Windows และ Ubuntu ที่คอมมิต `573c999` โดยรันการทดสอบบน `net10.0` และ `net8.0` และบน `net48` เมื่อรันบน Windows CI ใช้ตัวจำลองและไม่มีเครื่องอ่าน การที่ CI ผ่านจึงไม่ใช่หลักฐานด้านฮาร์ดแวร์ และตารางด้านล่างไม่ได้บอกอะไรเกี่ยวกับบัตร

| OS | สิ่งที่ต้องมี |
|---|---|
| Windows | `winscard.dll` เป็นส่วนหนึ่งของ Windows บริการ Smart Card ต้องทำงานอยู่: `net start SCardSvr` |
| Linux | `pcscd` และ libpcsclite: `sudo apt install pcscd libpcsclite1` (Debian, Ubuntu) หรือ `sudo dnf install pcsc-lite` (Fedora) จากนั้น `sudo systemctl start pcscd` |
| macOS | แพ็กเกจ PCSC เรียก `PCSC.framework` ของระบบ จึงน่าจะไม่ต้องติดตั้งอะไรเพิ่ม แต่ยังไม่เคยรันบน macOS |

`ThaiNationalIDCard.Windows` เป็นแพ็กเกจเล็ก ๆ สำหรับ Windows เท่านั้น แปลงไบต์จาก `GetPhoto()` เป็น `System.Drawing.Image` หรือ WPF `BitmapSource` ด้วย `card.GetPhoto()?.ToImage()` และ `card.GetPhoto()?.ToBitmapSource()`

ตัวอย่างอยู่ใน `samples/` โดยไม่มีข้อมูลจริง: `ConsoleSample` (`net10.0`) ไม่ผูกกับ OS และปิดบังเลขประจำตัวประชาชนเป็นค่าเริ่มต้น ส่วน `WinFormsSample` (`net10.0-windows`) แสดงการใช้ monitor, การ marshal, รูปถ่าย และโหมด private แบบต้องเลือกเปิดเอง

## การวินิจฉัย

`thaiid-diagnose` อ่านบัตรแล้วพิมพ์สิ่งที่เกิดขึ้น ได้แก่ status word ความยาว ค่า P2 ที่ใช้ และผ่านหรือไม่ผ่านของแต่ละ field เครื่องมือนี้ไม่มีตัวเลือกที่พิมพ์ข้อมูลบัตร ผลลัพธ์นี้คือสิ่งที่จะทำให้โปรเจกต์ออกจากสถานะ preview ได้ เครื่องมือยังไม่เผยแพร่ จึงต้องรันจาก repository นี้

```
dotnet run --project tools/ThaiNationalIDCard.Diagnose -c Release -- --mode public
```

ใช้กับบัตรของตัวเองหรือเมื่อผู้ถือบัตรยินยอมเท่านั้น สิ่งที่ต้องรันและสิ่งที่ต้องส่งกลับอยู่ใน [docs/hardware-test.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/hardware-test.md)

## ความยินยอมและกฎหมาย

*เขียนเมื่อพฤศจิกายน 2026 ข้อความนี้ไม่ใช่คำแนะนำทางกฎหมาย และยังไม่ผ่านการตรวจจากทนายความ*

- **คุณเป็นผู้รับผิดชอบเรื่องความยินยอม** ไลบรารีนี้เป็นเครื่องมือตัวกลาง ตามการตัดสินใจของเจ้าของโปรเจกต์ ผู้ที่นำไปใช้เป็นผู้รับผิดชอบความยินยอมของผู้ถือบัตรและกฎหมายที่เกี่ยวข้อง และเป็นผู้เลือกโหมดในการอ่านแต่ละครั้ง จุดยืนนี้เป็นการตัดสินใจของโปรเจกต์ ไม่ใช่ข้อสรุปทางกฎหมาย
- พ.ร.บ.บัตรประจำตัวประชาชน พ.ศ. 2526 มาตรา 12 กำหนดโทษอาญาสำหรับการเข้าถึงหรือเปิดเผยข้อมูลในหน่วยความจำของบัตร ที่ไม่ใช่ข้อมูลทั่วไปที่ปรากฏบนบัตร โดยไม่ได้รับความยินยอมจากผู้ถือบัตร พ.ร.บ.คุ้มครองข้อมูลส่วนบุคคล พ.ศ. 2562 อาจใช้บังคับด้วย และมาตรา 26 ระบุว่าข้อมูลศาสนาและสุขภาพเป็นข้อมูลอ่อนไหว
- โหมด public อยู่ตรงไหนเมื่อเทียบกับเส้นแบ่งตามมาตรา 12 เป็นการอนุมาน ไม่ใช่ข้อค้นพบ
- **การอ่านบัตรไม่ใช่การพิสูจน์ตัวตน** การอ่านสำเร็จและเลขตรวจสอบถูกต้องแสดงเพียงว่ามีบัตรตอบกลับ ไลบรารีตั้งใจไม่มี `IsVerified` หรือ `IsGenuine`
- คำสั่ง PIN และ VERIFY คำสั่งลายนิ้วมือ คำสั่งเขียน และบางพื้นที่ของชิป ถูกปฏิเสธในทุกโหมดโดย guard ที่อยู่ใต้ตรรกะการอ่าน

แหล่งอ้างอิงและประเด็นที่ยังเปิดอยู่มีใน [docs/privacy.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md)

## เอกสาร

- [สถาปัตยกรรม](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/architecture.md)
- [การย้ายจาก 2.x](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/migration-v2-to-v3.md)
- [ความเป็นส่วนตัวและกฎหมาย](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md)
- [การทดสอบกับฮาร์ดแวร์](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/hardware-test.md)
- [ประวัติการเปลี่ยนแปลง](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/CHANGELOG.md)
- [คำสั่ง APDU](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/APDU.md)

## เครดิต

- คำสั่ง APDU ชุดแรกมาจาก Mr.Manoi: <http://hosxp.net/index.php?option=com_smf&topic=22496>
- บัตรประเภท 3B 78 มาจาก [kolry wiki](https://github.com/kolry/ThaiNationalIDCard/wiki/_pages)
- ขอบคุณชุมชน .NET Thailand และ fork ของพวกเขาที่ [dotnetthailand/ThaiNationalIDCard](https://github.com/dotnetthailand/ThaiNationalIDCard)

สัญญาอนุญาต BSD 2-Clause: ดู [LICENSE](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/LICENSE)
