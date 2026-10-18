// SPDX-License-Identifier: MIT
namespace Riegel.Tests.TestSupport;

/// <summary>Test, der nur unter Windows Sinn ergibt (aus Zutritt); auf anderen Systemen übersprungen.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Nur unter Windows: " + reason;
    }
}
