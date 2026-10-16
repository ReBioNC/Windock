using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Windock.Interop;

/// COM declarations for the Windows shell: property stores, shortcuts, and item thumbnails.
internal static class ShellInterop
{
    public static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    public static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    public static readonly PropertyKey PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public const ushort VT_LPWSTR = 31;
    public const int SIIGBF_RESIZETOFIT = 0x0, SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
    public const int STGM_READ = 0;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct PropertyKey(Guid fmtid, uint pid)
    {
        public readonly Guid FormatId = fmtid;
        public readonly uint PropertyId = pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;

        public readonly string? AsString() => vt == VT_LPWSTR ? Marshal.PtrToStringUni(pointerValue) : null;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropVariant value);
        void SetValue(in PropertyKey key, in PropVariant value);
        void Commit();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int max, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    public class ShellLink;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx, cy;
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr hBitmap);
    }

    public static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(IntPtr bindCtx, in Guid handler, in Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHCreateItemFromParsingName")]
    private static extern int SHCreateShellItem(string path, IntPtr bindCtx, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    /// Friendly name of a shell item such as "shell:AppsFolder\<AUMID>" (e.g. "Calculator").
    public static string? DisplayName(string parsingName)
    {
        try
        {
            if (SHCreateShellItem(parsingName, IntPtr.Zero, IID_IShellItem, out var item) != 0) return null;
            try
            {
                item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var ptr);
                try { return Marshal.PtrToStringUni(ptr); }
                finally { Marshal.FreeCoTaskMem(ptr); }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static readonly PropertyKey PKEY_AppUserModel_PackageInstallPath = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 15);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindCtx, int flags, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    /// Install folder of a Store app from its "shell:AppsFolder\<AUMID>" item, or null.
    public static string? PackageInstallPath(string appsFolderPath)
    {
        try
        {
            if (SHGetPropertyStoreFromParsingName(appsFolderPath, IntPtr.Zero, 0, IID_IPropertyStore, out var store) != 0) return null;
            try { return ReadString(store, PKEY_AppUserModel_PackageInstallPath); }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch (COMException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pv);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    public static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(key, out var pv);
        try
        {
            return pv.AsString();
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }

    public static string? WindowAppUserModelId(IntPtr hwnd)
    {
        try
        {
            if (SHGetPropertyStoreForWindow(hwnd, IID_IPropertyStore, out var store) != 0) return null;
            try { return ReadString(store, PKEY_AppUserModel_ID); }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// Target path and AppUserModelID of a .lnk file; either may be null (e.g. Store app shortcuts have no path).
    public static (string? Target, string? Arguments, string? Aumid) ReadShortcut(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, STGM_READ);
            var sb = new System.Text.StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var target = sb.Length > 0 ? sb.ToString() : null;
            var args = new System.Text.StringBuilder(1024);
            link.GetArguments(args, args.Capacity);
            var aumid = ReadString((IPropertyStore)link, PKEY_AppUserModel_ID);
            return (target, args.ToString(), string.IsNullOrWhiteSpace(aumid) ? null : aumid);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}
