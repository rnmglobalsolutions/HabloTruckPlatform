#!/usr/bin/env python3
from __future__ import annotations

import sys
import textwrap
from pathlib import Path


PAGE_WIDTH = 612
PAGE_HEIGHT = 792
MARGIN_LEFT = 54
MARGIN_TOP = 54
MARGIN_BOTTOM = 54


def escape_pdf_text(text: str) -> bytes:
    encoded = text.encode("cp1252", errors="replace")
    encoded = encoded.replace(b"\\", b"\\\\").replace(b"(", b"\\(").replace(b")", b"\\)")
    return encoded


def wrap_line(text: str, width: int) -> list[str]:
    stripped = text.rstrip("\n")
    if not stripped:
        return [""]
    return textwrap.wrap(
        stripped,
        width=width,
        break_long_words=True,
        break_on_hyphens=False,
        replace_whitespace=False,
        drop_whitespace=False,
    ) or [""]


def parse_blocks(markdown_text: str) -> list[tuple[str, int, float, str]]:
    blocks: list[tuple[str, int, float, str]] = []
    in_code = False

    for raw_line in markdown_text.splitlines():
        line = raw_line.rstrip()

        if line.startswith("```"):
            in_code = not in_code
            continue

        if in_code:
            for wrapped in wrap_line(line, 86):
                blocks.append(("F3", 9, 11.5, wrapped))
            continue

        if line.startswith("# "):
            text = line[2:].strip()
            for wrapped in wrap_line(text, 44):
                blocks.append(("F2", 18, 22, wrapped))
            blocks.append(("F1", 8, 10, ""))
            continue

        if line.startswith("## "):
            text = line[3:].strip()
            for wrapped in wrap_line(text, 58):
                blocks.append(("F2", 14, 18, wrapped))
            blocks.append(("F1", 6, 8, ""))
            continue

        if line.startswith("---"):
            blocks.append(("F1", 8, 10, ""))
            blocks.append(("F1", 10, 12, "-" * 70))
            blocks.append(("F1", 8, 10, ""))
            continue

        normalized = line.replace("**", "")
        for wrapped in wrap_line(normalized, 90):
            blocks.append(("F1", 10, 13, wrapped))

    return blocks


def build_pages(blocks: list[tuple[str, int, float, str]]) -> list[bytes]:
    pages: list[list[bytes]] = []
    current: list[bytes] = []
    y = PAGE_HEIGHT - MARGIN_TOP

    def new_page() -> None:
        nonlocal current, y
        if current:
            pages.append(current)
        current = []
        y = PAGE_HEIGHT - MARGIN_TOP

    for font, size, leading, text in blocks:
        if y - leading < MARGIN_BOTTOM:
            new_page()

        text_bytes = escape_pdf_text(text)
        cmd = (
            b"BT /" + font.encode("ascii") + b" " + str(size).encode("ascii") + b" Tf "
            + str(MARGIN_LEFT).encode("ascii") + b" "
            + f"{y:.2f}".encode("ascii")
            + b" Td (" + text_bytes + b") Tj ET"
        )
        current.append(cmd)
        y -= leading

    if current:
        pages.append(current)

    return [b"\n".join(page) for page in pages]


def build_pdf(page_streams: list[bytes]) -> bytes:
    objects: list[bytes | None] = [None]

    def add_object(data: bytes) -> int:
        objects.append(data)
        return len(objects) - 1

    catalog_id = add_object(b"<< /Type /Catalog /Pages 2 0 R >>")
    pages_id = add_object(b"")
    font_regular_id = add_object(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
    font_bold_id = add_object(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>")
    font_mono_id = add_object(b"<< /Type /Font /Subtype /Type1 /BaseFont /BaseFont /Courier >>".replace(b"/BaseFont /BaseFont", b"/BaseFont"))

    page_ids: list[int] = []
    for stream in page_streams:
        content_id = add_object(
            b"<< /Length " + str(len(stream)).encode("ascii") + b" >>\nstream\n" + stream + b"\nendstream"
        )
        page_id = add_object(
            b"<< /Type /Page /Parent "
            + str(pages_id).encode("ascii")
            + b" 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 "
            + str(font_regular_id).encode("ascii")
            + b" 0 R /F2 "
            + str(font_bold_id).encode("ascii")
            + b" 0 R /F3 "
            + str(font_mono_id).encode("ascii")
            + b" 0 R >> >> /Contents "
            + str(content_id).encode("ascii")
            + b" 0 R >>"
        )
        page_ids.append(page_id)

    kids = b"[" + b" ".join(f"{page_id} 0 R".encode("ascii") for page_id in page_ids) + b"]"
    objects[pages_id] = b"<< /Type /Pages /Count " + str(len(page_ids)).encode("ascii") + b" /Kids " + kids + b" >>"

    output = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    xref: list[int] = [0]

    for obj_id in range(1, len(objects)):
        xref.append(len(output))
        output.extend(f"{obj_id} 0 obj\n".encode("ascii"))
        output.extend(objects[obj_id] or b"")
        output.extend(b"\nendobj\n")

    xref_start = len(output)
    output.extend(f"xref\n0 {len(objects)}\n".encode("ascii"))
    output.extend(b"0000000000 65535 f \n")
    for offset in xref[1:]:
        output.extend(f"{offset:010d} 00000 n \n".encode("ascii"))

    trailer = (
        b"trailer\n<< /Size "
        + str(len(objects)).encode("ascii")
        + b" /Root "
        + str(catalog_id).encode("ascii")
        + b" 0 R >>\nstartxref\n"
        + str(xref_start).encode("ascii")
        + b"\n%%EOF"
    )
    output.extend(trailer)
    return bytes(output)


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: generate_text_pdf.py <input.md> <output.pdf>", file=sys.stderr)
        return 1

    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])

    markdown_text = src.read_text(encoding="utf-8")
    blocks = parse_blocks(markdown_text)
    pages = build_pages(blocks)
    pdf_bytes = build_pdf(pages)

    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(pdf_bytes)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
