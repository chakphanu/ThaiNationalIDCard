# ThaiNationalIDCard.Windows

Windows helpers for the photo that [ThaiNationalIDCard](https://github.com/chakphanu/ThaiNationalIDCard/tree/v3) reads from
a card. They turn the JPEG bytes from `ThaiIdCardData.GetPhoto()` into something a Windows UI can show. This package has no
dependency on the card library and never talks to a card.

**Status: 3.0.0-preview. Not tested with a photo from a real card, and in this repository its tests only run on Windows CI.**

```csharp
using ThaiNationalIDCard.Windows;

// Windows Forms or System.Drawing. The caller owns the image and disposes it.
pictureBox.Image = card.GetPhoto()?.ToImage();

// WPF. The result is frozen, so any thread can use it.
imageControl.Source = card.GetPhoto()?.ToBitmapSource();
```

`GetPhoto()` returns null when there is no photo, for example when `ReadOptions.IncludePhoto` was false. Both helpers throw
`ArgumentException` for bytes that are not a JPEG they can decode.

Targets: `net10.0-windows`, `net48`. The photo is personal data: see
[docs/privacy.md](https://github.com/chakphanu/ThaiNationalIDCard/blob/v3/docs/privacy.md).
