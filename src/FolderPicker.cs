using System;
using System.Runtime.InteropServices;

namespace ClaudeSwitch
{
    // Windows 新式“选择文件夹”窗口（IFileOpenDialog + FOS_PICKFOLDERS）；不可用时退回老式的文件夹浏览框
    public static class FolderPicker
    {
        public static string Pick(IntPtr owner, string title, string initial)
        {
            try { return PickModern(owner, title, initial); }
            catch
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = title, SelectedPath = initial ?? "" })
                    return d.ShowDialog() == System.Windows.Forms.DialogResult.OK ? d.SelectedPath : null;
            }
        }

        private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        private static string PickModern(IntPtr owner, string title, string initial)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
            try
            {
                uint opts;
                dialog.GetOptions(out opts);
                dialog.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);
                dialog.SetOkButtonLabel("选择这个文件夹");

                // 初始位置：已存在的最近一级目录
                var start = initial;
                while (!string.IsNullOrEmpty(start) && !System.IO.Directory.Exists(start))
                    start = System.IO.Path.GetDirectoryName(start);
                if (!string.IsNullOrEmpty(start))
                {
                    IShellItem folder;
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out folder) == 0 && folder != null)
                        dialog.SetFolder(folder);
                }

                var hr = dialog.Show(owner);
                if (hr == ERROR_CANCELLED) return null;
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);

                IShellItem item;
                dialog.GetResult(out item);
                IntPtr p;
                item.GetDisplayName(SIGDN_FILESYSPATH, out p);
                try { return Marshal.PtrToStringUni(p); }
                finally { Marshal.FreeCoTaskMem(p); }
            }
            finally { Marshal.ReleaseComObject(dialog); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw { }

        // 只声明用到的前半段方法，顺序必须和 IFileDialog 的 vtable 一致
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName(out IntPtr pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
