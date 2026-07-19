using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AnalyseTool.Tools.Infrastructure
{
    /// <summary>
    /// Extracts the Windows Explorer thumbnail for a file (via <c>IShellItemImageFactory</c>) — for Revit
    /// <c>.rfa</c> files this is the embedded preview, obtained WITHOUT opening the family. Returned as a
    /// PNG data URI. Best-effort: any failure yields null so the caller falls back to a placeholder.
    /// </summary>
    internal static class ShellThumbnail
    {
        public static string? GetPngDataUri(string path, int size = 128)
        {
            // Shell image extraction is safest on an STA thread (some thumbnail providers require it).
            return RunSta(() => Extract(path, size));
        }

        private static string? Extract(string path, int size)
        {
            if (!File.Exists(path)) return null;

            IntPtr hbitmap = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out IShellItemImageFactory factory);
                if (factory is null) return null;

                SIZE sz = new() { cx = size, cy = size };
                int hr = factory.GetImage(sz, SIIGBF_RESIZETOFIT, out hbitmap);
                Marshal.ReleaseComObject(factory);
                if (hr != 0 || hbitmap == IntPtr.Zero) return null;

                using Bitmap bmp = Image.FromHbitmap(hbitmap);
                using MemoryStream ms = new();
                bmp.Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            }
        }

        private static T RunSta<T>(Func<T> func)
        {
            T result = default!;
            Thread t = new(() => { try { result = func(); } catch { /* best-effort */ } });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
            return result;
        }

        private const int SIIGBF_RESIZETOFIT = 0x0;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            string pszPath, IntPtr pbc, in Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }
    }
}
