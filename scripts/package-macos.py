"""Assemble and ad-hoc sign an Apple Silicon app from a self-contained publish.

Works on Windows. Uses only Python's standard library plus rcodesign.
ZIP permissions are explicit because Windows filesystem modes are not Unix modes.
"""
import argparse
import hashlib
import json
import os
import plistlib
import re
import shutil
import stat
import struct
import subprocess
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VERSION = "0.2.0"
ARM64 = 0x0100000C
MACH_MAGICS = (b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf")


def slices(data):
    if data[:4] == MACH_MAGICS[0]:
        return [(0, struct.unpack_from("<I", data, 4)[0])]
    if data[:4] in MACH_MAGICS[1:]:
        wide = data[:4] == MACH_MAGICS[2]
        count = struct.unpack_from(">I", data, 4)[0]
        result = []
        for i in range(count):
            at = 8 + i * (32 if wide else 20)
            cpu = struct.unpack_from(">I", data, at)[0]
            offset = struct.unpack_from(">Q" if wide else ">I", data, at + 8)[0]
            result.append((offset, cpu))
        return result
    return []


def inspect_native(path):
    data = path.read_bytes()
    architectures = slices(data)
    if not architectures:
        return None
    assert ARM64 in [cpu for _, cpu in architectures], f"No ARM64 slice: {path}"
    dependencies = []
    cdhashes = []
    for offset, cpu in architectures:
        commands = struct.unpack_from("<I", data, offset + 16)[0]
        at = offset + 32
        signature = False
        for _ in range(commands):
            command, length = struct.unpack_from("<II", data, at)
            assert length >= 8
            if command == 0x1D:  # LC_CODE_SIGNATURE
                signature = True
                start = offset + struct.unpack_from("<I", data, at + 8)[0]
                magic, _, count = struct.unpack_from(">III", data, start)
                assert magic == 0xFADE0CC0
                for slot_index in range(count):
                    slot, blob_offset = struct.unpack_from(">II", data, start + 12 + slot_index * 8)
                    if slot != 0 and not 0x1000 <= slot <= 0x1005:
                        continue
                    cd = start + blob_offset
                    cd_magic, cd_length, version, flags, hash_offset, identifier, special_count, code_count, code_limit = struct.unpack_from(">9I", data, cd)
                    assert cd_magic == 0xFADE0C02 and flags & 2, f"Expected ad-hoc CodeDirectory: {path}"
                    hash_size, hash_type, _, page_shift = struct.unpack_from("4B", data, cd + 36)
                    if version >= 0x20300 and not code_limit:
                        code_limit = struct.unpack_from(">Q", data, cd + 56)[0]
                    algorithm = {1: "sha1", 2: "sha256", 3: "sha256", 4: "sha384"}[hash_type]
                    page_size = 1 << page_shift
                    assert code_count == (code_limit + page_size - 1) // page_size
                    for page_index in range(code_count):
                        page_start = offset + page_index * page_size
                        page_end = offset + min((page_index + 1) * page_size, code_limit)
                        actual = hashlib.new(algorithm, data[page_start:page_end]).digest()[:hash_size]
                        hash_at = cd + hash_offset + page_index * hash_size
                        assert actual == data[hash_at:hash_at + hash_size], f"Code signature page mismatch: {path}, page {page_index}"
                    cdhashes.append(hashlib.new(algorithm, data[cd:cd + cd_length]).digest()[:20].hex())
            if command in (0xC, 0x80000018, 0x8000001F):
                name_at = at + struct.unpack_from("<I", data, at + 8)[0]
                name = data[name_at:data.index(b"\0", name_at)].decode()
                dependencies.append(name)
                assert name.startswith(("/usr/lib/", "/System/Library/", "@rpath/", "@loader_path/", "@executable_path/")), f"Nonportable dependency {name}"
            if command == 0x32:  # LC_BUILD_VERSION
                platform, minimum = struct.unpack_from("<II", data, at + 8)
                assert platform == 1 and minimum <= (14 << 16), f"Unsupported minimum OS in {path}"
            at += length
        assert signature, f"Missing ARM64 code signature: {path}"
    return {"file": path.name, "arm64": True, "dependencies": sorted(set(dependencies)), "cdhashes": cdhashes}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--publish", required=True, type=Path)
    parser.add_argument("--signer", default="rcodesign")
    args = parser.parse_args()
    artifacts = ROOT / "artifacts"
    artifacts.mkdir(exist_ok=True)
    stage = Path(tempfile.mkdtemp(prefix="mac-bundle-", dir=artifacts))
    app = stage / "DialShift.app"
    contents = app / "Contents"
    macos = contents / "MacOS"
    resources = contents / "Resources"
    shutil.copytree(args.publish, macos)
    resources.mkdir(parents=True)
    assert (macos / "DialShift").exists(), "Self-contained Mac apphost missing"
    # .NET's macOS single-file host statically links the native runtime.
    assert (macos / "DialShift").stat().st_size > 30_000_000, "Self-contained runtime missing"
    assert not list(macos.rglob("*.exe")), "Windows binaries in Mac package"
    assert not list(macos.rglob("*vlc*")), "Mac build must use system AVPlayer"
    assert not list(macos.rglob("settings.json")), "Personal data in publish directory"
    assert not list(macos.glob("*.dll")), "Use PublishSingleFile=true so managed assemblies are sealed inside the apphost"
    # Apple reserves MacOS for native code. Non-code publish notices live in Resources.
    for path in list(macos.iterdir()):
        if path.is_file():
            with path.open("rb") as file:
                magic = file.read(4)
            if magic not in MACH_MAGICS:
                shutil.move(path, resources / path.name)
    with (contents / "Info.plist").open("wb") as file:
        plistlib.dump({
            "CFBundleName": "DialShift", "CFBundleDisplayName": "DialShift",
            "CFBundleIdentifier": "com.dialshift.radio", "CFBundleExecutable": "DialShift",
            "CFBundlePackageType": "APPL", "CFBundleShortVersionString": "0.2.0",
            "CFBundleVersion": "4", "CFBundleIconFile": "DialShift.icns",
            "LSMinimumSystemVersion": "14.0", "NSHighResolutionCapable": True,
            "NSPrincipalClass": "NSApplication", "LSMultipleInstancesProhibited": True,
            "NSAppTransportSecurity": {"NSAllowsArbitraryLoadsForMedia": True},
            "NSHumanReadableCopyright": "DialShift contributors",
        }, file)
    png = (ROOT / "DialShift.Desktop/Assets/icon-512.png").read_bytes()
    icon = b"ic09" + struct.pack(">I", len(png) + 8) + png
    (resources / "DialShift.icns").write_bytes(b"icns" + struct.pack(">I", len(icon) + 8) + icon)
    shutil.copy2(ROOT / "MACOS.md", stage / "READ-ME-FIRST.md")
    shutil.copy2(ROOT / "MACOS.md", resources / "MACOS.md")
    shutil.copy2(ROOT / "THIRD-PARTY-NOTICES.md", resources / "THIRD-PARTY-NOTICES.md")
    shutil.copytree(ROOT / "licenses", resources / "licenses")
    # Allow .NET's JIT if hardened-runtime options are added to a later signed build.
    entitlement = stage / "entitlements.plist"
    with entitlement.open("wb") as file:
        plistlib.dump({"com.apple.security.cs.allow-jit": True}, file)
    log = stage / "signing.log"
    with log.open("w", encoding="utf-8") as file:
        subprocess.run([args.signer, "sign", "--timestamp-url", "none", "--entitlements-xml-file", os.path.relpath(entitlement), str(app)], check=True, stdout=file, stderr=subprocess.STDOUT)
    # Signing must not shift .NET's embedded assemblies or their absolute offsets.
    original = (args.publish / "DialShift").read_bytes()
    signed = (macos / "DialShift").read_bytes()
    metadata_offsets = [m.start() for m in re.finditer(b"BSJB", original)]
    assert len(metadata_offsets) > 50, "Managed runtime metadata missing"
    assert metadata_offsets == [m.start() for m in re.finditer(b"BSJB", signed)], "Signing shifted bundled metadata"
    at = 32
    for _ in range(struct.unpack_from("<I", original, 16)[0]):
        command, length = struct.unpack_from("<II", original, at)
        if command == 0x1D:
            signature_offset = struct.unpack_from("<I", original, at + 8)[0]
            assert original[metadata_offsets[0]:signature_offset] == signed[metadata_offsets[0]:signature_offset], "Signing changed bundled payload"
        at += length
    native = []
    for path in sorted(macos.rglob("*")):
        if not path.is_file():
            continue
        result = inspect_native(path)
        if result:
            native.append(result)
    assert {n["file"] for n in native} >= {"DialShift", "libAvaloniaNative.dylib", "libHarfBuzzSharp.dylib", "libSkiaSharp.dylib"}, "Native dependencies missing"
    sealed = plistlib.loads((contents / "_CodeSignature/CodeResources").read_bytes())
    for name, seal in sealed["files2"].items():
        path = contents / name
        if "hash2" in seal:
            assert hashlib.sha256(path.read_bytes()).digest() == seal["hash2"], f"Resource hash mismatch: {name}"
        if "cdhash" in seal:
            matching = next(n for n in native if n["file"] == path.name)
            assert seal["cdhash"].hex() in matching["cdhashes"], f"Native seal mismatch: {name}"
    output = artifacts / f"DialShift-{VERSION}-osx-arm64.zip"
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(app.rglob("*")):
            relative = path.relative_to(stage).as_posix()
            entry = zipfile.ZipInfo(relative + ("/" if path.is_dir() else ""))
            entry.create_system = 3
            if path.is_dir():
                entry.external_attr = ((stat.S_IFDIR | 0o755) << 16) | 0x10
                archive.writestr(entry, b"")
            else:
                executable = path.parent == macos and any(n["file"] == path.name for n in native)
                entry.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, path.read_bytes())
        archive.write(stage / "READ-ME-FIRST.md", "READ-ME-FIRST.md")
    with zipfile.ZipFile(output) as archive:
        executable = archive.getinfo("DialShift.app/Contents/MacOS/DialShift")
        assert executable.create_system == 3 and ((executable.external_attr >> 16) & 0o111) == 0o111
        assert archive.testzip() is None
    checksum = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(".zip.sha256").write_text(f"{checksum}  {output.name}\n", encoding="utf-8")
    report = {"archive": str(output), "bundle": str(app), "sha256": checksum, "native_binaries": native,
              "signature_verification": "CodeDirectory page hashes and resource seals passed (ad-hoc); Apple codesign/Gatekeeper not tested", "unix_executable_permissions": "passed", "embedded_dotnet_payload_preserved": True, "mac_runtime_tested": False}
    (artifacts / "mac-package-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Built {output} ({output.stat().st_size / 1024 / 1024:.1f} MiB)")
    print(f"Verified {len(native)} native ARM64 binaries, signatures, dependencies and archive permissions.")


if __name__ == "__main__":
    main()
