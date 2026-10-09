"""Verify the final portable ZIP using Python's standard library only."""
import argparse
import hashlib
import io
import json
import re
import zipfile
from pathlib import PurePosixPath
from xml.etree import ElementTree


def verify(path, version, commit=None, base_commit=None):
    with zipfile.ZipFile(path) as archive:
        entries = [entry for entry in archive.infolist() if not entry.is_dir()]
        names = [entry.filename for entry in entries]
        assert len({name.casefold() for name in names}) == len(names), "Duplicate ZIP entry"
        for name in names:
            parts = PurePosixPath(name).parts
            assert name and not name.startswith("/") and ".." not in parts and "\\" not in name and ":" not in name, "Unsafe ZIP path"
        assert archive.testzip() is None, "ZIP CRC mismatch"
        manifest = json.loads(archive.read("Build_Manifest.json").decode("utf-8-sig"))
        assert manifest["version"] == version, "Manifest version mismatch"
        if commit is not None:
            assert not manifest["localChanges"] and manifest["commit"] == commit, "Exact commit mismatch"
        if base_commit is not None:
            assert manifest["localChanges"] and manifest["commit"] is None and manifest["baseCommit"] == base_commit, "Local build base mismatch"
        if not manifest["localChanges"]:
            assert re.fullmatch(r"[0-9a-f]{40}", manifest["commit"] or ""), "Missing release commit"
        hashes = manifest["filesSha256"]
        assert set(hashes) == set(names) - {"Build_Manifest.json"}, "Package hash inventory differs"
        for name, expected in hashes.items():
            assert hashlib.sha256(archive.read(name)).hexdigest() == expected, "File hash mismatch: " + name
        sources = manifest["sourceFilesSha256"]
        assert {"Source/" + name for name in sources} == {name for name in names if name.startswith("Source/")}, "Source inventory differs"
        projects = {name for name in sources if name.endswith(".csproj")}
        assert projects == {"src/CanvasForge.Core/CanvasForge.Core.csproj", "src/CanvasForge.App/CanvasForge.App.csproj",
                            "tests/CanvasForge.Core.Tests/CanvasForge.Core.Tests.csproj", "tests/CanvasForge.App.Tests/CanvasForge.App.Tests.csproj"}, "Four source projects required"
        assert any(name.endswith(".cs") for name in sources), "C# source missing"
        for name, expected in sources.items():
            assert hashlib.sha256(archive.read("Source/" + name)).hexdigest() == expected, "Source hash mismatch: " + name
        props = ElementTree.fromstring(archive.read("Source/Directory.Build.props"))
        assert props.findtext("PropertyGroup/Version") == version, "Source version mismatch"
        assert manifest["packagedCoreChecksPassed"] and manifest["packagedWpfChecksPassed"], "Published DLL checks missing"
        return {"version": version, "sourceFiles": len(sources), "hashedFiles": len(hashes),
                "commit": manifest["commit"], "localChanges": manifest["localChanges"], "crc": "passed"}


def self_test():
    files = {"Source/Directory.Build.props": b"<Project><PropertyGroup><Version>1.0.14-beta.51</Version></PropertyGroup></Project>",
             "Pixora.exe": b"fixture", "Source/src/CanvasForge.Core/Test.cs": b"// fixture"}
    for area in ("src", "tests"):
        for project in ("CanvasForge.Core", "CanvasForge.App"):
            name = project + (".Tests" if area == "tests" else "")
            files[f"Source/{area}/{name}/{name}.csproj"] = b"<Project/>"
    base = "a" * 40
    manifest = {"version": "1.0.14-beta.51", "commit": None, "baseCommit": base, "localChanges": True,
                "packagedCoreChecksPassed": True, "packagedWpfChecksPassed": True,
                "filesSha256": {n: hashlib.sha256(b).hexdigest() for n, b in files.items()},
                "sourceFilesSha256": {n[7:]: hashlib.sha256(b).hexdigest() for n, b in files.items() if n.startswith("Source/")}}
    def zipped(data, m=manifest):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_STORED) as archive:
            for name, value in data.items(): archive.writestr(name, value)
            archive.writestr("Build_Manifest.json", json.dumps(m))
        stream.seek(0)
        return stream
    verify(zipped(files), "1.0.14-beta.51", base_commit=base)
    failures = [({n: b for n, b in files.items() if not n.endswith("Core.csproj")}, manifest, {}),
                ({**files, "Pixora.exe": b"tampered"}, manifest, {}),
                (files, {**manifest, "sourceFilesSha256": {}}, {}),
                (files, manifest, {"commit": "b" * 40}),
                (files, manifest, {"base_commit": "b" * 40}),
                (files, {**manifest, "version": "wrong"}, {}),
                ({**files, "../escape": b"unsafe"}, manifest, {})]
    for data, m, args in failures:
        try: verify(zipped(data, m), "1.0.14-beta.51", **args)
        except (AssertionError, KeyError): pass
        else: raise AssertionError("Invalid ZIP accepted")
    damaged = bytearray(zipped(files).getvalue())
    at = damaged.index(b"// fixture"); damaged[at] ^= 1
    try: verify(io.BytesIO(damaged), "1.0.14-beta.51")
    except (AssertionError, zipfile.BadZipFile): pass
    else: raise AssertionError("Corrupt CRC accepted")
    print("PASS final ZIP validator: valid fixture and eight rejection cases")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", nargs="?")
    parser.add_argument("--version")
    parser.add_argument("--commit")
    parser.add_argument("--base-commit")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test: self_test()
    if args.archive:
        if not args.version: parser.error("--version required for a ZIP")
        print(json.dumps(verify(args.archive, args.version, args.commit, args.base_commit)))
    elif not args.self_test: parser.error("archive or --self-test required")
