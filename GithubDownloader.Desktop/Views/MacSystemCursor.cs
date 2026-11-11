using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace GithubDownloader.Desktop.Views;

internal static class MacSystemCursor
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static Cursor? LoadClosedHand()
    {
        // AppKit is already loaded by Avalonia's macOS backend. Keep all temporary
        // Cocoa objects alive until their pixels have been copied into Avalonia.
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            IntPtr cursorClass = objc_getClass("NSCursor");
            if (cursorClass == IntPtr.Zero) return null;
            IntPtr cursor = Send(cursorClass, sel_registerName("closedHandCursor"));
            if (cursor == IntPtr.Zero) return null;
            IntPtr image = Send(cursor, sel_registerName("image"));
            if (image == IntPtr.Zero) return null;

            NativePoint hotSpot = GetPoint(cursor, sel_registerName("hotSpot"));
            NativeSize size = GetSize(image, sel_registerName("size"));
            if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height)
                || size.Width < 1 || size.Height < 1 || size.Width > 1024 || size.Height > 1024
                || !double.IsFinite(hotSpot.X) || !double.IsFinite(hotSpot.Y)
                || hotSpot.X < 0 || hotSpot.Y < 0 || hotSpot.X >= size.Width || hotSpot.Y >= size.Height)
                return null;

            // Encode the native image as PNG: Skia does not decode AppKit's TIFF.
            IntPtr tiff = Send(image, sel_registerName("TIFFRepresentation"));
            if (tiff == IntPtr.Zero) return null;
            IntPtr representation = SendObject(objc_getClass("NSBitmapImageRep"), sel_registerName("imageRepWithData:"), tiff);
            if (representation == IntPtr.Zero) return null;
            IntPtr properties = Send(objc_getClass("NSDictionary"), sel_registerName("dictionary"));
            const nuint pngFileType = 4; // NSBitmapImageFileTypePNG
            IntPtr png = EncodeImage(representation, sel_registerName("representationUsingType:properties:"), pngFileType, properties);
            if (png == IntPtr.Zero) return null;
            nuint length = GetLength(png, sel_registerName("length"));
            IntPtr bytes = Send(png, sel_registerName("bytes"));
            if (length == 0 || length > 16 * 1024 * 1024 || bytes == IntPtr.Zero) return null;
            byte[] data = new byte[(int)length];
            Marshal.Copy(bytes, data, 0, data.Length);

            using MemoryStream stream = new(data, writable: false);
            using Bitmap bitmap = new(stream);
            // Cocoa hotspots are in points. Normalize Retina image representations
            // to that logical size so both the cursor and hotspot stay aligned.
            PixelSize logicalSize = new((int)Math.Round(size.Width), (int)Math.Round(size.Height));
            using Bitmap scaled = bitmap.CreateScaledBitmap(logicalSize);
            return new Cursor(scaled, new PixelPoint((int)hotSpot.X, (int)hotSpot.Y));
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public double Width;
        public double Height;
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjectiveCLibrary)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendObject(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr EncodeImage(IntPtr receiver, IntPtr selector, nuint fileType, IntPtr properties);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint GetLength(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern NativePoint GetPoint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern NativeSize GetSize(IntPtr receiver, IntPtr selector);
}
