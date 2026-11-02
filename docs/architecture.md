# สถาปัตยกรรม

ทุกอย่างในเอกสารนี้สร้างและทดสอบกับตัวจำลอง ยังไม่มีส่วนใดเคยรันกับบัตรจริง

เป้าหมายคือไลบรารีที่รับมือบัตรรุ่นใหม่ได้โดยไม่ต้องแก้โค้ดเท่าที่จะทำได้: พฤติกรรมของบัตรเป็นตัวตัดสินวิธีอ่าน และสิ่งที่ตัดสินแบบนั้นไม่ได้จะอยู่ในข้อมูล โครงสร้างที่ย้ายตำแหน่ง AID ใหม่ หรือการยืนยันตัวตนที่บังคับ ไม่สามารถจัดการอัตโนมัติได้และต้องออกรุ่นใหม่

## ชั้นของระบบ

```
Public    ThaiIdCardReader, IThaiIdCardReader, ThaiIdCardMonitor, models, ReadOptions, ReadDiagnostics, exceptions
Workflow  CardReadWorkflow, ReadRun, ReadPolicy
Parsing   CardDataParser, ThaiCardTextDecoder, JpegScanner, FieldValidators
Card      CardProfile, MoiProfile, NhsoProfile, AdmProfile, MoiCardCommands
Protocol  ApduExchange, ApduResponse
Hardware  IRawCardChannel, GuardedCardConnection, CommandPolicy, IReaderMonitor, ReaderSelector
          Hardware/Pcsc: PcscRawCardChannel, the only code that knows PCSC
```

ทุกอย่างที่อยู่ใต้ type สาธารณะเป็น `internal` `CardProfile` ก็เป็น internal ในเวอร์ชันแรกนี้ ดังนั้น field ที่ย้ายตำแหน่งหรือ AID ใหม่ต้องออกรุ่นใหม่จนกว่าจะเปิดเป็นสาธารณะ ซึ่งภายหลังเปิดได้โดยไม่เกิด breaking change

**กฎ: มีเพียง `Hardware/Pcsc` ที่รู้จัก PCSC** ไม่มี type ใน namespace อื่นอ้างถึง type ของ PCSC ทั้งใน signature และในเนื้อเมธอด และไม่มี signature สาธารณะใดเปิดเผย type ของ PCSC ความล้มเหลวของ PC/SC ออกจากชั้นนี้ในรูป `ThaiIdCardException`

`ArchitectureTests` บังคับกฎของชั้นบน assembly ที่ build แล้ว รวมถึง IL ของเนื้อเมธอด และมีการทดสอบควบคุมเพื่อแสดงว่าการสแกนพบสิ่งที่มองหาจริง นอกจากนี้ยังตรวจว่า:

- `Card` และ `Parsing` ไม่ขึ้นกับ `Hardware` หรือ `Protocol`
- `Workflow` ไม่มีวิธีรอใด ๆ นอกจาก delay ที่ inject เข้ามา
- ไลบรารีไม่มีวิธี log
- monitor ไม่มีวิธีอ่านบัตร

`ThaiNationalIDCard.Windows` แยกต่างหากและไม่ขึ้นกับสิ่งใดในชุดนี้ ทำหน้าที่แปลงไบต์ JPEG เป็น image type ของ Windows เท่านั้น

## ตัวคุมคำสั่ง (guard)

`GuardedCardConnection` เป็น sealed และเป็นทางเดียวในการส่งคำสั่ง `ApduExchange` ไม่รับอย่างอื่น ดังนั้น PC/SC และตัวจำลองจึงอยู่ใต้ guard เดียวกัน และทุกการทดสอบใช้ guard ตัวจริง guard ติดตาม applet ที่เลือกอยู่และดูว่าต้องส่ง GET RESPONSE หรือไม่ เพราะช่วงของ READ เป็นของ applet ใด applet หนึ่ง: `015D` เป็น field ของ NHSO แต่อยู่ในช่วงต้องห้ามของ MOI

`CommandPolicy` เป็น allowlist ที่ไม่มีวิธีเพิ่มคำสั่งดิบ

