# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Startet ein Programm mit eingeschraenktem Token (Safer-Stufe "Basic User", wie runas /trustlevel:0x20000) und
    wartet auf das Ende - NUR auf GitHub-Runnern, fuer den Lauf des Sammlers ohne Adminrechte (spec 8.9).

.DESCRIPTION
    runas /trustlevel kehrt sofort zurueck und liefert keinen Exitcode. Dieses Skript nutzt dieselbe Safer-API
    (SaferCreateLevel / SaferComputeTokenFromLevel) und CreateProcessAsUser mit dem eingeschraenkten eigenen Token,
    wartet und gibt den Exitcode des Programms zurueck. Die Ausgaben gehen ueber cmd.exe in -Log.

.EXAMPLE
    ./tools/readonly-proof/Start-BasicUser.ps1 -CommandLine '"D:\a\Riegel.Collector.exe" --out "D:\a\out"' -Log D:\a\out\stdout.txt
#>
param(
    [Parameter(Mandatory)] [string] $CommandLine,
    [Parameter(Mandatory)] [string] $Log,
    [int] $TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') {
    [Console]::Error.WriteLine('Start-BasicUser.ps1 runs only on GitHub-hosted runners (GITHUB_ACTIONS=true).')
    exit 2
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class RiegelBasicUser
{
    const int SAFER_SCOPEID_USER = 2, SAFER_LEVELID_NORMALUSER = 0x20000, SAFER_LEVEL_OPEN = 1;
    const int CREATE_NO_WINDOW = 0x08000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferCreateLevel(int scope, int level, int openFlags, out IntPtr handle, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferComputeTokenFromLevel(IntPtr level, IntPtr inToken, out IntPtr outToken, int flags, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferCloseLevel(IntPtr level);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(IntPtr token, string application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, int flags, IntPtr environment, string currentDirectory, ref STARTUPINFO startup, out PROCESS_INFORMATION info);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out int code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);

    public static int Run(string commandLine, int timeoutMs)
    {
        IntPtr level, token;
        if (!SaferCreateLevel(SAFER_SCOPEID_USER, SAFER_LEVELID_NORMALUSER, SAFER_LEVEL_OPEN, out level, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
        try
        {
            if (!SaferComputeTokenFromLevel(level, IntPtr.Zero, out token, 0, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
        }
        finally { SaferCloseLevel(level); }
        var startup = new STARTUPINFO();
        startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        PROCESS_INFORMATION info;
        try
        {
            if (!CreateProcessAsUser(token, null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CREATE_NO_WINDOW, IntPtr.Zero, null, ref startup, out info))
                throw new System.ComponentModel.Win32Exception();
        }
        finally { CloseHandle(token); }
        try
        {
            if (WaitForSingleObject(info.hProcess, (uint)timeoutMs) != 0) throw new TimeoutException("process did not end in time");
            int code;
            GetExitCodeProcess(info.hProcess, out code);
            return code;
        }
        finally { CloseHandle(info.hThread); CloseHandle(info.hProcess); }
    }
}
'@

$cmd = Join-Path $env:SystemRoot 'System32\cmd.exe'
# cmd /c entfernt das erste und letzte Anfuehrungszeichen: deshalb die ganze Zeile in ein weiteres Paar
$code = [RiegelBasicUser]::Run("`"$cmd`" /c `"$CommandLine > `"$Log`" 2>&1`"", $TimeoutSeconds * 1000)
Write-Host "exit code (basic user): $code"
exit $code
