#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
#
# Prüft die Linux-Befehlszeile auf einem Minimal-System (debian:12-slim, im Container aufgerufen):
#   1. ohne OpenSSL und ICU: Hilfe läuft, jeder andere Befehl endet mit RGL-0018 und Exitcode 3 (nicht mit 134),
#      Hinweis RGL-0017 auf stderr;
#   2. mit den Paketen aus docs/installation.md (libssl3, libicu72, fonts-dejavu-core): --version, check, plant init,
#      import, report (PDF, CSV, Nachweispaket) und verify, das reproduzieren muss.
# Aufruf: linux-minimal-smoke.sh <Riegel.Cli> <Ordner mit synthetischen Exporten>
set -uo pipefail
cli="$1"
fixtures="$2"
work=$(mktemp -d)
fail() { echo "::error::$*"; exit 1; }

echo "--- ohne OpenSSL und ICU"
"$cli" --help > /dev/null 2> "$work/help.err" || fail "Hilfe ohne OpenSSL endete mit $?"
grep -q 'RGL-0017' "$work/help.err" || fail "kein Hinweis RGL-0017 ohne ICU"
"$cli" --version > /dev/null 2> "$work/version.err"
code=$?
cat "$work/version.err"
[ "$code" -eq 3 ] || fail "--version ohne OpenSSL endete mit $code statt 3"
grep -q 'RGL-0018' "$work/version.err" || fail "keine Meldung RGL-0018 ohne OpenSSL"
"$cli" check --snapshot "$fixtures/win11-24h2-workgroup-admin.snapshot.json" --brief > "$work/brief.out" 2> /dev/null
code=$?
[ "$code" -eq 3 ] || fail "check ohne OpenSSL endete mit $code statt 3"
grep -q '^RIEGEL UNKNOWN' "$work/brief.out" || fail "keine UNKNOWN-Zeile mit --brief ohne OpenSSL"

echo "--- mit libssl3, libicu72 und fonts-dejavu-core"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq > /dev/null || fail "apt-get update"
apt-get install -y -qq --no-install-recommends libssl3 libicu72 fonts-dejavu-core > /dev/null || fail "apt-get install"
"$cli" --version 2> "$work/version.err" || fail "--version endete mit $?"
if [ -s "$work/version.err" ]; then cat "$work/version.err"; fail "Meldungen auf stderr trotz aller Pakete"; fi
"$cli" check --snapshot "$fixtures/win11-24h2-workgroup-admin.snapshot.json" --brief
code=$?
[ "$code" -le 2 ] || fail "check endete mit $code"
"$cli" plant init "$work/plant" --name "CI Debian slim" || fail "plant init endete mit $?"
"$cli" import "$fixtures/win11-24h2-workgroup-admin.snapshot.json" "$fixtures/server2022-domain-member.snapshot.json" --plant "$work/plant" > /dev/null
code=$?
[ "$code" -le 2 ] || fail "import endete mit $code"
"$cli" report --plant "$work/plant" --evidence --format pdf,csv --out "$work/out" || fail "report endete mit $?"
"$cli" verify "$work"/out/Riegel-Evidence_*.zip || fail "verify endete mit $?"
echo "debian:12-slim: ohne Pakete klare Meldungen, mit den dokumentierten Paketen check, report und verify."
