using System;
using System.Runtime.InteropServices;

namespace AutoElectiveOrb
{
    internal static class CredentialStore
    {
        private const uint Generic = 1;
        private const uint LocalMachine = 2;

        public static void Save(string studentId, string password)
        {
            if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrEmpty(password)) return;
            var bytes = System.Text.Encoding.Unicode.GetBytes(password);
            var blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = Generic,
                    TargetName = Target(studentId),
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = LocalMachine,
                    UserName = studentId,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    Comment = "AutoElectiveOrb IAAA credential"
                };
                if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.FreeCoTaskMem(blob);
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public static string Read(string studentId)
        {
            if (string.IsNullOrWhiteSpace(studentId)) return string.Empty;
            IntPtr pointer;
            if (!CredRead(Target(studentId), Generic, 0, out pointer)) return string.Empty;
            try
            {
                var credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return string.Empty;
                return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }
            finally { CredFree(pointer); }
        }

        private static string Target(string studentId) { return "AutoElectiveOrb:IAAA:" + studentId.Trim(); }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    }
}
