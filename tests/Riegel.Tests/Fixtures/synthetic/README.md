[Riegel](../../../../README.md) › [Development](../../../../docs/development.md) › Synthetic fixtures

# Synthetic fixtures

**Contents:** [Snapshots](#snapshots) · [Built-in bundles](#built-in-bundles) · [Rules for fixtures](#rules-for-fixtures)

Made-up snapshots and built-in-commands bundles for tests. **Nothing here comes from a real machine:** host names,
accounts and SIDs (`S-1-5-21-1000-2000-3000-<rid>`) are invented, addresses are from RFC 5737, the domain is
`corp.example` (RFC 2606). The audit subcategory GUIDs are Windows constants (from `auditpol /list /subcategory:* /v`,
read only). Build and UBR numbers are plausible but were not checked against Microsoft's release history; tests of
lifecycle and update age set their own values.

The files are written by [`generate-fixtures.py`](generate-fixtures.py) (Python 3, standard library only; run it
from the repository root with `python tests/Riegel.Tests/Fixtures/synthetic/generate-fixtures.py .`). The output is
deterministic and committed; `SchemaTests` checks that every snapshot is schema-valid and round-trips byte for byte
through `RiegelJson`. Edit the generator, not the files.

## Snapshots

All with `origin: synthetic`, full plan coverage unless noted.

| File | Machine | What it is for |
|---|---|---|
| `win11-24h2-workgroup-admin` | Windows 11 Pro 24H2 (26100), workgroup, elevated | the hardened reference client |
| `win11-24h2-workgroup-nonadmin` | same machine, standard token | `denied` items and a denied subkey in coverage (Defender exclusions) |
| `server2022-domain-member` | Server 2022 Standard, domain member | domain GPO attribution in `parsed:gpresult`; no Security Center on servers |
| `win10-ltsc2019-hmi` | Windows 10 Enterprise LTSC 2019 (17763), operator station | automatic logon (password redacted), RDP without NLA, PowerShell 2.0 present, SMBv1 client driver, no LSA protection |
| `server2016-historian` | Server 2016 Standard | old update state, no VBS |
| `win7-sp1` | Windows 7 SP1 (7601) under CLR 2 | missing namespaces (Defender, DeviceGuard, SMB) as `namespaceMissing` |
| `server2012r2` | Server 2012 R2 | no Defender, DeviceGuard or Security Center |
| `win10-german-locale` | Windows 10 22H2, German | localised account and group names ("Gast", "Administratoren"); only SIDs may decide |
| `server2022-dc` | Server 2022, domain controller | DC handling (`domainRole` 5) |
| `win11-defender-passive-thirdparty-av` | Windows 11 | Defender in passive mode with a third-party antivirus active |

## Built-in bundles

`PG-ALT-01_20261010.riegel-builtin` (Windows 7 SP1) and `XP-HMI-01_20261010.riegel-builtin` (Windows XP SP3), in the
layout of [file-formats.md › Built-in bundle](../../../../docs/file-formats.md#built-in-commands-bundle):
`secedit.inf`/`secedit.log` and `reg\*.reg` in UTF-16 LE with BOM, `auditpol.csv` (Windows 7 only; XP has no
`auditpol /backup`), `services-start.txt`, `winlogon.txt`, `hostname.txt`, `ver.txt`, `exitcodes.txt` in ASCII with
CRLF. Git keeps them byte for byte (`-text` in `.gitattributes`).

⚠ The XP `reg query` output (header line `! REG.EXE VERSION 3.0`, tab-separated columns) is written from memory and
**not verified** against a real Windows XP. Replace it with a recorded, anonymised sample when one is available
(collector package, `Fixtures/recorded`).

## Rules for fixtures

- No data from real machines, customers or persons – recorded fixtures go to `Fixtures/recorded` and only anonymised
  (`tools/SnapshotAnonymizer`).
- Secrets only in redacted form (`{ "redacted": true }`), never a value.
- `parsed:*` items are allowed here because the origin is `synthetic`; the snapshot reader recomputes them for any
  other origin.
