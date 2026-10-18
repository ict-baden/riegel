// SPDX-License-Identifier: MIT
namespace Riegel.Tools.SnapshotAnonymizer;

/// <summary>
/// Entwicklerwerkzeug (Paket collector): anonymisiert aufgezeichnete Exporte zu Fixtures mit einer Erlaubnisliste
/// eingebauter Namen (spec 8.1). Gerüst: meldet sich als noch nicht umgesetzt (Exitcode 3). Nicht ausgeliefert.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        Console.Error.WriteLine($"{Core.Model.RglCodesCommon.NotImplemented}: SnapshotAnonymizer <in> <out>");
        return 3;
    }
}
