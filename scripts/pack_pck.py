#!/usr/bin/env python3
"""Write a Godot 4 resource pack (PCK format v3) from a directory, without a Godot binary.

usage: pack_pck.py <out.pck> <res-prefix> <dir>
  Every file under <dir> is stored at res://<res-prefix>/<path relative to dir>.
  e.g. pack_pck.py dist/X/X.pck X/localization src/X/localization

The mod only ships plain data (localization JSON), which the game reads through
FileAccess, so no import step is needed. Layout matches the game's own and other mods'
PCKs: 104-byte header padded to 16, files 16-byte aligned with offsets relative to the
file base (flag PACK_REL_FILEBASE), directory at the end. Output is deterministic: files
are sorted by path. The engine version stamped is 4.5.0, the game's major/minor (a pack
newer than the running engine is rejected; patch level does not matter).
"""
import hashlib
import os
import struct
import sys

PACK_FORMAT_VERSION = 3
ENGINE = (4, 5, 0)
PACK_REL_FILEBASE = 2
ALIGN = 16
HEADER_SIZE = 4 + 4 * 5 + 8 * 2 + 4 * 16  # magic, version + engine x3 + flags, base, dir, reserved


def pad(n, align=ALIGN):
    return (align - n % align) % align


def collect(prefix, root):
    files = []
    for dirpath, _, names in os.walk(root):
        for name in names:
            src = os.path.join(dirpath, name)
            rel = os.path.relpath(src, root).replace(os.sep, "/")
            files.append((f"{prefix.strip('/')}/{rel}", src))
    return sorted(files)


def write(out, files):
    file_base = HEADER_SIZE + pad(HEADER_SIZE)
    body = bytearray()
    entries = []
    for res_path, src in files:
        with open(src, "rb") as f:
            data = f.read()
        entries.append((res_path, len(body), len(data), hashlib.md5(data).digest()))
        body += data + b"\0" * pad(len(data))

    directory = bytearray(struct.pack("<I", len(entries)))
    for res_path, offset, size, md5 in entries:
        raw = res_path.encode()
        raw += b"\0" * pad(len(raw), 4)
        directory += struct.pack("<I", len(raw)) + raw
        directory += struct.pack("<QQ", offset, size) + md5 + struct.pack("<I", 0)

    dir_offset = file_base + len(body)
    header = struct.pack("<4sIIIIIQQ", b"GDPC", PACK_FORMAT_VERSION, *ENGINE,
                         PACK_REL_FILEBASE, file_base, dir_offset)
    header += b"\0" * (4 * 16)
    header += b"\0" * pad(len(header))
    assert len(header) == file_base

    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "wb") as f:
        f.write(header + body + directory)


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    out, prefix, root = sys.argv[1:]
    files = collect(prefix, root)
    if not files:
        sys.exit(f"no files under {root}")
    write(out, files)
    for res_path, _ in files:
        print(f"  res://{res_path}")


if __name__ == "__main__":
    main()
