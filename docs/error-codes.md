[Riegel](../README.md) › Error codes

# Error codes

**Contents:** [RGL-0001–0099](#rgl-00010099) · [RGL-0100–0199](#rgl-01000199) · [RGL-0200–0249](#rgl-02000249) · [RGL-0250–0299](#rgl-02500299) · [RGL-0300–0399](#rgl-03000399) · [RGL-0400–0449](#rgl-04000449) · [RGL-0450–0499](#rgl-04500499) · [RGL-0500–0549](#rgl-05000549) · [RGL-0550–0599](#rgl-05500599) · [RGL-0600–0699](#rgl-06000699)

Every message Riegel shows for an error or a reason starts with a code `RGL-nnnn`. The code is the same in every
language; the text below is the English one (the program shows it in the language of the user interface).
`{0}`, `{1}` … are filled in with details such as a file name. Reason codes of results (for example
"not checkable" because an item was denied) are in the engine range. This page is generated from the source code
(`tests/Riegel.Tests/Windows/Docs/ErrorCodesDocTests.cs`, `RIEGEL_UPDATE_DOCS=1 dotnet test`); a test keeps it current.

## RGL-0001–0099

Common.

| Code | Name | Meaning |
|---|---|---|
| `RGL-0001` | Unexpected | Unexpected error. Please create a diagnostics package and contact support. |
| `RGL-0002` | NotFound | Not found: {0} |
| `RGL-0003` | NotImplemented | Not available yet in this build: {0} |
| `RGL-0004` | InvalidJson | Invalid JSON or wrong structure ({0}) |
| `RGL-0005` | UnsupportedSchema | Unsupported file format: {0} |
| `RGL-0006` | LimitExceeded | Limit exceeded: {0} (maximum {1}) |
| `RGL-0007` | IoError | Read or write error: {0} |
| `RGL-0008` | AccessDenied | Access denied: {0} |
| `RGL-0009` | HashMismatch | Checksum does not match: {0} |
| `RGL-0010` | SettingsUnreadable | Settings could not be read; the defaults apply. |
| `RGL-0011` | DefaultsUnreadable | Machine-wide presets (defaults.json) could not be read and are ignored. |
| `RGL-0012` | PlatformNotSupported | Not possible on this operating system. |
| `RGL-0013` | InvalidArgument | Invalid value: {0} |
| `RGL-0014` | ReadOnlyMode | Read-only mode: nothing is written to the plant folder. |
| `RGL-0015` | DiagnosticsFailed | The diagnostics package could not be written: {0} |
| `RGL-0016` | AlreadyExists | Already exists and is not overwritten: {0} |
| `RGL-0017` | IcuMissing | ICU (libicu) was not found, so Riegel runs without culture data: texts are translated, but dates and numbers are not localised. For localised output install libicu (Debian 12: libicu72, Ubuntu 24.04: libicu74, Fedora and RHEL: libicu). |
| `RGL-0018` | OpenSslMissing | OpenSSL (libssl) was not found. Riegel needs it for SHA-256 checksums. Install it (Debian 12 and Ubuntu 22.04 or later: libssl3, Fedora and RHEL: openssl-libs) and run the command again. |

## RGL-0100–0199

Collector and Windows platform.

| Code | Name | Meaning |
|---|---|---|
| `RGL-0100` | Unexpected | Unexpected error: {0} |
| `RGL-0101` | InvalidArguments | Unknown option: {0} |
| `RGL-0102` | TargetNotWritable | Target not writable - use --out with a share or another folder. Nothing was collected. ({0}) |
| `RGL-0103` | ExportWriteFailed | The export could not be written: {0} |
| `RGL-0104` | PlanUnreadable | The embedded collection plan is unreadable ({0}). Nothing was collected. |
| `RGL-0105` | SidecarWriteFailed | The checksum file could not be written: {0} |
| `RGL-0106` | BuiltinBundleExists | The folder already exists (collect-builtin.cmd) |
| `RGL-0110` | HandoffInvalidToken | Invalid hand-over token. Only letters, digits and "-" (8 to 64 characters) are allowed. |
| `RGL-0111` | HandoffExists | The hand-over folder already exists and is never reused: {0} |
| `RGL-0112` | HandoffReparsePoint | Refused: part of the hand-over path is a link or junction (reparse point): {0} |
| `RGL-0113` | ElevationCancelled | Elevation was cancelled. Nothing was collected. |
| `RGL-0114` | ElevationFailed | Riegel could not be started with administrator rights (error {0}). |
| `RGL-0115` | HandoffNoSnapshot | The elevated collection ended without an export in the hand-over folder (exit code {0}). |
| `RGL-0116` | HandoffDifferentProfile | The elevated collection ran under a different account ("over the shoulder"), so its export landed in that account's profile. Open a command prompt with "Run as administrator" as that account, run Riegel.Cli.exe collect --out &lt;folder&gt; and import the file. |
| `RGL-0120` | ShellOpenRefused | Riegel only opens files it created (PDF, Excel, CSV, JSON, text, ZIP), folders and http/https links: {0} |
| `RGL-0121` | ShellOpenFailed | Could not open {0} (error {1}). |
| `RGL-0130` | CollectorBundleMissing | The collector package is missing next to the app ({0}). The single-file Riegel.exe does not contain it: use the collector package Riegel-Collector-&lt;version&gt;.zip from the release instead. |
| `RGL-0131` | CollectorBundleTargetExists | The target folder already contains {0}. Choose an empty folder. |
| `RGL-0140` | CollectionFailed | Collecting this PC failed ({0}). |

## RGL-0200–0249

Engine (catalogue, facts, evaluation).

| Code | Name | Meaning |
|---|---|---|
| `RGL-0200` | CatalogueInvalid | The rule catalogue is invalid: {0} |
| `RGL-0201` | UnknownCheckType | Unknown check type: {0} |
| `RGL-0202` | UnknownField | Unknown field: {0} |
| `RGL-0203` | OperatorMismatch | Operator does not fit the value type or the expected value: {0} |
| `RGL-0204` | DefaultWithoutSource | Default value without a source (https) or date seen: {0} |
| `RGL-0205` | MissingRuleIncomplete | Rule for missing values is incomplete: {0} |
| `RGL-0206` | UnknownCapability | Unknown capability: {0} |
| `RGL-0207` | UnknownFact | Unknown fact or wrong value type in a condition: {0} |
| `RGL-0208` | DuplicateCapability | Capability defined more than once: {0} |
| `RGL-0209` | DuplicateRuleId | Rule ID occurs more than once: {0} |
| `RGL-0210` | InvalidRule | Invalid rule: {0} |
| `RGL-0211` | InvalidTable | Invalid catalogue table or file: {0} |
| `RGL-0212` | BaselineConflict | Customer baseline does not fit: {0} |
| `RGL-0220` | ItemDenied | Access to the evidence was denied |
| `RGL-0221` | ItemError | The evidence could not be collected (error) |
| `RGL-0222` | ItemNotCollected | The evidence was not collected |
| `RGL-0223` | ItemNamespaceMissing | WMI namespace or class missing |
| `RGL-0224` | ItemTruncated | Evidence truncated or collection area incomplete |
| `RGL-0225` | ItemNotApplicable | The collector marked the evidence as not applicable |
| `RGL-0226` | AbsenceNotProvable | Absence cannot be proven (outside the collected area) |
| `RGL-0227` | NotSetNoDefault | Value not set; the rule does not assume a default |
| `RGL-0228` | DefaultUnknown | Value not set, default value unknown |
| `RGL-0229` | ApplicabilityUnknown | Applicability cannot be determined |
| `RGL-0230` | ConfiguredOnly | Configured, effect not proven |
| `RGL-0231` | HeuristicOnly | Heuristic evidence only |
| `RGL-0232` | ClockSuspect | Clock of the target machine implausible – ages cannot be checked |
| `RGL-0233` | UnexpectedValue | Unexpected value in the raw data |
| `RGL-0234` | RedactedValue | Redacted value cannot be compared |
| `RGL-0235` | CatalogueDataMissing | Catalogue data missing or too old |
| `RGL-0236` | DefenderStateUnknown | State of Microsoft Defender cannot be determined |
| `RGL-0237` | NoDecisiveEvidence | No decisive evidence |
| `RGL-0238` | UnresolvedNames | Account names in user rights not resolved to SIDs |
| `RGL-0239` | EvaluationFailed | Evaluation of the rule failed |
| `RGL-0240` | NotApplicableOs | Not for this kind of operating system |
| `RGL-0241` | NotApplicableBuild | Build outside the range of the rule |
| `RGL-0242` | NotApplicableDomain | Domain membership does not match |
| `RGL-0243` | NotApplicableDomainController | Domain controller – the rule does not apply there |
| `RGL-0244` | NotApplicableCondition | Condition of the rule does not apply |
| `RGL-0245` | NotApplicableCapability | Feature not present on this operating system |
| `RGL-0246` | NotApplicableThirdPartyAv | Third-party antivirus active |
| `RGL-0247` | NotApplicableAbsent | Service, feature or data not present |
| `RGL-0248` | Retired | Rule retired |

## RGL-0250–0299

Parsing (snapshots, built-in bundles, baselines).

| Code | Name | Meaning |
|---|---|---|
| `RGL-0250` | SnapshotNotJson | {0} is not valid JSON (position {1}); the file may be truncated. |
| `RGL-0251` | SnapshotVersion | Unsupported snapshot version {0} (this version reads {1}). |
| `RGL-0252` | SnapshotTooLarge | {0} is larger than allowed (maximum {1} bytes). |
| `RGL-0253` | TooManyItems | {0} contains too many entries (maximum {1}). |
| `RGL-0254` | StringTooLong | {0} contains a text that is too long (maximum {1} characters). |
| `RGL-0255` | JsonTooDeep | {0} is nested too deeply (maximum depth {1}). |
| `RGL-0256` | GzipInvalid | {0} is not a readable gzip file. |
| `RGL-0257` | SnapshotInvalid | {0} is not a valid snapshot ({1}). |
| `RGL-0258` | ParsedItemRecomputed | {0} derived entries (parsed:*) in a collected snapshot were discarded and recomputed from the raw data. |
| `RGL-0259` | RawSourceUnavailable | Raw data {0} is not available; the derived entry could not be computed. |
| `RGL-0260` | BundleInvalid | Not a valid built-in-commands bundle ({0}). A folder named *.riegel-builtin or holding hostname.txt or secedit.inf is read as such a bundle. |
| `RGL-0261` | BundleFileTooLarge | {0} is larger than allowed (maximum {1} bytes). |
| `RGL-0262` | InvalidEncoding | {0} is not valid {1} text. |
| `RGL-0263` | RegFileInvalid | {0}: line {1} is not a valid registry export line. |
| `RGL-0264` | InfInvalid | {0}: line {1} is not valid in a security template. |
| `RGL-0265` | AuditCsvInvalid | {0}: line {1} is not a valid audit policy row. |
| `RGL-0266` | RegQueryInvalid | {0}: line {1} is not valid reg query output. |
| `RGL-0267` | BundleStepFailed | Step {0} ended with exit code {1}; its data is not used. |
| `RGL-0268` | BundlePartMissing | {0} is not part of this built-in-commands bundle. |
| `RGL-0270` | RegistryPolInvalid | {0} is not a valid Registry.pol file (at byte {1}). |
| `RGL-0271` | RawHashMismatch | The SHA-256 of {0} does not match its content. |
| `RGL-0272` | GpresultInvalid | {0} is not valid gpresult XML ({1}). |
| `RGL-0280` | BaselineUnknownFormat | {0}: baseline format not recognised (expected a GPO backup folder, a .json or a .csv file). |
| `RGL-0281` | BaselineInvalid | Baseline invalid at {0} ({1}). |
| `RGL-0282` | BaselineIdInvalid | Invalid baseline or rule ID: {0} |
| `RGL-0283` | BaselineIdCollision | Rule ID {0} occurs twice in the baseline (second time at {1}). |
| `RGL-0284` | BaselineCsvInvalid | {0}: line {1}, column {2} is invalid. |
| `RGL-0285` | BaselineTooLarge | {0} is too large (maximum {1}). |
| `RGL-0286` | BaselineEmpty | {0} contains no computer settings (registry.pol, GptTmpl.inf or audit.csv). |

## RGL-0300–0399

Catalogue content and RuleTool (developer tool).

| Code | Name | Meaning |
|---|---|---|
| `RGL-0300` | Usage | Invalid command line. |
| `RGL-0301` | RepositoryNotFound | Repository not found (no Riegel.sln above the working directory; use --repo). |
| `RGL-0302` | RuleFileInvalid | Rule file cannot be read as riegel.rules/1. |
| `RGL-0303` | RulesDocOutdated | docs/rules.md differs from the generated version; run 'RuleTool rules-doc'. |
| `RGL-0304` | SourceFolderNotFound | Source folder not found. |
| `RGL-0305` | ReferenceNotFound | Reference not found in the local source copies. |
| `RGL-0306` | EvidenceNotInPlan | Evidence key is not covered by contracts/collection-plan.json. |
| `RGL-0307` | PlanInvalid | Collection plan cannot be read. |
| `RGL-0308` | SourceFileSkipped | Source file skipped (too large or unreadable). |

## RGL-0400–0449

Workflow (plant folder, runs, registers).

| Code | Name | Meaning |
|---|---|---|
| `RGL-0401` | ReadOnlyPlant | The plant folder is open in auditor mode (read-only): nothing is written into it. Write outputs with --out. |
| `RGL-0402` | NotAPlant | {0} is not a plant folder (riegel-plant.json is missing). Create one with 'plant init'. |
| `RGL-0403` | ChangedByOthers | {0} was changed by someone else in the meantime. Reload and try again. |
| `RGL-0404` | LockTimeout | The plant folder is locked by another process (waited 10 s). |
| `RGL-0405` | SnapshotConflict | A different export with the same name already exists in the plant folder: {0}. Nothing was overwritten. |
| `RGL-0406` | SidecarMismatch | The checksum file does not match the export {0} (expected {1}, actual {2}). Nothing was imported. |
| `RGL-0407` | InvalidRunId | Not a run ID: {0} (expected yyyyMMddTHHmmssZ-xxxxxx). |
| `RGL-0408` | ApprovalIncomplete | An approval needs the approving role and the approval record (change, QMS or approval-sheet reference). |
| `RGL-0409` | EntryNotFound | There is no entry {0} in the register {1}. |
| `RGL-0410` | ApprovedStateMismatch | The approved state does not match run {0} ({1}). |
| `RGL-0411` | InvalidImportId | Not an import ID: {0} (expected yyyyMMddTHHmmssZ-xxxxxx or 'latest'). |
| `RGL-0412` | InvalidPlant | riegel-plant.json is invalid at {0} (missing, duplicate or unknown entry). |
| `RGL-0413` | MeasuresNeedNoApproval | Measures need no approval; the sign-off of the plant report covers the plan. |
| `RGL-0414` | RawStatusUnknown | The tailoring is no longer in force and the original result is unknown: evaluate the export again. |
| `RGL-0415` | RunSnapshotOutsidePlant | The export of run {0} is not in the plant folder ({1}): only a relative path inside the folder without links is used. |
| `RGL-0416` | RunSnapshotMismatch | The export {0} does not match run {1} (SHA-256 expected {2}, found {3}). It is not used for a rollback, comparison or re-evaluation. |
| `RGL-0420` | DuplicateId | ID {0} is used more than once. |
| `RGL-0421` | IdFormat | ID {0} does not have the form {1}. |
| `RGL-0422` | UnknownRule | Rule {0} is not in the catalogue or in an imported baseline. |
| `RGL-0423` | RuleRetired | Rule {0} is retired; the entry is kept and marked. |
| `RGL-0424` | EmptyScope | The scope is empty: name machines, groups or all machines. |
| `RGL-0425` | UnknownGroup | Group {0} does not exist in the plant. |
| `RGL-0426` | UnknownMachine | Machine {0} is not known in the plant (no entry, no runs). |
| `RGL-0427` | TextTooShort | {0} needs at least {1} characters. |
| `RGL-0428` | ReferenceRequired | Category vendorRequirement needs a reference (vendor document or ticket). |
| `RGL-0429` | CompensatingMeasureRequired | Category compensatingControl needs the compensating measure. |
| `RGL-0430` | AcceptedValueRequired | acceptedValue is required for check rules: the canonical actual value, or * for any value. |
| `RGL-0431` | AcceptedValueAny | acceptedValue * accepts any value: a later change of the value will not be noticed. |
| `RGL-0432` | ApprovalPending | Not approved yet (missing: {0}). The finding stays open, flagged as pending. |
| `RGL-0433` | ApprovalNotBound | The approval fields are filled in but not bound to the content (approvedContentSha256 is missing). Enter the approval with 'exceptions approve' or in the app. |
| `RGL-0434` | ChangedAfterApproval | The entry was changed after approval (the content hash differs). It is pending again until it is approved anew. |
| `RGL-0435` | ReviewByTooLate | reviewBy {0} is more than {1} months after approvedOn {2}: the entry is invalid. |
| `RGL-0436` | ReviewByLong | reviewBy {0} is more than {1} months after approvedOn {2}; a yearly review is recommended. |
| `RGL-0437` | ReviewByBeforeApproval | reviewBy {0} is before approvedOn {1}. |
| `RGL-0438` | Expired | Expired: the review date {0} has passed. The finding is open again. |
| `RGL-0439` | Expiring | Expires on {0} (in {1} days). |
| `RGL-0440` | RiskAcceptanceRole | Accepting the risk of a high-severity rule needs one of the roles {0}; approved by {1}. |
| `RGL-0441` | Overlap | Overlaps with {0} for the same rule: the most specific scope wins, among equals the latest approval. |
| `RGL-0442` | TailoringHighSeverity | Tailoring a high-severity rule: would a justified exception be the better choice? |
| `RGL-0443` | MeasureOverdue | The measure is overdue since {0}. |
| `RGL-0444` | FieldRequired | {0} is required. |
| `RGL-0445` | PlantIdMismatch | plantId {0} in the file does not match the plant {1}. |
| `RGL-0446` | ApprovedInFuture | approvedOn {0} lies in the future: the approval is not in force yet. |
| `RGL-0447` | InventoryRule | Rule {0} is an inventory rule and never fails: the exception has no effect. |
| `RGL-0448` | InvalidNotApproved | Not approved: {0} is invalid ({1}: {2}). Correct the entry first – validate lists every issue. |

## RGL-0450–0499

Command line.

| Code | Name | Meaning |
|---|---|---|
| `RGL-0450` | UsageError | Unknown command or option: {0}. See --help. |
| `RGL-0451` | MissingValue | Option {0} needs a value. |
| `RGL-0452` | InvalidValue | Invalid value for {0}: {1}. Allowed: {2}. |
| `RGL-0453` | MissingOption | {0} needs {1}. |
| `RGL-0454` | ConflictingOptions | {0} and {1} cannot be combined. |
| `RGL-0455` | WindowsOnly | {0} works only on Windows. On this system, evaluate exports instead: {1} |
| `RGL-0456` | InvalidHandoffToken | Invalid hand-over token. |
| `RGL-0457` | ReadOnlyRefused | Read-only mode: {0} would write into the plant folder. Run it without --read-only. |
| `RGL-0458` | ReadOnlyNeedsOut | Read-only mode: nothing is written into the plant folder – give an output folder with --out. |
| `RGL-0459` | NoRuns | No run for machine {0} in this plant. Import or check a snapshot first. |
| `RGL-0460` | NoApprovedState | Machine {0} has no approved state. Approve a run first with approve --machine {0}. |
| `RGL-0461` | NoImports | No import record {0} in this plant. Import exports first: import &lt;file&gt; --plant &lt;folder&gt;. |
| `RGL-0462` | UnknownEntries | Not in the register: {0}. Nothing was changed. |
| `RGL-0463` | UnknownRule | Unknown rule: {0}. |
| `RGL-0464` | BaselineNotFound | Baseline {0} is not in this plant folder. |
| `RGL-0465` | SidecarMismatch | {0}: the SHA-256 does not match its .sha256 file (expected {1}, actual {2}). The export was changed or damaged; Riegel does not use it. |
| `RGL-0466` | ReviewDateMissing | {0} has no review date. Give one with --review-by &lt;yyyy-MM-dd&gt;. |
| `RGL-0467` | RunOfOtherMachine | Run {0} belongs to machine {1}, not to {2}. |
| `RGL-0468` | PlantNotFound | No plant folder at {0} (riegel-plant.json is missing). Create one with plant init &lt;folder&gt; --name &lt;name&gt;. |
| `RGL-0469` | Cancelled | Cancelled. |
| `RGL-0470` | NoExportsInFolder | The folder {0} is not a built-in-commands bundle and contains no exports (*.snapshot.json, *.snapshot.json.gz). |
| `RGL-0471` | UnknownTemplate | No template file and no built-in template named {0}. Built-in: {1}. |

## RGL-0500–0549

PDF documents.

| Code | Name | Meaning |
|---|---|---|
| `RGL-0500` | WriteFailed | The PDF file could not be written: {0} ({1}) |
| `RGL-0501` | NoFont | No font for the PDF documents was found (looked for {0} in {1}). On Linux, install for example fonts-dejavu-core or fonts-liberation. |
| `RGL-0502` | LogoTooLarge | The cover logo {0} is too large ({1} bytes, at most {2}). |
| `RGL-0503` | LogoInvalid | The cover logo {0} is not a valid PNG or JPEG image (at most 4096 × 4096 pixels). |
| `RGL-0504` | LogoUnreadable | The cover logo {0} could not be read ({1}). |
| `RGL-0510` | NoFontForLanguage | No font with Chinese characters (TrueType outlines) was found on this system for language {0}; this document is therefore written in English. Fonts that work: Microsoft YaHei or SimSun (Windows), WenQuanYi Micro Hei (Linux package fonts-wqy-microhei). |
| `RGL-0520` | PdfANotSupported | PDF/A is not available: PDFsharp-MigraDoc 6.2.4 cannot write valid PDF/A from these documents (see docs/reports.md). Write the report without --pdfa. |

## RGL-0550–0599

Data outputs (Excel, CSV, evidence pack, remediation).

| Code | Name | Meaning |
|---|---|---|
| `RGL-0550` | ZipUnreadable | The evidence pack is not a readable ZIP file: {0} |
| `RGL-0551` | ZipTooLarge | The evidence pack is too large ({0} bytes, limit {1}). |
| `RGL-0552` | EntryTooLarge | Entry {0} exceeds the limit of {1} bytes (ZIP bomb?). |
| `RGL-0553` | UnsafeEntryName | Unsafe entry name in the evidence pack: {0} |
| `RGL-0554` | ChecksumFileInvalid | {0} is missing or not in sha256sum format. |
| `RGL-0555` | ManifestInvalid | manifest.json is missing or invalid: {0} |
| `RGL-0556` | FileHashMismatch | Checksum of {0} does not match (expected {1}, found {2}). |
| `RGL-0557` | FileMissing | {0} is listed but missing from the pack. |
| `RGL-0558` | FileNotListed | {0} is in the pack but not listed. |
| `RGL-0559` | CatalogueMismatch | The packed catalogue ({0}) does not match the manifest ({1}). |
| `RGL-0560` | ResultMismatch | {0}: result differs on re-evaluation – {1} |
| `RGL-0561` | SummaryMismatch | Run {0}: counts differ on re-evaluation – {1} |
| `RGL-0562` | BaselineNotVerifiable | Baseline {0}: content not included – not verifiable |
| `RGL-0563` | RunNotVerifiable | Run {0} not verifiable: {1} |
| `RGL-0564` | HostNotInPack | Machine {0} is not in the evidence pack. |
| `RGL-0565` | PackedFileUnreadable | {0} in the pack is not readable ({1}). |
| `RGL-0566` | TooManyEntries | The evidence pack has too many entries ({0}, limit {1}). |
| `RGL-0567` | RunNotPackable | Cannot pack {0}: outside the plant folder, raw built-in bundle, missing, or the checksum differs from the run. |
| `RGL-0568` | DocumentNotPackable | Report file {0} cannot be packed (missing, too large or invalid name). |
| `RGL-0569` | RemediationActionInvalid | Remediation entry in the catalogue not allowed – left out: {0} |
| `RGL-0570` | RollbackValueOmitted | Old value not taken into the rollback: {0} |
| `RGL-0571` | RunManifestMismatch | Run in the pack does not match the manifest ({0} / {1}). |
| `RGL-0572` | CatalogueNotBuiltIn | The packed catalogue {0} ({1}) differs from the released catalogue {0} known to this Riegel version ({2}): its rules were changed. |
| `RGL-0573` | CatalogueNotReference | The packed catalogue {0} is not known to this Riegel version (built in: {1}), so its rules could not be compared with a reference and the pack is not verified. Verify it with the Riegel version that wrote it ({2}) or a newer one. |

## RGL-0600–0699

App (user interface).

| Code | Name | Meaning |
|---|---|---|
| `RGL-0600` | Unexpected | Unexpected error in the user interface. Please create a diagnostics package and contact support. |
| `RGL-0601` | DemoFailed | The demo plant could not be built. |
| `RGL-0602` | ScreenshotFailed | WPF drew an empty picture or the window size is wrong. Start the screenshots in a signed-in, connected Windows session. |
| `RGL-0603` | NoPlant | No plant is open. |
| `RGL-0604` | NotInDemo | Not available in the demo and while creating screenshots. |
| `RGL-0605` | ElevationFailed | The check with administrator rights failed or was cancelled. |
| `RGL-0606` | ElevationOtherProfile | Elevation used a different account: its export lies in that account's profile. Run Riegel.Cli.exe collect in an elevated console and import the export. |
| `RGL-0607` | ImportFailed | An export could not be imported: {0} |
| `RGL-0608` | DocumentFailed | A document could not be written: {0} |
| `RGL-0609` | NothingToImport | Nothing to import: choose exports (.snapshot.json, .json.gz) or built-in bundles. |
| `RGL-0610` | JustificationTooShort | The justification needs at least 20 characters. |
| `RGL-0611` | ReferenceRequired | A vendor requirement needs a reference (vendor document or ticket). |
| `RGL-0612` | CompensatingRequired | A compensating control needs the compensating measure. |
| `RGL-0613` | AcceptedValueRequired | The accepted value is required for check rules. |
| `RGL-0614` | AcceptedValueAny | “*” accepts any value – a later change would go unnoticed. |
| `RGL-0615` | ApprovalIncomplete | The approval needs role, date, review date and record. |
| `RGL-0616` | ReviewTooFar | The review date may be at most 36 months after the approval. |
| `RGL-0617` | ReviewOverYear | The review date is more than 12 months after the approval. |
| `RGL-0618` | ReviewBeforeApproval | The review date lies before the approval date. |
| `RGL-0619` | UnknownRule | The rule is not in the rule catalogue. |
| `RGL-0620` | ReasonTooShort | The reason needs at least 20 characters. |
| `RGL-0621` | TailoringHighSeverity | Tailoring a rule of high severity – would a justified exception fit better? |
| `RGL-0622` | ActionRequired | The measure needs an action. |
| `RGL-0623` | OwnerRoleRequired | The owner role is required. |
| `RGL-0624` | ScopeEmpty | The scope is empty: choose a machine, a group or the whole plant. |
| `RGL-0625` | RiskRoleNotAllowed | “Risk accepted” for a high-severity rule may only be approved by the roles set in the plant. |
| `RGL-0626` | ApprovalRoleRequired | The approving role is required. |
| `RGL-0627` | ApprovalRecordRequired | The approval record is required (change, QMS or sheet number). |
| `RGL-0628` | Overlap | The entry overlaps another one for the same rule. |
| `RGL-0630` | BaselineFailed | The baseline could not be stored or removed. |
| `RGL-0631` | OutputFolderRequired | Choose an output folder (required in auditor mode). |
| `RGL-0632` | OpenFailed | Could not be opened: {0} |
| `RGL-0633` | InvalidHost | Invalid or duplicate machine name: {0} |
| `RGL-0634` | InvalidNumber | Not a valid number of days (1–3650): {0} |

