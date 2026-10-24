using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LevelLibrary;

/// <summary>现代系统文件对话框（Vista+ IFileDialog COM，与系统资源管理器同款样式）；Unity 的 Mono WinForms 只支持经典对话框，所以直接调 Shell。
/// IFileDialog 必须在 STA 线程 + CoInitialize 之后调用，这里统一丢到独立 STA 线程并阻塞主线程等结果，否则异常会炸掉调用方 UI。</summary>
public static class ModernFileDialog {
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]   // CLSID_FileSaveDialog
    private class FileSaveDialogRCW { }

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]   // CLSID_FileOpenDialog
    private class FileOpenDialogRCW { }

    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IFileDialogEvents pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName(string pszName);
        void GetFileName(out string pszName);
        void SetTitle(string pszTitle);
        void SetOkButtonLabel(string pszText);
        void SetFileNameLabel(string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, FDAP fdap);
        void SetDefaultExtension(string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IShellItemFilter pFilter);
    }

    [ComImport, Guid("973510DB-7D7F-452B-8975-74A85828D354"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialogEvents { }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray { }

    [ComImport, Guid("2659B475-EEB8-48B7-8F07-B378810F48DD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemFilter { }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [Flags]
    private enum FOS : uint {
        FOS_OVERWRITEPROMPT = 0x2,
        FOS_STRICTFILETYPES = 0x4,
        FOS_NOCHANGEDIR = 0x8,
        FOS_PICKFOLDERS = 0x20,
        FOS_FORCEFILESYSTEM = 0x40,
        FOS_ALLNONSTORAGEITEMS = 0x80,
        FOS_NOVALIDATE = 0x100,
        FOS_ALLOWMULTISELECT = 0x200,
        FOS_PATHMUSTEXIST = 0x800,
        FOS_FILEMUSTEXIST = 0x1000,
        FOS_CREATEPROMPT = 0x2000,
        FOS_SHAREAWARE = 0x4000,
        FOS_NOREADONLYRETURN = 0x8000,
        FOS_NOTESTFILECREATE = 0x10000,
        FOS_HIDEMRUPLACES = 0x20000,
        FOS_HIDEPINNEDPLACES = 0x40000,
        FOS_NODEREFERENCELINKS = 0x80000,
        FOS_DONTADDTORECENT = 0x2000000,
        FOS_FORCESHOWHIDDEN = 0x10000000,
        FOS_DEFAULTNOMINIMODE = 0x20000000,
        FOS_FORCEPREVIEWPANEON = 0x40000000,
        FOS_SUPPORTSTREAMABLEITEMS = 0x80000000,
    }

    private enum SIGDN : uint {
        SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000,
        SIGDN_FILESYSPATH = 0x80058000,
    }

    private enum FDAP : uint { FDAP_BOTTOM = 0, FDAP_TOP = 1 }

    /// <summary>打开文件对话框，返回完整路径；取消返回 null</summary>
    public static string OpenFile(string title, string filterName, string filterExt) {
        return RunSta(() => {
            var dialog = (IFileDialog)new FileOpenDialogRCW();
            return ShowDialog(dialog, title, filterName, filterExt, null, false);
        });
    }

    /// <summary>保存文件对话框，返回完整路径；取消返回 null</summary>
    public static string SaveFile(string title, string filterName, string filterExt, string defaultName) {
        return RunSta(() => {
            var dialog = (IFileDialog)new FileSaveDialogRCW();
            return ShowDialog(dialog, title, filterName, filterExt, defaultName, true);
        });
    }

    /// <summary>在独立 STA 线程里跑对话框，主线程阻塞等待结果</summary>
    private static string RunSta(Func<string> action) {
        string result = null;
        Exception error = null;
        var thread = new Thread(() => {
            try {
                CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                result = action();
            } catch (Exception e) {
                error = e;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result;
    }

    /// <summary>选择文件夹对话框（Vista+ 新式，与资源管理器同款），返回完整路径；取消返回 null</summary>
    public static string OpenFolder(string title) {
        return RunSta(() => {
            var dialog = (IFileDialog)new FileOpenDialogRCW();
            try {
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);
                // 不加 FOS_NODEREFERENCELINKS：带上它时“导入”打不开资源管理器
                dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST);
                int hr = dialog.Show(IntPtr.Zero);
                if (hr != 0) return null;   // 用户取消
                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out IntPtr psz);
                string path = Marshal.PtrToStringUni(psz);
                Marshal.FreeCoTaskMem(psz);
                return path;
            } finally {
                Marshal.FinalReleaseComObject(dialog);
            }
        });
    }

    private static string ShowDialog(IFileDialog dialog, string title, string filterName, string filterExt, string defaultName, bool isSave) {
        try {
            if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);
            string spec = filterExt != null && filterExt.Contains("*") ? filterExt : "*." + filterExt;
            dialog.SetFileTypes(1, new[] { new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = spec } });
            dialog.SetFileTypeIndex(1);
            if (!string.IsNullOrEmpty(defaultName)) dialog.SetFileName(defaultName);
            dialog.SetOptions(isSave
                ? FOS.FOS_OVERWRITEPROMPT | FOS.FOS_PATHMUSTEXIST
                : FOS.FOS_FILEMUSTEXIST | FOS.FOS_PATHMUSTEXIST);
            int hr = dialog.Show(IntPtr.Zero);
            if (hr != 0) return null;   // 用户取消
            dialog.GetResult(out IShellItem item);
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out IntPtr psz);
            string path = Marshal.PtrToStringUni(psz);
            Marshal.FreeCoTaskMem(psz);
            return path;
        } finally {
            Marshal.FinalReleaseComObject(dialog);
        }
    }
}
