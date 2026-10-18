// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
// Alle Aufrufe lesen nur. Die einzige Änderung ist das Einschalten von SeSecurityPrivilege im eigenen Prozess-Token
// (für AuditQuerySystemPolicy), das danach wieder zurückgesetzt wird – keine Einstellung des Rechners.
#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>P/Invoke-Deklarationen (advapi32, netapi32, kernel32, ntdll, wevtapi, winhttp), alle lesend.</summary>
    internal static class Native
    {
        // ---- Registry (64-Bit-Ansicht über KEY_WOW64_64KEY; net35 kennt kein RegistryView) ----
        public static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));
        public const int KEY_READ = 0x20019;
        public const int KEY_WOW64_64KEY = 0x0100;
        public const int ERROR_SUCCESS = 0;
        public const int ERROR_FILE_NOT_FOUND = 2;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_MORE_DATA = 234;
        public const int ERROR_NO_MORE_ITEMS = 259;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_PRIVILEGE_NOT_HELD = 1314;
        public const int ERROR_NOT_ALL_ASSIGNED = 1300;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
        public static extern int RegOpenKeyEx(IntPtr key, string subKey, int options, int samDesired, out IntPtr result);

        [DllImport("advapi32.dll")]
        public static extern int RegCloseKey(IntPtr key);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryInfoKeyW")]
        public static extern int RegQueryInfoKey(IntPtr key, IntPtr cls, IntPtr clsLength, IntPtr reserved, out int subKeys,
            out int maxSubKeyLength, out int maxClassLength, out int values, out int maxValueNameLength, out int maxValueLength,
            IntPtr securityDescriptor, IntPtr lastWriteTime);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumKeyExW")]
        public static extern int RegEnumKeyEx(IntPtr key, int index, [Out] char[] name, ref int nameLength, IntPtr reserved,
            IntPtr cls, IntPtr clsLength, IntPtr lastWriteTime);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumValueW")]
        public static extern int RegEnumValue(IntPtr key, int index, [Out] char[] name, ref int nameLength, IntPtr reserved,
            out int type, [Out] byte[] data, ref int dataLength);

        // ---- Kennwort- und Sperrrichtlinie, lokale Konten und Gruppen (netapi32) ----
        public const int MAX_PREFERRED_LENGTH = -1;
        public const int FILTER_NORMAL_ACCOUNT = 2;
        public const int NERR_Success = 0;

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetUserModalsGet(string server, int level, out IntPtr buffer);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetUserEnum(string server, int level, int filter, out IntPtr buffer, int preferredMaxLength,
            out int entriesRead, out int totalEntries, ref int resumeHandle);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetLocalGroupGetMembers(string server, string group, int level, out IntPtr buffer,
            int preferredMaxLength, out int entriesRead, out int totalEntries, ref IntPtr resumeHandle);

        [DllImport("netapi32.dll")]
        public static extern int NetApiBufferFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential)]
        public struct USER_MODALS_INFO_0
        {
            public int MinPasswordLength;
            public int MaxPasswordAge;
            public int MinPasswordAge;
            public int ForceLogoff;
            public int PasswordHistoryLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct USER_MODALS_INFO_3
        {
            public int LockoutDuration;
            public int LockoutObservationWindow;
            public int LockoutThreshold;
        }

        /// <summary>USER_INFO_2; Zeichenketten bleiben Zeiger, nur der Name wird gelesen.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct USER_INFO_2
        {
            public IntPtr Name;
            public IntPtr Password;
            public int PasswordAge;
            public int Privilege;
            public IntPtr HomeDir;
            public IntPtr Comment;
            public int Flags;
            public IntPtr ScriptPath;
            public int AuthFlags;
            public IntPtr FullName;
            public IntPtr UserComment;
            public IntPtr Parms;
            public IntPtr Workstations;
            public int LastLogon;
            public int LastLogoff;
            public int AccountExpires;
            public int MaxStorage;
            public int UnitsPerWeek;
            public IntPtr LogonHours;
            public int BadPasswordCount;
            public int NumberOfLogons;
            public IntPtr LogonServer;
            public int CountryCode;
            public int CodePage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LOCALGROUP_MEMBERS_INFO_2
        {
            public IntPtr Sid;
            public int SidUsage;
            public IntPtr DomainAndName;
        }

        // ---- Benutzerrechte (LSA) ----
        public const int POLICY_VIEW_LOCAL_INFORMATION = 0x1;
        public const int POLICY_LOOKUP_NAMES = 0x800;
        public const uint STATUS_NO_MORE_ENTRIES = 0x8000001A;
        public const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;
        public const uint STATUS_NO_SUCH_PRIVILEGE = 0xC0000060;
        public const uint STATUS_ACCESS_DENIED = 0xC0000022;

        [StructLayout(LayoutKind.Sequential)]
        public struct LSA_UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LSA_OBJECT_ATTRIBUTES
        {
            public int Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public int Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [DllImport("advapi32.dll")]
        public static extern uint LsaOpenPolicy(IntPtr systemName, ref LSA_OBJECT_ATTRIBUTES attributes, int access, out IntPtr policy);

        [DllImport("advapi32.dll")]
        public static extern uint LsaEnumerateAccountsWithUserRight(IntPtr policy, ref LSA_UNICODE_STRING right, out IntPtr buffer, out int count);

        [DllImport("advapi32.dll")]
        public static extern uint LsaFreeMemory(IntPtr buffer);

        [DllImport("advapi32.dll")]
        public static extern uint LsaClose(IntPtr policy);

        [DllImport("advapi32.dll")]
        public static extern int LsaNtStatusToWinError(uint status);

        // ---- Überwachungsrichtlinie (BOOLEAN-Rückgaben sind 1 Byte: U1, sonst liefert der Aufruf ohne Adminrechte
        //      ein falsches „true“ und einen Null-Zeiger – das war der NullReferenceException des Prototyps) ----
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool AuditEnumerateCategories(out IntPtr categories, out int count);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool AuditEnumerateSubCategories(ref Guid category, [MarshalAs(UnmanagedType.U1)] bool all, out IntPtr subCategories, out int count);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool AuditQuerySystemPolicy([In] Guid[] subCategories, int count, out IntPtr policy);

        [DllImport("advapi32.dll")]
        public static extern void AuditFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential)]
        public struct AUDIT_POLICY_INFORMATION
        {
            public Guid SubCategory;
            public int AuditingInformation;
            public Guid Category;
        }

        // ---- Privileg im eigenen Token (nur dieser Prozess) ----
        public const int TOKEN_ADJUST_PRIVILEGES = 0x20;
        public const int TOKEN_QUERY = 0x8;
        public const int SE_PRIVILEGE_ENABLED = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public int Low;
            public int High;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID Luid;
            public int Attributes;
        }

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
        public static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState,
            int bufferLength, ref TOKEN_PRIVILEGES previousState, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        // ---- LSASS-Schutzstufe (nur mit --deep) ----
        public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const int ProcessProtectionInformation = 61;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(int access, bool inherit, int processId);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(IntPtr process, int infoClass, out byte info, int length, out int returnLength);

        // ---- Prozessor und WOW64 ----
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_INFO
        {
            public ushort ProcessorArchitecture;
            public ushort Reserved;
            public int PageSize;
            public IntPtr MinimumApplicationAddress;
            public IntPtr MaximumApplicationAddress;
            public IntPtr ActiveProcessorMask;
            public int NumberOfProcessors;
            public int ProcessorType;
            public int AllocationGranularity;
            public ushort ProcessorLevel;
            public ushort ProcessorRevision;
        }

        [DllImport("kernel32.dll")]
        public static extern void GetNativeSystemInfo(out SYSTEM_INFO info);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool IsWow64Process(IntPtr process, out bool wow64);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetShortPathNameW")]
        public static extern int GetShortPathName(string longPath, StringBuilder shortPath, int length);

        // ---- Ereignisprotokoll (wevtapi, ab Vista) ----
        public const int EvtQueryChannelPath = 0x1;
        public const int EvtQueryReverseDirection = 0x200;
        public const int EvtRenderEventXml = 1;

        [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr EvtQuery(IntPtr session, string path, string query, int flags);

        [DllImport("wevtapi.dll", SetLastError = true)]
        public static extern bool EvtNext(IntPtr resultSet, int count, [Out] IntPtr[] events, int timeout, int flags, out int returned);

        [DllImport("wevtapi.dll", SetLastError = true)]
        public static extern bool EvtRender(IntPtr context, IntPtr fragment, int flags, int bufferSize, IntPtr buffer, out int used, out int propertyCount);

        [DllImport("wevtapi.dll")]
        public static extern bool EvtClose(IntPtr handle);

        // ---- Proxy-Einstellung von WinHTTP (nur lesen, keine Verbindung) ----
        [StructLayout(LayoutKind.Sequential)]
        public struct WINHTTP_PROXY_INFO
        {
            public int AccessType;
            public IntPtr Proxy;
            public IntPtr ProxyBypass;
        }

        [DllImport("winhttp.dll", SetLastError = true)]
        public static extern bool WinHttpGetDefaultProxyConfiguration(ref WINHTTP_PROXY_INFO info);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalFree(IntPtr memory);
    }
}
