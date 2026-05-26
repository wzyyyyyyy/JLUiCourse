using Avalonia.Media.Imaging;

namespace iCourse.Services;

public interface IImageDecoder
{
    Bitmap DecodeBase64Bitmap(string base64Image);
}