| โหมด | ที่อนุญาต |
|---|---|
| `Public` | SELECT ของ MOI, GET RESPONSE `00 C0 00 {00,01}` เมื่อถึงกำหนด, `80 B0` ภายในช่วง public ของ profile |
| `PublicAndPrivate` | เพิ่ม SELECT ของ NHSO และ ADM, ช่วง private ของ MOI, ช่วงของ NHSO และ `80 00 00 00 07` ใน ADM |
| ปฏิเสธในทุกโหมด | `80 20`, `80 B4`, `B0 34`, คำสั่ง ISO สำหรับการเขียนและ reference data, SELECT ที่ไม่มี AID, `80 CA` และทุกอย่างที่ไม่อยู่ในรายการ |

การส่งซ้ำหลัง `6C xx` ผ่าน guard เหมือนคำสั่งอื่น บัตรจึงขยายการอ่านเกินช่วงที่ประกาศไว้ไม่ได้ profile ก็ขยายส่วนที่ต้องห้ามไม่ได้เช่นกัน: ช่วงที่อ่านได้ที่ทับกับช่วงที่ห้ามอ่านจะล้มเหลวตั้งแต่ตอนสร้าง profile ก่อนจะแตะบัตร

## ลำดับการอ่าน

1. แสดงรายการเครื่องอ่านพร้อมสถานะและเลือกหนึ่งเครื่อง `ThaiIdCardReader` อ่านได้ครั้งละหนึ่งการอ่านต่อเครื่องอ่านภายใน process เดียวกัน
2. เชื่อมต่อในโหมด Shared การลองใหม่เกิดนอก transaction หลังหน่วงเวลา สำหรับ `CardReset` และ `SharingViolation` และสำหรับ `CardRemoved` เฉพาะระหว่างเชื่อมต่อ ไม่มีการลองใหม่หลัง timeout และหน่วงเวลาคงที่ 1500 ms ของ 2.x ถูกถอดออก
3. เปิด transaction ข้างในไม่มีการ sleep เพราะ Windows จะ reset บัตรเมื่อ transaction ว่างนาน 5 วินาที
4. SELECT MOI ค่าอื่นนอกจาก `90 00` หรือ `61 xx` จะโยน `UnsupportedCardException` หลัง `61 xx` จะส่ง GET RESPONSE และทิ้งไบต์โดยไม่อ่าน
5. ด่านตรวจ CID, `80 B0 00 04 02 00 0D`:
   - ครบ 13 หลักและเลขตรวจสอบตรง: ล็อก P2 นี้
   - ครบ 13 หลักแต่เลขตรวจสอบไม่ตรง: SELECT ใหม่แล้วอ่านซ้ำด้วย P2 เดิมหนึ่งครั้ง ถ้ายังไม่ตรงให้อ่านต่อและตั้ง field เป็น `Suspect`
   - กรณีอื่น: SELECT ใหม่แล้วลอง P2 อีกค่า ได้หนึ่งครั้งต่อการอ่าน ถ้าไม่เคยส่ง GET RESPONSE แสดงว่า P2 ไม่เกี่ยวข้อง จึงลองอ่านแบบเดิมอีกหนึ่งครั้งแทน ถ้ายังล้มเหลวจะโยน `UnknownReadBehaviourException`
6. อ่าน field ตาม profile สถานะอื่นที่ไม่ใช่ `90 00` ทำให้ field นั้นล้มเหลว และไบต์ของมันจะไม่ถูกถอดรหัสเป็นข้อความ
7. ในโหมด private อ่านช่วง private ของ MOI แล้ว SELECT NHSO และ ADM ความล้มเหลวตรงนี้กระทบเฉพาะ field เหล่านั้น
8. ปิด transaction ใน `finally`

`ApduExchange` คือ SW1 state machine: `90 00` จบ, `61 xx` ส่ง GET RESPONSE (`00` หมายถึง 256) ได้ไม่เกินจำนวนรอบที่กำหนด, `6C xx` ส่งซ้ำหนึ่งครั้ง และค่าอื่นคืนสถานะ ด้วยวิธีนี้เครื่องอ่านที่ตอบตรงและเครื่องอ่านที่ตอบ `61 xx` จึงใช้โค้ดเส้นทางเดียวกันโดยไม่ต้องรู้รุ่นเครื่องอ่าน

