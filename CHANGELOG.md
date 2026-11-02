# ประวัติการเปลี่ยนแปลง

การเปลี่ยนแปลงของสาย 3.x สาย 2.x มี changelog ของตัวเองบน [`release/2.x`](https://github.com/chakphanu/ThaiNationalIDCard/tree/release/2.x)

## 3.0.0-preview.1 (ยังไม่เผยแพร่)

**Preview: ทุกอย่างในรุ่นนี้ยังไม่เคยทดสอบกับบัตรหรือเครื่องอ่านจริง** ลำดับการอ่านและโครงสร้าง field มาจากโค้ดสาธารณะและการให้เหตุผลจากโค้ดนั้น และการทดสอบรันกับตัวจำลอง ถ้าทำได้ โปรดรัน `thaiid-diagnose` ซึ่งตอนนี้ต้องรันจาก repository นี้เพราะยังไม่เผยแพร่: ดู `docs/hardware-test.md`

### การเปลี่ยนแปลงที่ไม่เข้ากันกับรุ่นเดิม

ตารางเทียบทีละ member อยู่ใน `docs/migration-v2-to-v3.md`

- **Target:** ตอนนี้คือ `net10.0`, `net8.0` และ `netstandard2.0` ส่วน `net40` ถึง `net461` อยู่ที่ `release/2.x` PCSC ขยับเป็น 7.0.1
- **ที่ถอดออก:** `ThaiIDCard`, `Personal`, `APDU_THAILAND_IDCARD` และ type ของมัน และ `IAPDU_THAILAND_IDCARD` ไม่มีชั้นความเข้ากันได้
- **จุดเริ่มต้นใหม่:** `ThaiIdCardReader` พร้อม `ReadAsync` ที่คืน `ThaiIdCardData` และ `ThaiIdCardMonitor`
- **ความล้มเหลวเป็น exception:** `readAll` ที่คืน null, null ที่ส่งให้ monitor handler และ `ErrorCode()`/`Error()` ถูกถอดออก แทนด้วย typed exception ภายใต้ `ThaiIdCardException` รวมถึง `ReadCanceledException`
- **Monitor ไม่อ่านบัตรอีกต่อไป** `CardInserted` ให้ชื่อเครื่องอ่านและ ATR แล้ว handler ของคุณเรียก `ReadAsync` เอง
- **Event ไม่ถูก marshal** event ของ monitor รันบน thread pool thread ทีละตัว
- **วันที่:** `ThaiCardDate` แทน `DateTime` ปีเกิด `0000` เป็น null ขณะที่ 2.x ให้ปีปัจจุบัน เดือนหรือวัน `00` เป็น null ขณะที่ 2.x ให้ 1 วันหมดอายุ `99999999` เป็น `IsLifetime` ขณะที่ 2.x ให้ `9456-12-31`
- **ที่อยู่:** property ที่มีป้ายกำกับจะมีค่าเฉพาะเมื่อส่วนนั้นมีป้ายภาษาไทยตรงกัน และ `RawParts` เก็บทุกส่วนไว้ `addrLane` และ `addrRoad` ที่อิงตำแหน่งถูกถอดออก
- **เพศ:** enum `CardSex` และ `SexRaw` แทนค่าข้อความ
- **รูปถ่าย:** `PhotoRaw` เปลี่ยนเป็น `GetPhoto()` ซึ่งคืนสำเนา `PhotoBitmap` ย้ายไปอยู่ในแพ็กเกจใหม่ `ThaiNationalIDCard.Windows` รูปที่ไม่มีจุดเริ่มต้นและจุดสิ้นสุด JPEG ที่ถูกต้องจะไม่ถูกส่งให้ และ `PhotoFailure` บอกเหตุผล
- **Member ที่ถอดออกโดยไม่มีตัวแทน:** `readCitizenid`, `Open`, `Close`, `Version`, `Info`, `Issue_Expire`, `ExpireString` และ `BirthdayYearString`

### ที่เพิ่มเข้ามา

- `ReadOptions.Mode`: `CardReadMode.Public` เป็นค่าเริ่มต้น และ `CardReadMode.PublicAndPrivate` เป็นทางเลือกที่ต้องระบุเอง โหมด private เพิ่มค่าที่ถือว่าเป็น Laser ID, ช่วง NHSO เก้าช่วง และช่วง MOI ดิบสี่ช่วง ค่าที่แหล่งข้อมูลสาธารณะตีความไม่ตรงกันจะคงเป็นค่าดิบและตั้งชื่อตามตำแหน่ง `ChipDataRaw` มีใน API แต่ไม่เคยมีค่า: guard ปฏิเสธคำสั่งข้อมูลชิปในทุกโหมด
- Guard คำสั่งที่อยู่ใต้ตรรกะการอ่าน ปฏิเสธคำสั่ง PIN, VERIFY, ลายนิ้วมือ และคำสั่งเขียน รวมถึงช่วง `1627-173D` และ `015A-0166` ในทุกโหมด
- `ThaiIdCardQuality`: `Ok`, `NotRead`, `Suspect` หรือ `Failed` ของแต่ละ field
- `ReadDiagnostics` และ `TraceEntry`: trace บนผลลัพธ์และบน exception ที่มีเฉพาะ enum และตัวเลข
- `ThaiIdCardMonitor`: เครื่องอ่านกี่เครื่องก็ได้ มีชื่อเครื่องอ่านในทุก event และมี `MonitorError`
- `IThaiIdCardReader` สำหรับ mock
- `thaiid-diagnose`: เครื่องมือที่พิมพ์ status word ความยาว และผ่านหรือไม่ผ่าน และไม่พิมพ์ข้อมูลบัตร ยังไม่เผยแพร่
- `ThaiNationalIDCard.Windows`: `ToImage()` และ `ToBitmapSource()` สำหรับไบต์ JPEG จาก `GetPhoto()` ทั้งสองตัวโยน `ArgumentException` เมื่อถอดรหัสไบต์เป็น JPEG ไม่ได้
- `samples/ConsoleSample` และ `samples/WinFormsSample` เขียนใหม่ และไม่มีข้อมูลจริง
- `docs/architecture.md`, `docs/migration-v2-to-v3.md`, `docs/privacy.md`, `docs/hardware-test.md` และ CI บน Linux และ Windows CI ผ่านทั้ง Windows และ Ubuntu ที่คอมมิต `573c999` โดยรันการทดสอบบน `net10.0` และ `net8.0` และบน `net48` เมื่อรันบน Windows การที่ CI ผ่านไม่ใช่หลักฐานด้านฮาร์ดแวร์

### ที่เปลี่ยนไป

ค่าเหล่านี้มาจากแหล่งข้อมูลสาธารณะ และโปรเจกต์นี้ยังไม่ได้ยืนยันกับบัตรจริง

- ที่อยู่อ่าน 160 ไบต์ ขณะที่ 2.x อ่าน 100
- รูปถ่ายอ่าน 5118 ไบต์ ขณะที่ 2.x อ่าน 5100 โดยเพิ่ม chunk `80 B0 15 67 02 00 12`
- ไบต์ `0177-0178` อ่านเฉพาะในโหมด private
- ATR มีผลแค่ลำดับการลอง P2 การอ่านแต่ละครั้งลองค่า P2 อีกค่าได้หนึ่งครั้ง โดยตัดสินจากว่าเลขประจำตัวประชาชนกลับมาครบ 13 หลักและเลขตรวจสอบตรงหรือไม่
- SW1 state machine รองรับ `90 00`, `61 xx`, `61 00` ในความหมาย 256, `61 xx` ต่อเนื่อง และ `6C xx` โดยส่งซ้ำหนึ่งครั้ง เครื่องอ่านที่ตอบตรงและเครื่องอ่านที่ตอบ `61 xx` ใช้โค้ดเส้นทางเดียวกัน
- สถานะอื่นที่ไม่ใช่ `90 00` ทำให้ field นั้นล้มเหลว และไบต์ของมันจะไม่ถูกถอดรหัสเป็นข้อความ
- การอ่านรันใน transaction โดยไม่มีการ sleep ข้างใน และการลองใหม่อยู่นอก transaction หน่วงเวลาคงที่ 1500 ms ถูกถอดออก
- `ToString()` ของ type ข้อมูลและข้อความของ exception ไม่มีข้อมูลบัตร

### ที่ยังไม่ได้ทำ

- ยังไม่ได้ทดสอบกับบัตรหรือเครื่องอ่านจริง `docs/hardware-test.md` แสดงรายการสิ่งที่มีแต่บัตรจริงเท่านั้นที่ตอบได้
- `CardProfile` เป็น internal ดังนั้น field ที่ย้ายตำแหน่งหรือ AID ใหม่ต้องออกรุ่นใหม่
- จุดยืนทางกฎหมายยังไม่ผ่านการตรวจจากทนายความ ดู `docs/privacy.md`
