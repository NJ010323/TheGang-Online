using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace View;

/// <summary>Charge le gant de laine et l'adapte au format de curseur Windows.</summary>
internal static class GameCursor
{
    /// <summary>Crée un curseur de 64 pixels dont le point actif est le bout de l'index.</summary>
    public static Cursor Create()
    {
        var source = new BitmapImage(new Uri(
            "pack://application:,,,/TheGang;component/Assets/wool-glove.png", UriKind.Absolute));
        const int size = 64;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawImage(source, new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        // CUR : en-tête Windows, point actif (4, 3), puis une image PNG intégrée.
        var data = png.ToArray();
        using var cursorStream = new MemoryStream();
        using (var writer = new BinaryWriter(cursorStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)2);
            writer.Write((ushort)1);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)4);
            writer.Write((ushort)3);
            writer.Write((uint)data.Length);
            writer.Write((uint)22);
            writer.Write(data);
        }
        cursorStream.Position = 0;
        return new Cursor(cursorStream);
    }
}
