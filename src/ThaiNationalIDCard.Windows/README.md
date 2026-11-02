# ThaiNationalIDCard.Windows

Helper สำหรับ Windows ที่ใช้กับรูปถ่ายซึ่ง [ThaiNationalIDCard](https://github.com/chakphanu/ThaiNationalIDCard/tree/v3) อ่านจากบัตร แปลงไบต์ JPEG จาก `ThaiIdCardData.GetPhoto()` ให้ UI ของ Windows แสดงได้ แพ็กเกจนี้ไม่ขึ้นกับไลบรารีบัตร และไม่เคยสื่อสารกับบัตร

**สถานะ: 3.0.0-preview ยังไม่เคยทดสอบกับรูปถ่ายจากบัตรจริง ยังไม่เผยแพร่บน NuGet และใน repository นี้การทดสอบของแพ็กเกจรันบน Windows CI เท่านั้น** การทดสอบเหล่านั้นใช้ภาพเปล่าที่สร้างขึ้นเอง การที่ CI ผ่านจึงไม่ใช่หลักฐานด้านฮาร์ดแวร์

```csharp
using ThaiNationalIDCard.Windows;

// Windows Forms or System.Drawing. The caller owns the image and disposes it.
pictureBox.Image = card.GetPhoto()?.ToImage();

// WPF. The result is frozen, so any thread can use it.
imageControl.Source = card.GetPhoto()?.ToBitmapSource();
```

`GetPhoto()` คืน null เมื่อไม่มีรูปถ่าย เช่น เมื่อ `ReadOptions.IncludePhoto` เป็น false Helper ทั้งสองตัวโยน `ArgumentException` เมื่อไบต์ไม่ใช่ JPEG ที่ถอดรหัสได้

Target: `net10.0-windows`, `net48` รูปถ่ายเป็นข้อมูลส่วนบุคคล: ดู [docs/privacy.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md)
