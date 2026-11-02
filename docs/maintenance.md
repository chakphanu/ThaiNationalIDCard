# นโยบายการบำรุงรักษา release/2.x

Branch นี้ (`release/2.x`) เป็นสายสำหรับความเข้ากันได้ของไลบรารี 2.x ส่วน v3 พัฒนาแยกต่างหาก หน้านี้อธิบายเฉพาะสิ่งที่เกิดขึ้นบน branch `release/2.x`

## นโยบาย

- แก้บั๊กเท่านั้น ไม่เพิ่มฟีเจอร์
- เข้ากันได้กับ 2.0.0 ทั้งในระดับ source และพฤติกรรม: ไม่เปลี่ยนชื่อ ไม่เปลี่ยน API ไม่ถอด member ชนิดค่าที่คืนเหมือนเดิม ชื่อ property เหมือนเดิม และเนื้อหาสตริงที่ถอดรหัสเหมือนเดิม
- .NET Framework เท่านั้น ไลบรารี target net40, net45, net452, net46 และ net461 (ใช้ WPF และ System.Drawing)
- Dependency: PCSC 5.0.0 และ PCSC.Iso7816 5.0.0 เป็น NuGet package reference
- การเขียนใหม่ขนาดใหญ่ (netstandard, API ใหม่, Dispose pattern) อยู่นอกขอบเขตของสายนี้

## รุ่น 2.0.1

รุ่นแก้บั๊ก ยังไม่เผยแพร่ รุ่นล่าสุดบน NuGet ยังเป็น 2.0.0 รายการดูที่ `CHANGELOG.md` หมายเหตุเรื่องระบบ build:

- โปรเจกต์ multi-target แบบ SDK-style (`ThaiNationalIDCard/ThaiNationalIDCard.csproj`) configuration เดิม `release-40/45/...` เปลี่ยนแค่ output path และไม่มีอีกแล้ว ทุก target build ด้วย `Release`
- แพ็กเกจ NuGet ตอนนี้มี `lib/net40`, `lib/net45`, `lib/net452`, `lib/net46` และ `lib/net461` และขึ้นกับ PCSC 5.0.0 แพ็กเกจ 2.0.0 มีแค่ `lib/net40` ขึ้นกับ PCSC 3.7.0 และรวม `pcsc-sharp.dll` รุ่นเก่ามาด้วย
- AssemblyVersion ยังเป็น `2.0.0.0` (binding redirect ยังใช้ได้) เวอร์ชันของแพ็กเกจและไฟล์คือ 2.0.1 `ThaiIDCard.Version()` รายงานเวอร์ชันของ assembly

## คำถามจาก issue tracker

