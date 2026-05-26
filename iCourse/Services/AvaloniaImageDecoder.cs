using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace iCourse.Services;

public sealed class AvaloniaImageDecoder : IImageDecoder
{
    public Bitmap DecodeBase64Bitmap(string base64Image)
    {
        var imageBytes = Convert.FromBase64String(base64Image);
        return new Bitmap(new MemoryStream(imageBytes));
    }
}
