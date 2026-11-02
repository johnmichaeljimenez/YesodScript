#!/usr/bin/env python3
"""
Export LDtk level-data.ldtk -> level-data.ysl LEVELDATA sections.

Sokoban layout (see level.ysl):
  $LEVEL_FILL w h
  SETVAR LEVEL_SPAWN_X/Y + LEVEL_HAS_SPAWN
  $LEVEL_SET x y type

Tile types:
  0 floor
  1 wall   (World IntGrid value 1)
  2 box    (Box entity) — gameplay stub
  3 exit   (Exit entity) — gameplay stub

Player_Start sets spawn coordinates and HeroType (not a tile type).
Entity stamps overwrite IntGrid blockers on the same cell.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

TILE_SIZE = 16
GAMEPLAY_INTGRID_LAYER = "World"

# entity identifier -> tile type
ENTITY_TILE_TYPE = {
    "Box": 2,
    "Exit": 3,
}

# Keep these values in sync with HERO_TYPE checks in player.ysl.
HERO_TYPE_VALUE = {
    "Warrior": 0,
    "Thief": 1,
    "Wizard": 2,
}


def grid_size(level: dict[str, Any]) -> tuple[int, int]:
    return level["pxWid"] // TILE_SIZE, level["pxHei"] // TILE_SIZE


def find_player_start(level: dict[str, Any]) -> tuple[int, int, int] | None:
    for layer in level["layerInstances"]:
        if layer.get("__type") != "Entities":
            continue
        for entity in layer.get("entityInstances", []):
            ident = entity.get("__identifier") or ""
            # accept Player_Start / PlayerStart
            if ident in ("Player_Start", "PlayerStart"):
                gx, gy = entity["__grid"]
                hero_name = next(
                    (
                        field.get("__value")
                        for field in entity.get("fieldInstances", [])
                        if field.get("__identifier") == "HeroType"
                    ),
                    "Warrior",
                )
                if hero_name not in HERO_TYPE_VALUE:
                    raise ValueError(
                        f"unsupported HeroType '{hero_name}' on Player_Start "
                        f"in level '{level['identifier']}'"
                    )
                return int(gx), int(gy), HERO_TYPE_VALUE[hero_name]
    return None


def is_gameplay_intgrid_layer(layer: dict[str, Any]) -> bool:
    return (
        layer.get("__type") == "IntGrid"
        and str(layer.get("__identifier") or "") == GAMEPLAY_INTGRID_LAYER
    )


def collect_walls(level: dict[str, Any], gw: int, gh: int) -> dict[tuple[int, int], int]:
    """World IntGrid value 1 -> wall tiles (type 1)."""
    tiles: dict[tuple[int, int], int] = {}
    for layer in level.get("layerInstances", []):
        if not is_gameplay_intgrid_layer(layer):
            continue
        csv = layer.get("intGridCsv") or []
        for y in range(gh):
            for x in range(gw):
                idx = y * gw + x
                if idx < len(csv) and int(csv[idx]) == 1:
                    tiles[(x, y)] = 1
    return tiles


def apply_entities(
    level: dict[str, Any],
    gw: int,
    gh: int,
    tiles: dict[tuple[int, int], int],
) -> None:
    for layer in level.get("layerInstances", []):
        if layer.get("__type") != "Entities":
            continue
        for entity in layer.get("entityInstances", []):
            etype = entity.get("__identifier") or ""
            if etype in ("Player_Start", "PlayerStart"):
                continue

            tile_type = ENTITY_TILE_TYPE.get(etype)
            if tile_type is None:
                print(f"  warning: skipping unsupported entity '{etype}'")
                continue

            gx, gy = entity.get("__grid", [0, 0])
            w_tiles = max(1, int(entity.get("width", TILE_SIZE)) // TILE_SIZE)
            h_tiles = max(1, int(entity.get("height", TILE_SIZE)) // TILE_SIZE)

            for dy in range(h_tiles):
                for dx in range(w_tiles):
                    tx, ty = int(gx) + dx, int(gy) + dy
                    if 0 <= tx < gw and 0 <= ty < gh:
                        tiles[(tx, ty)] = tile_type


def generate_leveldata(level: dict[str, Any]) -> str:
    name = str(level["identifier"]).lower()
    gw, gh = grid_size(level)
    lines = [
        f"@LEVELDATA:{name}",
        f"\t$LEVEL_FILL {gw} {gh}",
    ]

    spawn = find_player_start(level)
    if spawn is not None:
        sx, sy, hero_type = spawn
        lines.append(f"\tSETVAR LEVEL_SPAWN_X {sx}")
        lines.append(f"\tSETVAR LEVEL_SPAWN_Y {sy}")
        lines.append(f"\tSETVAR LEVEL_SPAWN_HERO {hero_type}")
        lines.append("\tSETVAR LEVEL_HAS_SPAWN true")
    else:
        lines.append("\t# no Player_Start found")

    tiles = collect_walls(level, gw, gh)
    apply_entities(level, gw, gh, tiles)

    for (x, y), tile_type in sorted(tiles.items()):
        lines.append(f"\t$LEVEL_SET {x} {y} {tile_type}")

    lines.append("\tBREAK")
    return "\n".join(lines)


def generate_leveldata_metadata(levels: list[dict[str, Any]]) -> str:
    lines = [
        "@LEVELDATA:metadata",
        f"\tSETVAR LEVEL_COUNT {len(levels)}",
        "\tLISTCLEAR LEVEL_NAMES",
    ]
    lines.extend(
        f'\tLISTADD LEVEL_NAMES "{str(level["identifier"]).lower()}"'
        for level in levels
    )
    lines.append("\tBREAK")
    return "\n".join(lines)


def export_ldtk(
    ldtk_path: Path,
    out_path: Path,
    only_levels: set[str] | None = None,
) -> None:
    data = json.loads(ldtk_path.read_text(encoding="utf-8"))
    levels = [
        level
        for level in data.get("levels", [])
        if only_levels is None or level["identifier"] in only_levels
    ]
    if not levels:
        raise SystemExit("no levels exported (check --only filter)")

    sections = [generate_leveldata_metadata(levels)]
    for level in levels:
        ident = level["identifier"]
        print(f"exporting level: {ident}")
        sections.append(generate_leveldata(level))

    header = (
        "# Auto-generated by ldtk_to_ysl.py — do not edit by hand.\n"
        "# Source: " + ldtk_path.name + "\n"
        "# Tile types: 0 floor, 1 wall, 2 box, 3 exit\n\n"
    )
    out_path.write_text(header + "\n\n".join(sections) + "\n", encoding="utf-8")
    print(f"wrote {out_path}")


def main() -> None:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Convert LDtk sokoban levels to YSL LEVELDATA.")
    parser.add_argument(
        "ldtk",
        nargs="?",
        default=str(script_dir / "level-data.ldtk"),
        help="path to level-data.ldtk",
    )
    parser.add_argument(
        "-o",
        "--output",
        default=str(script_dir / "level-data.ysl"),
        help="output .ysl path",
    )
    parser.add_argument(
        "--only",
        action="append",
        default=None,
        help="export only this LDtk level identifier (repeatable)",
    )
    args = parser.parse_args()

    only = set(args.only) if args.only else None
    export_ldtk(Path(args.ldtk), Path(args.output), only_levels=only)


if __name__ == "__main__":
    main()