**ใช้กับ .NET Framework 3.5 ได้ไหม (#6)** ไม่ได้ PCSC 5.0.0 เริ่มที่ net40 นั่นจึงเป็น target ต่ำสุดของ 2.x

**ทำไมถึงเจอ "'TIS-620' is not a supported encoding name" (#7)** ข้อผิดพลาดนี้เกิดบน .NET Core สาย 2.x target เฉพาะ .NET Framework สำหรับ .NET รุ่นใหม่ให้ดู v3 ซึ่งตอนนี้เป็น preview ใน branch `v3` ยังไม่เผยแพร่ และยังไม่เคยทดสอบกับบัตรจริง

**อ่านศาสนาหรือเลขคำขอใต้รูปถ่ายได้ไหม (#12)** ไลบรารีนี้อ่านไม่ได้ ใน #12 ผู้ดูแลตอบว่าบัตรไม่เก็บศาสนา แล้วเสริมภายหลังว่าบัตรบางเวอร์ชันอาจเก็บ ซึ่งต้องหาตัวอย่างบัตรมาไล่อ่านทุก address ส่วนตำแหน่งของเลขคำขอไม่ทราบ

**ทำไมรูปถ่ายเสียเมื่อฉันต่อ 20 chunk เอง (#15)** คำตอบ APDU แต่ละครั้งลงท้ายด้วยไบต์สถานะ 2 ไบต์ (`90 00` เมื่อสำเร็จ) ซึ่งไม่ใช่ข้อมูลภาพ ถ้าต่อคำตอบเองต้องตัดออก หรือใช้ `Personal.PhotoRaw` จาก `readAllPhoto()` ซึ่งมีแต่ไบต์ของภาพอยู่แล้ว

**จะรู้ได้อย่างไรว่า event มาจากเครื่องอ่านไหน (#18)** Event ของ 2.x ไม่มีชื่อเครื่องอ่าน ให้ใช้ `ThaiIDCard` หนึ่ง instance ต่อเครื่องอ่าน เก็บ reference ของ instance ไว้ และจับชื่อไว้ใน handler ใน branch `v3` event มี `ReaderName` แล้ว แต่ v3 ยังเป็น preview ที่ไม่เผยแพร่และไม่เคยทดสอบกับเครื่องอ่านจริง

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

ตัวอย่างนี้เรียก `MonitorStart` ซึ่งไม่มีการทดสอบครอบคลุมบน 2.x

**ต้องใช้แพ็กเกจ NuGet ตัวไหน (#20)** ตั้งแต่ 2.0.1 เป็นต้นไป ติดตั้งแค่ `ThaiNationalIDCard` NuGet จะติดตั้ง PCSC 5.0.0 และ PCSC.Iso7816 5.0.0 เป็น dependency ให้ รุ่น 2.0.1 ยังไม่เผยแพร่ แพ็กเกจ 2.0.0 ที่เผยแพร่อยู่ขึ้นกับ PCSC 3.7.0

## การ build ทดสอบ และสร้างแพ็กเกจ

```
dotnet build ThaiNationalIDCard.sln -c Release
dotnet test tests/ThaiNationalIDCard.Tests/ThaiNationalIDCard.Tests.csproj -c Release
dotnet pack ThaiNationalIDCard/ThaiNationalIDCard.csproj -c Release -o artifacts
```

บน Linux หรือ macOS ให้ตั้ง `EnableWindowsTargeting=true` และ build เฉพาะไลบรารี target ของ .NET Framework คอมไพล์กับ `Microsoft.NETFramework.ReferenceAssemblies` ได้ แต่รันบนระบบเหล่านั้นไม่ได้

## การทดสอบ

`tests/ThaiNationalIDCard.Tests` (xUnit, net8.0) โหลด build `net461` ของไลบรารีบน .NET runtime รุ่นใหม่ ไม่ใช่บน .NET Framework และขับด้วยเครื่องอ่าน PC/SC จำลองผ่าน internal constructor (`InternalsVisibleTo`) ใช้ข้อมูลบัตรสังเคราะห์เท่านั้น การทดสอบไม่แตะฮาร์ดแวร์จริง และไม่ครอบคลุมพฤติกรรมบนบัตรจริง ให้ตรวจกับเครื่องอ่านจริงด้วยมือก่อนออกรุ่น

ไม่มีการทดสอบที่เรียก `MonitorStart` จริง การทดสอบของ monitor เรียก private callback โดยตรง

.NET 8 จะหมดระยะการสนับสนุนในวันที่ 10 พฤศจิกายน 2026 โปรเจกต์ทดสอบ target `net8.0` และตั้ง `RollForward` เป็น `Major`

## การทำงานของ CI

`.github/workflows/ci.yml` build, test และ pack บน `windows-latest` และ build ไลบรารีเพื่อตรวจไขว้บน `ubuntu-latest` ไม่ได้เผยแพร่อะไร การเผยแพร่แพ็กเกจเป็นขั้นตอนที่ทำด้วยมือ

CI ผ่านบน Windows และ Ubuntu ที่คอมมิต `c488da3` การที่ CI ผ่านไม่ใช่หลักฐานด้านฮาร์ดแวร์

## รายการตรวจก่อนออกรุ่น

1. อัปเดต `CHANGELOG.md` และ `Version` ใน `ThaiNationalIDCard/ThaiNationalIDCard.csproj`
2. CI ผ่านบน `release/2.x`
3. Smoke test กับเครื่องอ่านและบัตรจริง (อ่าน, อ่านพร้อมรูปถ่าย, event เสียบและดึงบัตร)
4. ดาวน์โหลด artifact `nupkg` ตรวจสอบ (`lib/net40` ... `lib/net461`, dependency PCSC 5.0.0) แล้วเผยแพร่ด้วยมือ
