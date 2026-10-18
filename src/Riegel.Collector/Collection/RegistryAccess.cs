// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections.Generic;

namespace Riegel.Collector.Collection
{
    /// <summary>Ein gelesener Registry-Wert, roh: Name, Typnummer (REG_*) und Bytes.</summary>
    internal sealed class RegValue
    {
        public string Name;
        public int Type;
        public byte[] Data;

        public RegValue(string name, int type, byte[] data)
        {
            Name = name;
            Type = type;
            Data = data ?? new byte[0];
        }
    }

    /// <summary>Lesender Zugriff auf HKLM (in Tests ersetzt durch eine Attrappe).</summary>
    internal interface IRegistry
    {
        /// <summary>Öffnet HKLM\&lt;path&gt; (ohne „HKLM\“) zum Lesen. Rückgabe: 0 oder der Win32-Fehler (2 fehlt, 5 verweigert).</summary>
        int Open(string path, out IRegistryKey key);
    }

    /// <summary>Ein geöffneter Schlüssel.</summary>
    internal interface IRegistryKey : IDisposable
    {
        /// <summary>Namen der Unterschlüssel; Rückgabe 0 oder der Win32-Fehler.</summary>
        int SubKeyNames(List<string> names);

        /// <summary>Alle Werte in der Reihenfolge der Registry; Rückgabe 0 oder der Win32-Fehler.</summary>
        int Values(List<RegValue> values);
    }

    /// <summary>
    /// HKLM über advapi32 in der 64-Bit-Ansicht (KEY_READ | KEY_WOW64_64KEY), damit auch ein 32-Bit-Prozess nicht in
    /// WOW6432Node landet. Nur Öffnen, Aufzählen und Lesen.
    /// </summary>
    internal sealed class Win32Registry : IRegistry
    {
        public int Open(string path, out IRegistryKey key)
        {
            key = null;
            IntPtr handle;
            int rc = Native.RegOpenKeyEx(Native.HKEY_LOCAL_MACHINE, path, 0, Native.KEY_READ | Native.KEY_WOW64_64KEY, out handle);
            if (rc != Native.ERROR_SUCCESS) return rc;
            key = new Key(handle);
            return 0;
        }

        private sealed class Key : IRegistryKey
        {
            private IntPtr _handle;

            public Key(IntPtr handle) { _handle = handle; }

            public void Dispose()
            {
                if (_handle != IntPtr.Zero) Native.RegCloseKey(_handle);
                _handle = IntPtr.Zero;
            }

            public int SubKeyNames(List<string> names)
            {
                var buffer = new char[256];
                for (int i = 0; ; i++)
                {
                    int length = buffer.Length;
                    int rc = Native.RegEnumKeyEx(_handle, i, buffer, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (rc == Native.ERROR_NO_MORE_ITEMS) return 0;
                    if (rc == Native.ERROR_MORE_DATA && buffer.Length < 32768) { buffer = new char[buffer.Length * 4]; i--; continue; }
                    if (rc != Native.ERROR_SUCCESS) return rc;
                    names.Add(new string(buffer, 0, length));
                }
            }

            public int Values(List<RegValue> values)
            {
                int subKeys, maxSubKey, maxClass, count, maxName, maxData;
                int rc = Native.RegQueryInfoKey(_handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out subKeys, out maxSubKey, out maxClass,
                    out count, out maxName, out maxData, IntPtr.Zero, IntPtr.Zero);
                if (rc != Native.ERROR_SUCCESS) return rc;
                var name = new char[Math.Max(maxName + 1, 256)];
                var data = new byte[Math.Max(maxData, 256)];
                for (int i = 0; ; i++)
                {
                    int nameLength = name.Length;
                    int dataLength = data.Length;
                    int type;
                    rc = Native.RegEnumValue(_handle, i, name, ref nameLength, IntPtr.Zero, out type, data, ref dataLength);
                    if (rc == Native.ERROR_NO_MORE_ITEMS) return 0;
                    if (rc == Native.ERROR_MORE_DATA)
                    {
                        // Wert ist inzwischen größer geworden: Puffer vergrößern und denselben Index noch einmal lesen
                        // (Registry-Werte sind höchstens wenige MB groß; darüber ist etwas faul)
                        if (data.Length > 64 * 1024 * 1024) return rc;
                        if (name.Length < 32768) name = new char[32768];
                        data = new byte[Math.Max(data.Length * 2, dataLength)];
                        i--;
                        continue;
                    }
                    if (rc != Native.ERROR_SUCCESS) return rc;
                    var bytes = new byte[dataLength];
                    Array.Copy(data, bytes, dataLength);
                    values.Add(new RegValue(new string(name, 0, nameLength), type, bytes));
                }
            }
        }
    }
}
