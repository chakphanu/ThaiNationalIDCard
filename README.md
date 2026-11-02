# ThaiNationalIDCard

ไลบรารีช่วยอ่านบัตรประจำตัวประชาชนไทย (Smartcard) สำหรับ .NET Framework

## สถานะโปรเจกต์

- `release/2.x` เป็นสายสำหรับความเข้ากันได้: แก้บั๊กเท่านั้น สำหรับ .NET Framework ดู `docs/maintenance.md` และ `CHANGELOG.md`
- รุ่น 2.0.1 ใน branch นี้ยังไม่เผยแพร่ รุ่นล่าสุดบน NuGet ยังเป็น 2.0.0 ซึ่งไม่มีการแก้ไขตาม `CHANGELOG.md`
- v3 พัฒนาอยู่ใน branch `v3` เป็น preview ยังไม่เผยแพร่ และยังไม่เคยทดสอบกับบัตรจริง
- Repository นี้เลิกสถานะ archive เมื่อวันที่ 2 พฤศจิกายน 2026
- Fork ของชุมชน ขอบคุณชุมชน .NET Thailand: https://github.com/dotnetthailand/ThaiNationalIDCard

## ขอบเขตของหลักฐาน

CI ของ `release/2.x` ผ่านบน Windows และ Ubuntu ที่คอมมิต `c488da3` งานบน Ubuntu ทำแค่ build ไลบรารี การทดสอบรันบน Windows การที่ CI ผ่านไม่ใช่หลักฐานด้านฮาร์ดแวร์:

- การทดสอบใช้เครื่องอ่าน PC/SC จำลองและข้อมูลสังเคราะห์ ไม่แตะฮาร์ดแวร์จริง
- การทดสอบรัน build `net461` ของไลบรารีบน .NET runtime (`net8.0`) ไม่ใช่บน .NET Framework
- ไม่มีการทดสอบที่เรียก `MonitorStart` จริง

เอกสารนี้ไม่ได้อ้างว่าใช้ได้กับบัตรหรือเครื่องอ่านรุ่นใด

## การติดตั้ง

รันคำสั่งนี้ใน Package Manager Console:

```
PM> Install-Package ThaiNationalIDCard
```

ตอนนี้คำสั่งนี้จะได้รุ่น 2.0.0

## การใช้งาน

```csharp
using ThaiNationalIDCard;
...
...
public void readCard()
{
            ThaiIDCard idcard = new ThaiIDCard();
            Personal personal = idcard.readAll();
            if (personal != null)
            {
                Console.WriteLine(personal.Citizenid);
                Console.WriteLine(personal.Birthday.ToString("dd/MM/yyyy"));
                Console.WriteLine(personal.Sex);
                Console.WriteLine(personal.Th_Prefix);
                Console.WriteLine(personal.Th_Firstname);
                Console.WriteLine(personal.Th_Lastname);
                Console.WriteLine(personal.En_Prefix);
                Console.WriteLine(personal.En_Firstname);
                Console.WriteLine(personal.En_Lastname);
                Console.WriteLine(personal.Issue.ToString("dd/MM/yyyy")); // วันออกบัตร
                Console.WriteLine(personal.Expire.ToString("dd/MM/yyyy")); // วันหมดอายุ

                Console.WriteLine(personal.Address);
                Console.WriteLine(personal.addrHouseNo); // บ้านเลขที่
                Console.WriteLine(personal.addrVillageNo); // หมู่ที่
                Console.WriteLine(personal.addrLane); // ซอย
                Console.WriteLine(personal.addrRoad); // ถนน
                Console.WriteLine(personal.addrTambol);
                Console.WriteLine(personal.addrAmphur);
                Console.WriteLine(personal.addrProvince);
            }
            else if (idcard.ErrorCode() > 0)
            {
                Console.WriteLine(idcard.Error());
            }
}            

```

## เครดิต

- คำสั่ง APDU จาก Mr.Manoi: http://hosxp.net/index.php?option=com_smf&topic=22496
- บัตรไทยประเภท 3B 78 ดูใน wiki: https://github.com/kolry/ThaiNationalIDCard/wiki/_pages
