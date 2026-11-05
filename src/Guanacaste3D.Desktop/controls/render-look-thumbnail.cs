using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Small examples of a sky or photographic finish, generated from the same lighting and finish settings as the renderer.</summary>
internal static class RenderLookThumbnail
{
    public static BitmapSource? Style(ImageStyle style)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources", "samples", "casa-del-cobogo.png");
        if (!File.Exists(path)) return null;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 96; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); converted.Freeze();
        var w = converted.PixelWidth; var h = converted.PixelHeight; var bytes = new byte[w*h*4]; converted.CopyPixels(bytes, w*4, 0);
        var pixels = new Vector3[w*h];
        for (int i=0;i<pixels.Length;i++) pixels[i] = new Vector3(MaterialLibrary.SrgbToLinear(bytes[i*4+2]/255f), MaterialLibrary.SrgbToLinear(bytes[i*4+1]/255f), MaterialLibrary.SrgbToLinear(bytes[i*4]/255f));
        var finish = style.Apply(ImageProcessor.Plain) with { AutoExposure = false, Dither = false };
        return Bitmap(w,h,ImageProcessor.ToBgra32(pixels,w,h,1,finish));
    }

    public static BitmapSource Sky(SkyPreset preset)
    {
        const int w=96,h=54;
        var settings=RenderPresets.Lighting(preset.Scenario, 12.1, preset.DayOfYear, preset.SolarHour);
        var sky=new SkyModel(settings.Sky,settings.SunDirection,(float)preset.Turbidity);
        var clouds=new CloudLayer((float)preset.CloudCover,preset.CloudSeed,preset.Clouds,(float)preset.CloudDirectionDegrees);
        var pixels=new Vector3[w*h];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            var direction=Vector3.Normalize(new Vector3(1,(x/(float)(w-1)-.5f)*2.0f,.05f+(h-1-y)/(float)(h-1)*1.2f));
            var baseSky=sky.Radiance(direction)*(float)preset.SkyStrength;
            var sample=clouds.Sample(direction);
            var shade=clouds.ShadeFor(direction,sample.Thickness,settings.SunDirection,new Vector3((float)preset.SunStrength),new Vector3(.55f));
            pixels[y*w+x]=Vector3.Lerp(baseSky,shade,sample.Opacity);
        }
        return Bitmap(w,h,ImageProcessor.ToBgra32(pixels,w,h,.5f,ImageProcessor.Plain));
    }

    private static BitmapSource Bitmap(int width,int height,byte[] pixels)
    {
        var result=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4); result.Freeze(); return result;
    }
}
