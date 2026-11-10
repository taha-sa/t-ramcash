using System.Drawing;
using System.Reflection;

namespace TRamCash;

internal static class BrandAssets
{
    internal static Bitmap LoadLogo(string id)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"TRamCash.Assets.{id}.png");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
