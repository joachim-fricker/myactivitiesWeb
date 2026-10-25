#!/usr/bin/env python3
"""
Repariert Git-Merge-Konflikte (<<<<<<< / ======= / >>>>>>>) in den
Aktivitätsdateien des Repos myactivities.

Aufruf (in WSL, im Klon von myactivities):
    python3 fix_conflicts.py activities                  # nur anzeigen (Probelauf)
    python3 fix_conflicts.py activities --apply          # reparieren, erste Seite (HEAD) behalten
    python3 fix_conflicts.py activities --apply --side theirs   # zweite Seite behalten
"""
import argparse
import json
import pathlib
import sys


def resolve(text: str, side: str) -> str:
    keep = 1 if side == "ours" else 2
    out, state = [], 0          # 0 = normal, 1 = erste Seite, 2 = zweite Seite
    for line in text.splitlines(keepends=True):
        if line.startswith("<<<<<<<"):
            state = 1
        elif line.startswith("=======") and state == 1:
            state = 2
        elif line.startswith(">>>>>>>") and state == 2:
            state = 0
        elif state in (0, keep):
            out.append(line)
    return "".join(out)


def differing_keys(text: str) -> str:
    """Welche Top-Level-Felder unterscheiden sich zwischen den beiden Seiten?"""
    try:
        a, b = json.loads(resolve(text, "ours")), json.loads(resolve(text, "theirs"))
    except json.JSONDecodeError:
        return "(Vergleich nicht möglich)"
    keys = sorted(k for k in set(a) | set(b) if a.get(k) != b.get(k))
    return ", ".join(keys) if keys else "(keine Unterschiede)"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--apply", action="store_true", help="Dateien wirklich ändern")
    ap.add_argument("--side", choices=["ours", "theirs"], default="ours")
    args = ap.parse_args()

    folder = pathlib.Path(args.folder)
    if not folder.is_dir() or not any(folder.glob("*.json")):
        print(f"Keine JSON-Dateien in '{folder.resolve()}' gefunden – richtigen Ordner angeben?")
        return 2
    files = sorted(pathlib.Path(args.folder).glob("*.json")) + sorted(pathlib.Path(args.folder).glob("*.gpx"))
    broken, bad = 0, 0
    for f in files:
        text = f.read_text(encoding="utf-8")
        if "<<<<<<<" not in text:
            continue
        broken += 1
        fixed = resolve(text, args.side)
        if f.suffix == ".json":
            try:
                json.loads(fixed)
            except json.JSONDecodeError as e:
                print(f"FEHLER {f.name}: nach Reparatur kein gültiges JSON ({e})")
                bad += 1
                continue
            if broken <= 10:
                print(f"{f.name}: unterschiedliche Felder: {differing_keys(text)}")
        if args.apply:
            f.write_text(fixed, encoding="utf-8")

    print(f"\n{broken} Dateien mit Konflikten, {bad} nicht reparierbar.")
    print("Repariert." if args.apply else "Probelauf – mit --apply wirklich ändern.")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
