[Riegel](../../../../README.md) › [Collector](../../../../docs/collector.md) › Recorded fixtures

# Recorded runner fixtures

**Contents:** [What is here](#what-is-here) · [How they were made](#how-they-were-made) · [Anonymisation](#anonymisation) · [Re-recording](#re-recording)

## What is here

Snapshots written by `Riegel.Collector.exe` 0.1.0 (SHA-256 `042f7865c4d24d8a5d3b6cbf4e1c3444b523f581cfb5b35b67478e54e6dd6b49`, the
released collection plan) on disposable GitHub-hosted runners on 2026-10-18 (re-recorded in the integration step after
the plan gained the keys for AUD-30, ENC-07, LIFE-05, NET-05 and the service fallback, and `CurrentVersion` got its value
filter), anonymised, with origin `recorded`:

| File | Runner image | Windows | Token |
|---|---|---|---|
| `windows-2022-admin.snapshot.json` | `windows-2022` 20261012.333.1 | Server 2022 Datacenter, build 20348.5622, x64 | elevated |
| `windows-2022-nonadmin.snapshot.json` | same | same | Safer "Basic User" (as `runas /trustlevel:0x20000`) |
| `windows-2025-admin.snapshot.json` | `windows-2025-vs2026` 20261007.264.1 | Server 2025 Datacenter, build 26100.33438, x64 | elevated |
| `windows-2025-nonadmin.snapshot.json` | same | same | Basic User |
| `windows-11-arm-admin.snapshot.json` | `windows-11-vs2026-arm64` 20261013.183.4 | Windows 11 Enterprise 25H2, build 26200.9457, Arm64 | elevated |
| `windows-11-arm-nonadmin.snapshot.json` | same | same | Basic User |

`raw/<runner>/` holds the raw exports of the same runners as parser fixtures, byte for byte as Windows writes them
(`.gitattributes`: no line-ending conversion): `secedit.inf` (`secedit /export`, UTF-16 LE with BOM), `auditpol.csv`
(`auditpol /backup`) and `gpresult.xml` (`gpresult /scope computer /x`, UTF-16 LE with BOM). On `windows-11-arm`
`gpresult` answers "Access Denied" even to an elevated administrator, so there is no `gpresult.xml` and the
snapshot's `raw:gpresult-xml` item is an error – a real behaviour of that image, kept as it is. In
`windows-11-arm-nonadmin.snapshot.json` the Defender preferences (`MSFT_MpPreference`) are an error: Defender on that
image answered the query with an error during this recording (it did so intermittently in several CI runs), also kept
as it is.

## How they were made

Workflow `collector.yml` with the pull-request label `record-fixtures` (or `workflow_dispatch` with `record=true`): the
collector runs elevated and with a Basic-User token, then `tools/SnapshotAnonymizer` anonymises the snapshots and raw
files **on the runner**, and only the anonymised files are uploaded (artifact, 7 days). They were downloaded with
`gh run download`, every file was reviewed for real data (host names, user names, SIDs, GUIDs, addresses, vendor
names), and then committed.

## Anonymisation

`tools/SnapshotAnonymizer` keeps what its allow-list names (built-in accounts by RID, Windows services by image path,
Windows firewall rule groups, Microsoft software and hardware) and replaces everything else:

- host names (`RUNNER-2022`, `RUNNER-2025`, `RUNNER-11-ARM`), also the image's computer name in the `gpresult` data,
  and DNS suffixes (`example.invalid`);
- local user names (`user-01`, `user-02`; built-in accounts keep their default English names) – only where they are
  accounts (whole values, profile paths, `HOST\name`), never as words in other text;
- the machine SID (`S-1-5-21-1000-2000-3000-<RID>`), the MachineGuid (`00000000-0000-4000-8000-0000000000a1`),
  adapter GUIDs and BitLocker key-protector IDs;
- non-Microsoft software (`Example Software NN`, `Example Vendor`), services outside the Windows folders, firewall
  rules of other programs or without a Windows group, Defender exclusions, shares, gateway addresses (RFC 5737) and
  licence/certificate identifiers (product ID, RDP certificate hash).

Tests (`RecordedFixtureTests`) check that every fixture is schema-valid, has origin `recorded`, contains no other
domain SID, no profile name and no generated computer name, and that each runner has its raw exports.

## Re-recording

Add the label `record-fixtures` to the pull request (or run `collector.yml` manually with `record=true`), download
the artifacts with `gh run download <run-id>`, review every file again, and replace the files here. Never commit
snapshots of a workstation or a customer machine.
