// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Schwärzen beim Erfassen (spec 7.3). Wertnamen mit password, passwd, pwd, secret, token, credential, passphrase
    /// oder community (ohne Groß/klein) sowie DefaultPassword und AltDefaultPassword werden als
    /// <c>{ "t": …, "redacted": true, "empty": … }</c> geschrieben. Ausnahme (Entscheidung des Pakets collector):
    /// DWORD- und QWORD-Werte sind Schalter, keine Geheimnisse – sie bleiben lesbar, sonst wären z. B.
    /// LocalAccountTokenFilterPolicy, LimitBlankPasswordUse oder DisablePasswordSaving nicht prüfbar. DefaultPassword
    /// und AltDefaultPassword werden immer geschwärzt. Unter …\SNMP\Parameters\ValidCommunities werden die Wertnamen
    /// (die Community-Namen) zu „redacted-&lt;n&gt;“; unter …\SNMP\Parameters\TrapConfiguration ebenso die Namen der
    /// Unterschlüssel.
    /// </summary>
    internal static class Redaction
    {
        private static readonly string[] SecretParts = { "password", "passwd", "pwd", "secret", "token", "credential", "passphrase", "community" };

        public const string SnmpCommunities = @"\SNMP\Parameters\ValidCommunities";
        public const string SnmpTraps = @"\SNMP\Parameters\TrapConfiguration";

        /// <summary>Ist der Wertname ein Geheimnis-Name (unabhängig vom Typ)?</summary>
        public static bool IsSecretName(string valueName)
        {
            if (string.IsNullOrEmpty(valueName)) return false;
            string lower = valueName.ToLowerInvariant();
            foreach (string part in SecretParts)
                if (lower.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>Wird dieser Wert geschwärzt? Zahlen-Schalter (REG_DWORD, REG_QWORD) nur bei DefaultPassword/AltDefaultPassword.</summary>
        public static bool ShouldRedact(string valueName, int regType)
        {
            if (string.Equals(valueName, "DefaultPassword", StringComparison.OrdinalIgnoreCase)
                || string.Equals(valueName, "AltDefaultPassword", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IsSecretName(valueName)) return false;
            return regType != RegTypes.Dword && regType != RegTypes.Qword;
        }

        /// <summary>Liegt der Schlüssel unter einer Liste von SNMP-Communities (Wertnamen schwärzen)?</summary>
        public static bool IsSnmpCommunityKey(string keyPath)
        {
            return keyPath.IndexOf(SnmpCommunities, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Ist der Schlüssel …\TrapConfiguration selbst (Unterschlüssel heißen wie Communities)?</summary>
        public static bool IsSnmpTrapKey(string keyPath)
        {
            return keyPath.EndsWith(SnmpTraps, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Nummern der Registry-Typen (winnt.h REG_*).</summary>
    internal static class RegTypes
    {
        public const int None = 0;
        public const int Sz = 1;
        public const int ExpandSz = 2;
        public const int Binary = 3;
        public const int Dword = 4;
        public const int MultiSz = 7;
        public const int Qword = 11;
    }

    /// <summary>
    /// Pseudonymisierung (<c>--pseudonymize</c>, spec 7.3): lokale Benutzernamen, Winlogon-Benutzernamen,
    /// Gruppenmitglieder und Profilpfad-Abschnitte (<c>\Users\&lt;name&gt;\</c>) werden zu
    /// <c>user-&lt;erste 8 Hex von HMAC-SHA256(Schlüssel = MachineGuid, Name klein)&gt;</c>. SIDs bleiben.
    /// Pseudonymisiert ist nicht anonym: kurze Namen lassen sich erraten.
    /// </summary>
    internal sealed class Pseudonymizer
    {
        private readonly byte[] _key;

        /// <param name="machineGuid">MachineGuid des Rechners; als GUID gelesen ihre 16 Bytes, sonst der Text in UTF-8.</param>
        /// <param name="fallback">Schlüssel, wenn die MachineGuid fehlt (Hostname): bleibt so je Rechner stabil.</param>
        public Pseudonymizer(string machineGuid, string fallback)
        {
            _key = KeyBytes(machineGuid, fallback);
        }

        internal static byte[] KeyBytes(string machineGuid, string fallback)
        {
            if (!string.IsNullOrEmpty(machineGuid))
            {
                try { return new Guid(machineGuid.Trim()).ToByteArray(); }
                catch (FormatException) { return Encoding.UTF8.GetBytes(machineGuid.Trim()); }
                catch (OverflowException) { return Encoding.UTF8.GetBytes(machineGuid.Trim()); }
            }
            return Encoding.UTF8.GetBytes((fallback ?? "").ToLowerInvariant());
        }

        /// <summary>Pseudonym eines Namens; leere Namen bleiben leer.</summary>
        public string Name(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            using (var hmac = new HMACSHA256(_key))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
                var sb = new StringBuilder("user-", 13);
                for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>„DOMAIN\name“ oder „.\name“: nur der Name wird ersetzt; ohne „\“ der ganze Text.</summary>
        public string Account(string account)
        {
            if (string.IsNullOrEmpty(account)) return account;
            int slash = account.LastIndexOf('\\');
            return slash < 0 ? Name(account) : account.Substring(0, slash + 1) + Name(account.Substring(slash + 1));
        }

        /// <summary>Ersetzt in Pfaden jeden Abschnitt nach „\Users\“ (Profilordner) durch sein Pseudonym; Public und Default bleiben.</summary>
        public string Paths(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            const string marker = @"\Users\";
            int from = 0;
            while (from < text.Length)
            {
                int i = text.IndexOf(marker, from, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                int start = i + marker.Length;
                int end = start;
                while (end < text.Length && text[end] != '\\' && text[end] != '"' && text[end] != ';' && text[end] != ',' && text[end] != '|') end++;
                string segment = text.Substring(start, end - start);
                if (segment.Length == 0 || IsSharedProfile(segment)) { from = end; continue; }
                string pseudonym = Name(segment);
                text = text.Substring(0, start) + pseudonym + text.Substring(end);
                from = start + pseudonym.Length;
            }
            return text;
        }

        private static bool IsSharedProfile(string segment)
        {
            return string.Equals(segment, "Public", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "Default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "Default User", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "All Users", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith("user-", StringComparison.Ordinal);
        }
    }
}
