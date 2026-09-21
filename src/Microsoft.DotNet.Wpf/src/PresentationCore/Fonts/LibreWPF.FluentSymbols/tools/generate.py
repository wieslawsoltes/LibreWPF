#!/usr/bin/env python3
"""Build LibreWPF Fluent Symbols from pinned, redistributable sources.

The generated font preserves the open Uno legacy cmap and adds every Gallery
code point that is absent there from an explicitly reviewed Fluent System Icons
mapping.  No Segoe font binary or outline is read by this tool.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

from fontTools import __version__ as fonttools_version
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont


FONTTOOLS_VERSION = "4.59.1"
FAMILY_NAME = "LibreWPF Fluent Symbols"
POSTSCRIPT_NAME = "LibreWPFFluentSymbols"
OUTPUT_NAME = "LibreWPF.FluentSymbols.ttf"
FIXED_FONT_TIMESTAMP = 3_849_868_800  # 2026-01-01 00:00:00 UTC, seconds since 1904.

SOURCES = {
    "uno": {
        "filename": "uno.ttf",
        "url": "https://raw.githubusercontent.com/unoplatform/uno.fonts/ae06dc8d52ec90c4e050fd2f161711512deb0ba1/webfonts/Uno%20Fluent%20Icons/uno-fluentui-assets.ttf",
        "sha256": "2573f53b71ebee599dfc94c60e1ea848d6d20e7e777d2c65d063822850e738b6",
    },
    "fluent": {
        "filename": "fluent.ttf",
        "url": "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/32374ae9ccf107e026db0d9aa9c0d631328b8003/fonts/FluentSystemIcons-Regular.ttf",
        "sha256": "d72f64599dfe4bb44be1686ba879a018da7471e72372b6a545c24a10648293e0",
    },
    "fluentMetadata": {
        "filename": "FluentSystemIcons-Regular.json",
        "url": "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/32374ae9ccf107e026db0d9aa9c0d631328b8003/fonts/FluentSystemIcons-Regular.json",
        "sha256": "e3c7f72f1a0d1cb58ba2224dd825a9996ecaab5828ba16e1a9192b44e4f8100e",
    },
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def acquire_source(cache: Path, source_name: str) -> Path:
    source = SOURCES[source_name]
    target = cache / source["filename"]
    if not target.exists() or sha256(target) != source["sha256"]:
        cache.mkdir(parents=True, exist_ok=True)
        temporary = target.with_suffix(".download")
        with urllib.request.urlopen(source["url"], timeout=120) as response:
            temporary.write_bytes(response.read())
        if sha256(temporary) != source["sha256"]:
            temporary.unlink(missing_ok=True)
            raise RuntimeError(f"Downloaded {source_name} font failed its pinned SHA-256 check.")
        temporary.replace(target)
    return target


def update_name_table(font: TTFont) -> None:
    values = {
        0: (
            "LibreWPF Fluent Symbols is derived from Uno Fluent Icons (Apache-2.0) "
            "and Microsoft Fluent System Icons (MIT); modified by the LibreWPF project."
        ),
        1: FAMILY_NAME,
        2: "Regular",
        3: "LibreWPF Fluent Symbols Regular 1.0",
        4: FAMILY_NAME,
        5: "Version 1.000",
        6: POSTSCRIPT_NAME,
        13: "Apache-2.0 and MIT; see the LibreWPF Fluent Symbols NOTICE and source manifest.",
        14: "https://github.com/wieslawsoltes/wpf",
        16: FAMILY_NAME,
        17: "Regular",
    }
    table = font["name"]
    retained = [record for record in table.names if record.nameID not in values]
    table.names = retained
    for name_id, value in values.items():
        table.setName(value, name_id, 3, 1, 0x0409)
        table.setName(value, name_id, 1, 0, 0)


def add_source_glyph(
    destination: TTFont,
    source: TTFont,
    source_glyph_name: str,
    destination_glyph_name: str,
) -> None:
    destination_units = destination["head"].unitsPerEm
    source_units = source["head"].unitsPerEm
    scale = destination_units / source_units
    pen = TTGlyphPen(None)
    source.getGlyphSet()[source_glyph_name].draw(TransformPen(pen, (scale, 0, 0, scale, 0, 0)))
    destination["glyf"][destination_glyph_name] = pen.glyph()

    source_advance, source_lsb = source["hmtx"][source_glyph_name]
    destination["hmtx"][destination_glyph_name] = (
        round(source_advance * scale),
        round(source_lsb * scale),
    )


def build_font(
    base_path: Path,
    fluent_path: Path,
    fluent_metadata_path: Path,
    mapping_path: Path,
    output_path: Path,
) -> None:
    mapping = json.loads(mapping_path.read_text(encoding="utf-8"))
    if mapping.get("schemaVersion") != 1:
        raise RuntimeError("Unsupported legacy glyph mapping schema.")

    expected = {
        int(item["codepoint"], 16): item["legacyName"]
        for item in mapping["legacyGlyphs"]
    }
    additions = {
        int(item["codepoint"], 16): item
        for item in mapping["entries"]
    }
    if len(expected) != 1_475 or len(additions) != 282:
        raise RuntimeError(
            f"Expected the reviewed 1,475-code-point catalog and 282 additions; "
            f"found {len(expected)} and {len(additions)}."
        )

    destination = TTFont(base_path, recalcBBoxes=True, recalcTimestamp=False)
    source = TTFont(fluent_path, recalcBBoxes=True, recalcTimestamp=False)
    source_metadata = json.loads(fluent_metadata_path.read_text(encoding="utf-8"))
    source_cmap = source.getBestCmap()
    destination_cmap = destination.getBestCmap()

    missing = set(expected).difference(destination_cmap)
    if missing != set(additions):
        raise RuntimeError(
            "Reviewed additions do not exactly match the legacy glyphs missing from the pinned Uno font."
        )

    # Keep a detached order list.  glyf.__setitem__ also updates its internal
    # order, so sharing that list would append every imported glyph twice.
    glyph_order = list(destination.getGlyphOrder())
    imported: dict[str, str] = {}
    for codepoint in sorted(additions):
        entry = additions[codepoint]
        if entry["legacyName"] != expected[codepoint]:
            raise RuntimeError(f"Legacy name mismatch at U+{codepoint:04X}.")
        source_codepoint = int(entry["sourceCodepoint"], 16)
        if source_metadata.get(entry["sourceGlyph"]) != source_codepoint:
            raise RuntimeError(
                f"Selected semantic glyph {entry['sourceGlyph']} does not match its pinned metadata codepoint."
            )
        source_glyph = source_cmap.get(source_codepoint)
        if source_glyph is None:
            raise RuntimeError(f"Missing selected source glyph {entry['sourceGlyph']}.")
        selected_name = entry["sourceGlyph"]
        selected_cmap_name = source_cmap[source_codepoint]
        selected_name = selected_cmap_name

        destination_name = imported.get(selected_name)
        if destination_name is None:
            destination_name = f"librewpf.{len(imported):04d}"
            imported[selected_name] = destination_name
            add_source_glyph(destination, source, selected_name, destination_name)
            glyph_order.append(destination_name)

        for cmap_table in destination["cmap"].tables:
            if cmap_table.isUnicode():
                cmap_table.cmap[codepoint] = destination_name

    destination.setGlyphOrder(glyph_order)
    destination["glyf"].glyphOrder = glyph_order
    destination["maxp"].numGlyphs = len(glyph_order)
    destination["hhea"].numberOfHMetrics = len(glyph_order)
    destination["head"].created = FIXED_FONT_TIMESTAMP
    destination["head"].modified = FIXED_FONT_TIMESTAMP
    destination["OS/2"].fsType = 0
    if "post" in destination:
        destination["post"].formatType = 3.0
    update_name_table(destination)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    if len(destination["glyf"].glyphOrder) != len(destination["glyf"].glyphs):
        raise RuntimeError(
            f"Generated glyph order/table mismatch: {len(destination['glyf'].glyphOrder)} "
            f"order entries vs {len(destination['glyf'].glyphs)} glyphs."
        )
    destination.save(output_path, reorderTables=False)

    generated = TTFont(output_path, recalcTimestamp=False)
    generated_cmap = generated.getBestCmap()
    unresolved = [codepoint for codepoint in expected if not generated_cmap.get(codepoint)]
    if unresolved:
        raise RuntimeError(f"Generated font has unresolved legacy glyphs: {unresolved[:8]}")
    family_names = {
        record.toUnicode()
        for record in generated["name"].names
        if record.nameID in (1, 16)
    }
    if family_names != {FAMILY_NAME}:
        raise RuntimeError(f"Unexpected generated family names: {sorted(family_names)}")
    if generated["OS/2"].fsType != 0:
        raise RuntimeError("Generated font is not marked installable/redistributable.")


def main() -> int:
    if fonttools_version != FONTTOOLS_VERSION:
        raise RuntimeError(
            f"This generator requires fontTools {FONTTOOLS_VERSION}, found {fonttools_version}."
        )

    font_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--cache", type=Path, default=font_root / ".cache")
    parser.add_argument("--output", type=Path, default=font_root / OUTPUT_NAME)
    args = parser.parse_args()

    base_path = acquire_source(args.cache, "uno")
    fluent_path = acquire_source(args.cache, "fluent")
    fluent_metadata_path = acquire_source(args.cache, "fluentMetadata")
    build_font(
        base_path,
        fluent_path,
        fluent_metadata_path,
        font_root / "LegacyFluentGlyphMap.json",
        args.output,
    )
    print(f"{args.output}: {sha256(args.output)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
