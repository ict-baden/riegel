// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Konten und Richtlinien über netapi32 und LSA (spec 7.3): Kennwort- und Sperrrichtlinie, lokale Konten,
    /// Benutzerrechte als SIDs, Mitglieder lokaler Gruppen (über die SID gefunden, der Name ist übersetzt).
    /// </summary>
    internal static class AccountCollector
    {
        private const int TimeqForever = -1; // TIMEQ_FOREVER (0xFFFFFFFF)
        private const int MaxUsers = 5000;   // Domänencontroller zählen hier alle Domänenkonten auf

        /// <summary>api:netusermodals – NetUserModalsGet Stufe 0 und 3; Sekunden in Tage bzw. Minuten.</summary>
        public static CollectedItem UserModals(string key)
        {
            IntPtr buffer;
            int rc = Native.NetUserModalsGet(null, 0, out buffer);
            if (rc != Native.NERR_Success) return Net(key, rc);
            Native.USER_MODALS_INFO_0 level0;
            try { level0 = (Native.USER_MODALS_INFO_0)Marshal.PtrToStructure(buffer, typeof(Native.USER_MODALS_INFO_0)); }
            finally { Native.NetApiBufferFree(buffer); }
            rc = Native.NetUserModalsGet(null, 3, out buffer);
            if (rc != Native.NERR_Success) return Net(key, rc);
            Native.USER_MODALS_INFO_3 level3;
            try { level3 = (Native.USER_MODALS_INFO_3)Marshal.PtrToStructure(buffer, typeof(Native.USER_MODALS_INFO_3)); }
            finally { Native.NetApiBufferFree(buffer); }
            return CollectedItem.Ok(key, Modals(level0, level3));
        }

        internal static JObject Modals(Native.USER_MODALS_INFO_0 level0, Native.USER_MODALS_INFO_3 level3)
        {
            return new JObject()
                .Add("minPwLen", (long)level0.MinPasswordLength)
                .Add("maxPwAgeDays", level0.MaxPasswordAge == TimeqForever ? null : (object)(long)((uint)level0.MaxPasswordAge / 86400u))
                .Add("minPwAgeDays", (long)((uint)level0.MinPasswordAge / 86400u))
                .Add("pwHistLen", (long)level0.PasswordHistoryLength)
                .Add("lockoutThreshold", (long)level3.LockoutThreshold)
                // TIMEQ_FOREVER bei der Sperrdauer heißt „bis ein Administrator entsperrt“: null
                .Add("lockoutDurationMin", level3.LockoutDuration == TimeqForever ? null : (object)(long)((uint)level3.LockoutDuration / 60u))
                .Add("lockoutWindowMin", (long)((uint)level3.LockoutObservationWindow / 60u));
        }

        /// <summary>api:netuserenum – NetUserEnum Stufe 2, nur Name, SID, RID, Schalter, letzte Anmeldung als Datum, Kennwortalter.</summary>
        public static CollectedItem UserEnum(string key, Pseudonymizer pseudonymizer)
        {
            var list = new List<object>();
            int resume = 0;
            bool truncated = false;
            while (true)
            {
                IntPtr buffer;
                int read, total;
                int rc = Native.NetUserEnum(null, 2, Native.FILTER_NORMAL_ACCOUNT, out buffer, Native.MAX_PREFERRED_LENGTH, out read, out total, ref resume);
                if (rc != Native.NERR_Success && rc != Native.ERROR_MORE_DATA)
                {
                    if (buffer != IntPtr.Zero) Native.NetApiBufferFree(buffer);
                    return Net(key, rc);
                }
                try
                {
                    int size = Marshal.SizeOf(typeof(Native.USER_INFO_2));
                    for (int i = 0; i < read; i++)
                    {
                        if (list.Count >= MaxUsers) { truncated = true; break; }
                        var info = (Native.USER_INFO_2)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + (long)i * size), typeof(Native.USER_INFO_2));
                        list.Add(User(Marshal.PtrToStringUni(info.Name), info, pseudonymizer));
                    }
                }
                finally { if (buffer != IntPtr.Zero) Native.NetApiBufferFree(buffer); }
                if (rc != Native.ERROR_MORE_DATA || truncated) break;
            }
            var item = CollectedItem.Ok(key, list);
            item.Truncated = truncated;
            return item;
        }

        private static JObject User(string name, Native.USER_INFO_2 info, Pseudonymizer pseudonymizer)
        {
            // Zuerst als lokales Konto, sonst unqualifiziert (Domänencontroller: Domänenkonten)
            string sid = Sid(Environment.MachineName + "\\" + name) ?? Sid(name);
            const int UF_ACCOUNTDISABLE = 0x2, UF_LOCKOUT = 0x10, UF_PASSWD_NOTREQD = 0x20, UF_DONT_EXPIRE_PASSWD = 0x10000;
            object lastLogon = null;
            if (info.LastLogon != 0)
                lastLogon = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds((uint)info.LastLogon).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new JObject()
                .Add("name", pseudonymizer != null ? pseudonymizer.Name(name) : name)
                .Add("sid", sid)
                .Add("rid", Rid(sid))
                .Add("disabled", (info.Flags & UF_ACCOUNTDISABLE) != 0)
                .Add("passwordNotRequired", (info.Flags & UF_PASSWD_NOTREQD) != 0)
                .Add("passwordNeverExpires", (info.Flags & UF_DONT_EXPIRE_PASSWD) != 0)
                .Add("lockedOut", (info.Flags & UF_LOCKOUT) != 0)
                .Add("lastLogonDate", lastLogon)
                .Add("passwordAgeDays", (long)((uint)info.PasswordAge / 86400u));
        }

        /// <summary>SID zu einem Kontonamen (beim Erfassen übersetzt, spec 7.4); null, wenn nicht auflösbar.</summary>
        internal static string Sid(string account)
        {
            try { return ((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value; }
            catch (IdentityNotMappedException) { return null; }
            catch (SystemException) { return null; }
        }

        internal static object Rid(string sid)
        {
            if (sid == null) return null;
            int dash = sid.LastIndexOf('-');
            long rid;
            return dash > 0 && long.TryParse(sid.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out rid) ? (object)rid : null;
        }

        /// <summary>api:localgroup:&lt;SID&gt; – Gruppe über die SID finden, Mitglieder mit NetLocalGroupGetMembers Stufe 2.</summary>
        public static CollectedItem LocalGroup(string key, string groupSid, Pseudonymizer pseudonymizer)
        {
            string group;
            try
            {
                string account = ((NTAccount)new SecurityIdentifier(groupSid).Translate(typeof(NTAccount))).Value;
                group = account.Substring(account.IndexOf('\\') + 1);
            }
            catch (IdentityNotMappedException)
            {
                return CollectedItem.Fail(key, Statuses.NotApplicable, null, "group does not exist");
            }
            var list = new List<object>();
            IntPtr resume = IntPtr.Zero;
            while (true)
            {
                IntPtr buffer;
                int read, total;
                int rc = Native.NetLocalGroupGetMembers(null, group, 2, out buffer, Native.MAX_PREFERRED_LENGTH, out read, out total, ref resume);
                if (rc != Native.NERR_Success && rc != Native.ERROR_MORE_DATA)
                {
                    if (buffer != IntPtr.Zero) Native.NetApiBufferFree(buffer);
                    return Net(key, rc);
                }
                try
                {
                    int size = Marshal.SizeOf(typeof(Native.LOCALGROUP_MEMBERS_INFO_2));
                    for (int i = 0; i < read; i++)
                    {
                        var info = (Native.LOCALGROUP_MEMBERS_INFO_2)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + (long)i * size), typeof(Native.LOCALGROUP_MEMBERS_INFO_2));
                        list.Add(Member(info, pseudonymizer));
                    }
                }
                finally { if (buffer != IntPtr.Zero) Native.NetApiBufferFree(buffer); }
                if (rc != Native.ERROR_MORE_DATA) break;
            }
            return CollectedItem.Ok(key, list);
        }

        private static JObject Member(Native.LOCALGROUP_MEMBERS_INFO_2 info, Pseudonymizer pseudonymizer)
        {
            string sid = info.Sid != IntPtr.Zero ? new SecurityIdentifier(info.Sid).Value : null;
            string full = Marshal.PtrToStringUni(info.DomainAndName) ?? "";
            int slash = full.IndexOf('\\');
            string domain = slash >= 0 ? full.Substring(0, slash) : null;
            string name = slash >= 0 ? full.Substring(slash + 1) : full;
            string kind = Kind(info.SidUsage);
            if (pseudonymizer != null && kind == "user") name = pseudonymizer.Name(name);
            return new JObject().Add("sid", sid).Add("name", name).Add("kind", kind).Add("domain", domain);
        }

        /// <summary>SID_NAME_USE → Art im Vertrag.</summary>
        internal static string Kind(int sidUsage)
        {
            switch (sidUsage)
            {
                case 1: return "user";           // SidTypeUser
                case 2: return "group";          // SidTypeGroup
                case 4: return "group";          // SidTypeAlias
                case 5: return "wellKnown";      // SidTypeWellKnownGroup
                case 6: return "deletedAccount"; // SidTypeDeletedAccount
                default: return "unknown";
            }
        }

        private static CollectedItem Net(string key, int rc)
        {
            return rc == Native.ERROR_ACCESS_DENIED
                ? CollectedItem.Fail(key, Statuses.Denied, rc, "Access denied")
                : CollectedItem.Fail(key, Statuses.Error, rc, "NetAPI error");
        }

        // ------------------------------------------------------------------ Benutzerrechte

        /// <summary>api:lsa-rights – je Recht des Plans die SIDs (leere Liste = niemand).</summary>
        public static CollectedItem Rights(string key, string[] rights, ILsaRights lsa)
        {
            int error;
            if (!lsa.Open(out error))
                return error == Native.ERROR_ACCESS_DENIED
                    ? CollectedItem.Fail(key, Statuses.Denied, error, "Access denied")
                    : CollectedItem.Fail(key, Statuses.Error, error, "LSA error");
            try
            {
                var result = new JObject();
                foreach (string right in rights)
                {
                    var sids = new List<string>();
                    error = lsa.Enumerate(right, sids);
                    // Ohne Adminrechte lässt sich die Richtlinie öffnen, aber nicht aufzählen (Fehler 5)
                    if (error == Native.ERROR_ACCESS_DENIED) return CollectedItem.Fail(key, Statuses.Denied, error, "Access denied");
                    if (error != 0) return CollectedItem.Fail(key, Statuses.Error, error, "LSA error");
                    result.Add(right, new List<object>(sids.ToArray()));
                }
                return CollectedItem.Ok(key, result);
            }
            finally { lsa.Close(); }
        }
    }

    /// <summary>Lesender Zugriff auf die Benutzerrechte (in Tests ersetzt).</summary>
    internal interface ILsaRights
    {
        bool Open(out int error);

        /// <summary>SIDs mit diesem Recht; Rückgabe 0 (auch wenn niemand es hat oder es das Recht hier nicht gibt) oder der Win32-Fehler.</summary>
        int Enumerate(string right, List<string> sids);

        void Close();
    }

    /// <summary>LsaOpenPolicy(POLICY_VIEW_LOCAL_INFORMATION | POLICY_LOOKUP_NAMES) + LsaEnumerateAccountsWithUserRight.</summary>
    internal sealed class Win32LsaRights : ILsaRights
    {
        private IntPtr _policy;

        public bool Open(out int error)
        {
            var attributes = new Native.LSA_OBJECT_ATTRIBUTES();
            attributes.Length = Marshal.SizeOf(typeof(Native.LSA_OBJECT_ATTRIBUTES));
            uint status = Native.LsaOpenPolicy(IntPtr.Zero, ref attributes, Native.POLICY_VIEW_LOCAL_INFORMATION | Native.POLICY_LOOKUP_NAMES, out _policy);
            error = status == 0 ? 0 : Native.LsaNtStatusToWinError(status);
            return status == 0;
        }

        public int Enumerate(string right, List<string> sids)
        {
            var name = new Native.LSA_UNICODE_STRING();
            name.Buffer = Marshal.StringToHGlobalUni(right);
            name.Length = (ushort)(right.Length * 2);
            name.MaximumLength = (ushort)(right.Length * 2 + 2);
            try
            {
                IntPtr buffer;
                int count;
                uint status = Native.LsaEnumerateAccountsWithUserRight(_policy, ref name, out buffer, out count);
                if (status == Native.STATUS_NO_MORE_ENTRIES || status == Native.STATUS_NO_SUCH_PRIVILEGE || status == Native.STATUS_OBJECT_NAME_NOT_FOUND) return 0;
                if (status != 0) return Native.LsaNtStatusToWinError(status);
                try
                {
                    for (int i = 0; i < count; i++)
                        sids.Add(new SecurityIdentifier(Marshal.ReadIntPtr(buffer, i * IntPtr.Size)).Value);
                }
                finally { Native.LsaFreeMemory(buffer); }
                sids.Sort(StringComparer.Ordinal);
                return 0;
            }
            finally { Marshal.FreeHGlobal(name.Buffer); }
        }

        public void Close()
        {
            if (_policy != IntPtr.Zero) Native.LsaClose(_policy);
            _policy = IntPtr.Zero;
        }
    }
}
