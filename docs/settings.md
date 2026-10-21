[Riegel](../README.md) › Settings

# Settings

**Contents:** [Where settings live](#where-settings-live) · [settings.json](#settingsjson) ·
[defaults.json for all users](#defaultsjson-for-all-users) · [Which value wins](#which-value-wins) ·
[Plant settings](#plant-settings) · [Environment variables](#environment-variables) · [Data folder](#data-folder)

Riegel keeps very little per user: language, theme, a default folder, report language, the defaults for new plants and
the list of recently opened plants. Everything about a plant – groups, machines, thresholds, sign-off roles – lives in
the plant folder itself. The [Settings page](usage.md#settings) edits both.

## Where settings live

| File | Location | Written by |
|---|---|---|
| `settings.json` | data folder: `%LocalAppData%\Riegel` (Windows), `~/.local/share/riegel` (Linux, `$XDG_DATA_HOME` honoured), `~/Library/Application Support/Riegel` (macOS) | the app, when you change a setting |
| `defaults.json` | `%ProgramData%\ICT-Baden\Riegel` (Windows), `/etc/ictbaden/riegel` (Linux) | administrators (GPO, Intune, SOTI); Riegel only reads it |
| `riegel-plant.json` | the plant folder | the app, only on *Save in the plant* or when you edit groups and machines |

Both JSON files are UTF-8, at most 1 MB. A missing file means "nothing set"; an unreadable file is ignored and noted
in the log (`RGL-0010` for `settings.json`, `RGL-0011` for `defaults.json`) – Riegel never fails to start because of
it.

## settings.json

All keys are optional; a missing key or `null` means "not set".

| Key | Values | Meaning | Built-in default |
|---|---|---|---|
| `language` | `en`, `de`, `es`, `fr`, `zh-Hans`, or `""` | language of the user interface; `""` follows Windows | Windows language, else English |
| `theme` | `light`, `dark` | theme of the window; not set follows Windows | follows Windows |
| `defaultPlantFolder` | absolute path | where *Open plant* and *New plant* start | – |
| `reportLanguage` | `en`, `de`, `es`, `fr`, `zh-Hans` | default language of PDF and Excel documents | the UI language |
| `warnDays` | days | new plants: exceptions count as "expiring soon" this many days before their review date | 30 |
| `staleDays` | days | new plants: an export older than this is "outdated" and every report says so | 30 |
| `updateMaxDays` | days | new plants: maximum days since the last cumulative update (update-age rule) | 35 |
| `unusedAccountDays` | days | new plants: enabled local accounts without a logon for this long are listed | 90 |
| `recentPlants` | list of paths | recently opened plant folders (at most 8; only in `settings.json`) | – |

The Settings page accepts 1 to 3650 days. Example:

```json
{
  "language": "de",
  "theme": "dark",
  "defaultPlantFolder": "D:\\Anlagen",
  "reportLanguage": "de",
  "warnDays": 45
}
```

The demo plant is never added to `recentPlants`; `--demo` and `--screenshots` never write `settings.json`.

## defaults.json for all users

`defaults.json` takes the same keys (except `recentPlants`) and presets them for every user of the machine – for example
a company-wide report language or longer warning periods. Distribute it with a GPO file preference, an Intune
Win32 app or SOTI; Riegel never writes it.

```json
{
  "language": "de",
  "reportLanguage": "de",
  "defaultPlantFolder": "\\\\fileserver\\ot\\Anlagen",
  "warnDays": 60,
  "staleDays": 45
}
```

The Settings page marks every value that comes from `defaults.json` as *preset* and shows where the file is read from.

## Which value wins

For each key: the user's value in `settings.json`, else the preset from `defaults.json`, else the built-in default.
The language has one more step in front: the environment variable `RIEGEL_LANGUAGE` (see below). Report languages
other than English and German are machine pre-translations; the language lists say "machine translation" next to
them, and such documents say so on the cover and in every footer.

## Plant settings

`warnDays`, `staleDays`, `updateMaxDays` and `unusedAccountDays` from the user settings are only **defaults for new
plants**. Each plant keeps its own values in `riegel-plant.json` under `settings`, and those are what the evaluation and
the reports use:

| Key in `settings` | Set on | Meaning |
|---|---|---|
| `warnDays`, `staleDays`, `updateMaxDays`, `unusedAccountDays` | Settings page, lower part | as above, for this plant |
| `operatorRole` | Settings page | role of the operator, used in the import dialog and the evidence pack |
| `reportLanguage` | Settings or Reports page | language of this plant's documents |
| `signOff` | Reports page | sign-off steps (prepared, reviewed, approved, accepted) with roles |
| `reportPreset` | Reports page | `audit` or `asDelivered` (machine builder) |
| `confidentiality`, `performedBy`, `coverLogoFile` | Reports page | cover of the plant report; the logo (PNG/JPEG, at most 1 MB) stays in the plant folder |
| `riskAcceptanceRoles` | `riegel-plant.json` | roles allowed to approve "risk accepted" for high-severity rules; empty = every role |

New values count from the next evaluation (*Evaluate again* on the Machines page). The full file format is in
[File formats](file-formats.md#plant-folder).

## Environment variables

| Variable | Effect |
|---|---|
| `RIEGEL_LANGUAGE` | language of the user interface and the command line (`en`, `de`, `es`, `fr`, `zh-Hans`); wins over every setting – used for the screenshots of all languages |
| `RIEGEL_DATA_DIR` | absolute path of the data folder instead of the default (scripts, tests, screenshots); `settings.json` and the logs go there |

## Data folder

The data folder holds `settings.json`, `logs/riegel-<yyyy-MM-dd>.log` (7 days, at most 1 MB per day; never setting
values, host names, accounts or paths) and the hand-over folder of *Check as administrator*. *Open folder* on the
About page shows it. Deleting the folder resets Riegel; plant folders are not affected.
