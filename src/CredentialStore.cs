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
            SaveCredential(Target("IAAA", studentId), studentId, password, "AutoElectiveOrb IAAA credential");
        }

        public static string Read(string studentId)
        {
            return ReadCredential(Target("IAAA", studentId));
        }

        public static void SaveTt(string username, string password)
        {
            SaveCredential(Target("TTShitu", username), username, password, "AutoElectiveOrb TTShitu credential");
        }

        public static string ReadTt(string username)
        {
            return ReadCredential(Target("TTShitu", username));
        }

        private static void SaveCredential(string target, string username, string password, string comment)
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return;
            var bytes = System.Text.Encoding.Unicode.GetBytes(password);
            var blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = Generic,
                    TargetName = target,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = LocalMachine,
                    UserName = username.Trim(),
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    Comment = comment
                };
                if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.FreeCoTaskMem(blob);
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static string ReadCredential(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return string.Empty;
            IntPtr pointer;
            if (!CredRead(target, Generic, 0, out pointer)) return string.Empty;
            try
            {
                var credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return string.Empty;
                return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }
            finally { CredFree(pointer); }
        }

        private static string Target(string kind, string account)
        {
            return string.IsNullOrWhiteSpace(account) ? string.Empty : "AutoElectiveOrb:" + kind + ":" + account.Trim();
        }

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