ATR มีผลแค่ลำดับการลอง P2: `3B 67` ให้ `[01, 00]` และค่าอื่นให้ `[00, 01]` ATR ที่สั้นถือเป็นเรื่องปกติ และ trace จะบันทึกเมื่อมีการใช้กฎนี้ ในหลักฐานสาธารณะไม่มีบัตรไทยที่แสดง `3B 67` ที่ยังคงกฎนี้ไว้เพราะไม่มีบัตรมาพิสูจน์ว่าเอาออกแล้วปลอดภัย

## การแปลงข้อมูล

- หารูปถ่ายโดยไล่ตาม JPEG marker ไม่ใช่การตัดส่วนท้ายทิ้ง เพราะ `0x20` อาจเป็นข้อมูลภาพ ถ้าไม่มี marker เริ่มต้นหรือสิ้นสุดจะไม่มีรูปและมี `PhotoFailure`
- ส่วนของที่อยู่ที่มีป้ายกำกับ เช่น `Road` จะมีค่าเฉพาะเมื่อส่วนนั้นขึ้นต้นด้วยป้ายภาษาไทยนั้น `RawParts` เก็บทุกส่วนไว้
- `ThaiCardDate` ไม่เติมค่าให้ `AgeReferenceDate` แยกต่างหากและใช้สำหรับนับอายุเท่านั้น
- `Sex` ถูกตัดออกมาเป็นไบต์ก่อนการถอดรหัส และไม่เดาจากคำนำหน้าชื่อ

## การวินิจฉัย

`TraceEntry` เป็น readonly struct ของ enum และตัวเลข โครงสร้างจึงเก็บข้อมูลบัตรไม่ได้ และมีการทดสอบด้วย reflection ตรวจเรื่องนี้ trace เดินทางไปกับผลลัพธ์และ exception และไม่มีสถานะค้างระหว่างการอ่านแต่ละครั้ง

## การเฝ้าดูเครื่องอ่าน

`ThaiIdCardMonitor` รัน PC/SC monitor หนึ่งตัวต่อเครื่องอ่าน เครื่องอ่านที่หายไปหนึ่งเครื่องจึงไม่หยุดเครื่องอื่น thread ของ monitor ทำแค่เข้าคิว: โค้ดของผู้เรียกใช้รันบน thread pool thread ทีละ event มันรายงาน ATR และไม่เคยเชื่อมต่อกับบัตร

## จุดที่ต้องแก้เมื่อได้ข้อค้นพบจากบัตรจริง

ข้อค้นพบส่วนใหญ่ควรแก้แค่ข้อมูลหรือ policy

| ข้อค้นพบ | สิ่งที่ต้องแก้ |
|---|---|
| พฤติกรรม P2, จำนวนการลองใหม่และการหน่วงเวลา, จะใช้ transaction หรือไม่, จะ drain SELECT หรือไม่ | `ReadPolicy` (`Workflow/ReadPolicy.cs`) |
| field ย้ายตำแหน่ง, ความยาวต่างไป, AID ใหม่, ช่วงที่ต้องอ่านหรือเลิกอ่าน | `MoiProfile`, `NhsoProfile`, `AdmProfile` (`Card/CardProfile.cs`) |
| คำตอบของ Laser มีลักษณะอย่างไร | ค่าคงที่ของ `AdmProfile` ที่ทำเครื่องหมาย `ASSUMPTION` |
| status word ที่ต้องจัดการแบบใหม่ | `ApduExchange` พร้อม scenario ในตัวจำลอง |
| ค่าที่ยืนยันความหมายได้แล้ว | property ใหม่ใน model และใน `PublicAPI.Unshipped.txt` |

การทดสอบที่ผูกพฤติกรรมกับข้อเท็จจริงของบัตรจะระบุไว้ golden sequence ใน `tests/ThaiNationalIDCard.Tests/Golden` บอกว่า 2.x ส่งอะไร ไม่ได้บอกว่าบัตรต้องการอะไร ข้อค้นพบจากบัตรจริงจึงอาจเปลี่ยนสิ่งเหล่านี้ได้อย่างถูกต้อง

คำถามที่ยังเปิดอยู่มีใน `docs/hardware-test.md`
