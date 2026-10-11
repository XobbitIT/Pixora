"""Validate active UI strings and diagnostic templates without game input."""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src/CanvasForge.App"

def literal(source, start):
    interpolated = source.startswith('$"', start)
    if not interpolated and not source.startswith('"', start):
        return None
    i = start + (2 if interpolated else 1)
    result = ""
    parameter = 0
    while i < len(source):
        c = source[i]
        if c == '\\':
            pair = source[i:i+2]
            result += {"\\n": "\n", "\\r": "\r", "\\t": "\t", '\\"': '"', "\\\\": "\\"}.get(pair, pair)
            i += 2
        elif c == '"':
            return result, i + 1
        elif interpolated and source.startswith("{{", i):
            result += "{"; i += 2
        elif interpolated and source.startswith("}}", i):
            result += "}"; i += 2
        elif interpolated and c == "{":
            result += "{p" + str(parameter) + "}"; parameter += 1
            depth = 1; i += 1
            while i < len(source) and depth:
                if source[i] == '"' or source.startswith('$"', i):
                    nested = literal(source, i)
                    if nested is None: return None
                    i = nested[1]; continue
                if source[i] == "{": depth += 1
                elif source[i] == "}": depth -= 1
                i += 1
        else:
            result += c; i += 1
    return None

def inventory():
    mapping = json.loads((APP / "translations.json").read_text(encoding="utf-8-sig"))
    required = set()
    pairs = {}
    for path in APP.glob("*.cs"):
        source = path.read_text(encoding="utf-8-sig")
        for call in re.finditer(r"\bT\(\s*", source):
            first = literal(source, call.end())
            if first is None: continue
            first_text, end = first
            tail = re.match(r"\s*,\s*", source[end:])
            second = literal(source, end + tail.end()) if tail else None
            if second is not None:
                english = second[0]; pairs[first_text] = english; required.add(english)
            elif first_text in mapping:
                required.add(mapping[first_text])
        # Worker status callbacks are localized by the UI after delivery.
        # Walk their balanced arguments; interpolated HEX data remains data.
        for call in re.finditer(r"\b(SetStatus|BeginTiming|ReportTiming|report)\(\s*", source):
            if call[1]=='report' and not path.name.startswith('Painter'):continue
            i=call.end();depth=1
            while i<len(source) and depth:
                value=literal(source,i)
                if value is not None:
                    text,i=value
                    fixed=re.sub(r"\{p\d+\}","",text)
                    if re.search(r"[А-Яа-яІіЇїЄєҐґ]",fixed) or re.search(r"[A-Za-z]",fixed) and (depth==1 or call[1]=='report' and depth==2):
                        required.add(mapping.get(text,text))
                    continue
                if source[i]=='(':depth+=1
                elif source[i]==')':depth-=1
                i+=1
    # Owned failures reach the UI through T(error.Message), so they must be
    # inventoried even when no literal T() occurs at the throw site.
    for folder in (APP, ROOT / "src/CanvasForge.Core"):
        for path in folder.glob("*.cs"):
            source = path.read_text(encoding="utf-8-sig")
            for call in re.finditer(r"\b(?:new\s+|:\s*)(?:InvalidOperationException|InvalidDataException|ArgumentException|NotSupportedException|IOException|TimeoutException)\s*\(\s*", source):
                value = literal(source, call.end())
                if value:
                    required.add(mapping.get(value[0], value[0]))
    # These are emitted as structured diagnostics rather than literal T calls.
    required.update(json.loads((APP / "diagnostic-keys.json").read_text(encoding="utf-8")))
    return required, pairs

def check():
    rows = json.loads((APP / "international.json").read_text(encoding="utf-8-sig"))
    keys = set(); issues = []
    for row in rows:
        if len(row) != 6 or row[0] in keys:
            issues.append("Invalid or duplicate row: " + repr(row)); continue
        keys.add(row[0]); tokens = sorted(re.findall(r"\{p\d+\}", row[0]))
        for translated in row[1:]:
            if not translated.strip() or sorted(re.findall(r"\{p\d+\}", translated)) != tokens:
                issues.append("Missing translation or invalid placeholders: " + row[0])
    required, _ = inventory()
    missing = sorted(required - keys)
    if "--inventory" in sys.argv:
        print(json.dumps(missing,ensure_ascii=False,indent=2)); return
    if missing or issues:
        print(json.dumps({"missing": missing,"invalid":issues},ensure_ascii=False,indent=2)); raise SystemExit(1)
    print(f"PASS {len(required)} active UI/diagnostic keys in five additional languages; {len(keys)} total templates")

if __name__ == "__main__":
    check()
