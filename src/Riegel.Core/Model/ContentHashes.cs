// SPDX-License-Identifier: MIT
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

/// <summary>
/// Inhalts-Hash eines Registereintrags (spec 2.11): SHA-256 über die kompakte kanonische JSON-Form des Eintrags
/// ohne die Freigabefelder (Approval leer). Gemeinsam für Freigabe, Genehmigungsblatt und Prüfung.
/// </summary>
public static class ContentHashes
{
    public static string Of(ExceptionEntry e) => Hashes.Sha256Hex(RiegelJson.SerializeCompact(e with { Approval = Approval.None }));

    public static string Of(TailoringEntry e) => Hashes.Sha256Hex(RiegelJson.SerializeCompact(e with { Approval = Approval.None }));
}
