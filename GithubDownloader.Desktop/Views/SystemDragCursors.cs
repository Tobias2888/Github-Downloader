using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GithubDownloader.Desktop.Views;

internal static class SystemDragCursors
{
    public static Cursor OpenHand { get; } = OperatingSystem.IsMacOS()
        ? new Cursor(StandardCursorType.DragMove) // Avalonia maps this to NSCursor.openHandCursor.
        : Load("grab", "openhand", StandardCursorType.Hand);
    public static Cursor ClosedHand { get; } = Load("grabbing", "closedhand", StandardCursorType.DragMove);

    private static Cursor Load(string name, string alias, StandardCursorType fallback)
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                Cursor? cursor = MacSystemCursor.LoadClosedHand();
                if (cursor is not null) return cursor;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        if (OperatingSystem.IsLinux() && BitConverter.IsLittleEndian)
        {
            try
            {
                Cursor? cursor = LoadLinuxCursor(name, alias);
                if (cursor is not null) return cursor;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        return new Cursor(fallback);
    }

    private static Cursor? LoadLinuxCursor(string name, string alias)
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        try
        {
            string? theme = display != IntPtr.Zero
                ? Marshal.PtrToStringUTF8(XcursorGetTheme(display))
                : Environment.GetEnvironmentVariable("XCURSOR_THEME");
            int size = display != IntPtr.Zero ? XcursorGetDefaultSize(display) : 24;
            if (display == IntPtr.Zero && int.TryParse(Environment.GetEnvironmentVariable("XCURSOR_SIZE"), out int configuredSize))
                size = configuredSize;
            if (size <= 0) size = 24;

            IntPtr image = XcursorLibraryLoadImage(name, theme, size);
            if (image == IntPtr.Zero) image = XcursorLibraryLoadImage(alias, theme, size);
            if (image == IntPtr.Zero) return null;
            try
            {
                XcursorImage cursor = Marshal.PtrToStructure<XcursorImage>(image);
                if (cursor.Width == 0 || cursor.Height == 0 || cursor.Width > 1024 || cursor.Height > 1024
                    || cursor.XHot >= cursor.Width || cursor.YHot >= cursor.Height || cursor.Pixels == IntPtr.Zero)
                    return null;

                // Xcursor supplies premultiplied ARGB pixels from the user's theme.
                using Bitmap bitmap = new(PixelFormat.Bgra8888, AlphaFormat.Premul, cursor.Pixels,
                    new PixelSize((int)cursor.Width, (int)cursor.Height), new Vector(96, 96), (int)cursor.Width * 4);
                return new Cursor(bitmap, new PixelPoint((int)cursor.XHot, (int)cursor.YHot));
            }
            finally { XcursorImageDestroy(image); }
        }
        finally
        {
            if (display != IntPtr.Zero) XCloseDisplay(display);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XcursorImage
    {
        public uint Version;
        public uint Size;
        public uint Width;
        public uint Height;
        public uint XHot;
        public uint YHot;
        public uint Delay;
        public IntPtr Pixels;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libXcursor.so.1")]
    private static extern IntPtr XcursorGetTheme(IntPtr display);

    [DllImport("libXcursor.so.1")]
    private static extern int XcursorGetDefaultSize(IntPtr display);

    [DllImport("libXcursor.so.1")]
    private static extern IntPtr XcursorLibraryLoadImage(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? theme, int size);

    [DllImport("libXcursor.so.1")]
    private static extern void XcursorImageDestroy(IntPtr image);
}
