#!/usr/bin/env python3
"""
Export LDtk mapdata.ldtk -> mapdata.ysl MAPDATA sections.

MAP_SET layout (see map.ysl / map_set.ysl):
  $MAP_SET x y id type v1 v2 t state sprite entitysprite

Tile types:
  0 blank
  1 blocker          (IntGrid value 1)
  2 misc / gate      (Interactable Type=Misc)
  3 npc              (Interactable Type=NPC)
  4 item / gold      (Interactable Type=Item)
  5 switch           (Interactable Type=Switch)
  6 trigger          (Trigger entity)
  7 savepoint        (Interactable Type=Savepoint)

Entity ids and target refs always use the first 8 chars of the LDtk iid
so they stay unique across the whole project (no level-name prefix).

Field mapping:
  switch/gate/item/npc/savepoint:
    v1 = Value1, v2 = Value2, t = Target iid, state = Disabled
  trigger:
    v1 = Trigger_Key
    v2 = Parameter
        OR, when Trigger_Key is "exit", the destination level identifier
        (lowercased) resolved from Target.levelIid
    t = Target iid
    state = Disabled

Empty / missing string fields become 0 (same default as MAP_FILL).
sprite and entitysprite are always * for now.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

# LDtk grid is 16px in this project.
TILE_SIZE = 16

# Interactable.Type enum -> MAP tile type
INTERACT_TYPE_TO_TILE = {
    "Misc": 2,       # gate / misc blocker-like entity
    "NPC": 3,
    "Item": 4,       # gold and other items (Value1/Value2 carry payload)
    "Switch": 5,
    "Savepoint": 7,
}

DEFAULT_TILE = {
    "id": "0",
    "type": 0,
    "v1": "0",
    "v2": "0",
    "t": "0",
    "state": "false",
    "sprite": "*",
    "entitysprite": "*",
}


def short_iid(iid: str | None) -> str:
    """LDtk iid is a UUID; first 8 chars are unique enough for this game."""
    if not iid:
        return "0"
    return iid[:8]


def field_map(entity: dict[str, Any]) -> dict[str, Any]:
    return {
        fi["__identifier"]: fi.get("__value")
        for fi in entity.get("fieldInstances", [])
    }


def ysl_token(value: Any) -> str:
    """
    Format one MAP_SET argument.
    - None / empty -> 0
    - bool -> true/false
    - pure int string / int -> bare number
    - everything else -> quoted string
    """
    if value is None:
        return "0"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)

    text = str(value).strip()
    if text == "":
        return "0"
    if text.isdigit() or (text.startswith("-") and text[1:].isdigit()):
        return text
    escaped = text.replace('"', '\\"')
    return f'"{escaped}"'


def target_ref(fields: dict[str, Any]) -> dict[str, Any] | None:
    """Return the raw EntityRef dict for Target, if present."""
    target = fields.get("Target")
    return target if isinstance(target, dict) else None


def target_iid(fields: dict[str, Any]) -> str:
    """Resolve EntityRef Target to short iid, or 0 if unset."""
    target = target_ref(fields)
    if target is not None:
        return short_iid(target.get("entityIid"))
    raw = fields.get("Target")
    if isinstance(raw, str) and raw:
        return short_iid(raw)
    return "0"


def target_level_name(
    fields: dict[str, Any],
    levels_by_iid: dict[str, dict[str, Any]],
) -> str:
    """
    Resolve Target.levelIid -> LDtk level identifier (lowercased).
    Returns "" if unset / unknown.
    """
    target = target_ref(fields)
    if target is None:
        return ""
    level_iid = target.get("levelIid")
    if not level_iid:
        return ""
    level = levels_by_iid.get(level_iid)
    if level is None:
        return ""
    return str(level["identifier"]).lower()


def make_tile(
    *,
    entity_iid: str,
    tile_type: int,
    v1: Any = "0",
    v2: Any = "0",
    t: Any = "0",
    disabled: Any = False,
) -> dict[str, str | int]:
    return {
        "id": short_iid(entity_iid),
        "type": tile_type,
        "v1": "0" if v1 in (None, "") else str(v1).strip(),
        "v2": "0" if v2 in (None, "") else str(v2).strip(),
        "t": "0" if t in (None, "") else str(t).strip(),
        "state": "true" if disabled else "false",
        "sprite": "*",
        "entitysprite": "*",
    }


def tile_from_entity(
    entity: dict[str, Any],
    levels_by_iid: dict[str, dict[str, Any]],
) -> dict[str, str | int] | None:
    """
    Convert one LDtk entity into a MAP_SET payload.
    Returns None for entities that do not become tiles (e.g. PlayerStart).
    """
    etype = entity["__identifier"]
    fields = field_map(entity)
    iid = entity["iid"]
    disabled = bool(fields.get("Disabled", False))

    if etype == "PlayerStart":
        return None

    if etype == "Trigger":
        # trigger = type 6 | v1=Trigger_Key | v2=Parameter or exit level | t=target iid
        trigger_key = fields.get("Trigger_Key")
        parameter = fields.get("Parameter")
        v2: Any = parameter
        if isinstance(trigger_key, str) and trigger_key.strip().lower() == "exit":
            # Exit warps need the destination MAPDATA name; Parameter is unused.
            level_name = target_level_name(fields, levels_by_iid)
            v2 = level_name if level_name else parameter

        return make_tile(
            entity_iid=iid,
            tile_type=6,
            v1=trigger_key,
            v2=v2,
            t=target_iid(fields),
            disabled=disabled,
        )

    if etype == "Interactable":
        interact = fields.get("Type") or "Misc"
        tile_type = INTERACT_TYPE_TO_TILE.get(interact)
        if tile_type is None:
            print(f"  warning: unknown Interactable.Type '{interact}', using misc(2)")
            tile_type = 2

        # switch / gate / gold / npc / savepoint all share this shape:
        # id type | v1=Value1 | v2=Value2 | t=target iid | Disabled
        return make_tile(
            entity_iid=iid,
            tile_type=tile_type,
            v1=fields.get("Value1"),
            v2=fields.get("Value2"),
            t=target_iid(fields),
            disabled=disabled,
        )

    print(f"  warning: skipping unsupported entity '{etype}' ({short_iid(iid)})")
    return None


def grid_size(level: dict[str, Any]) -> tuple[int, int]:
    return level["pxWid"] // TILE_SIZE, level["pxHei"] // TILE_SIZE


def find_player_start(level: dict[str, Any]) -> tuple[int, int] | None:
    for layer in level["layerInstances"]:
        if layer["__type"] != "Entities":
            continue
        for entity in layer["entityInstances"]:
            if entity["__identifier"] == "PlayerStart":
                gx, gy = entity["__grid"]
                return gx, gy
    return None


def collect_intgrid_blockers(
    level: dict[str, Any],
    gw: int,
    gh: int,
) -> dict[tuple[int, int], dict[str, str | int]]:
    """IntGrid value 1 -> solid blocker tiles (type 1)."""
    tiles: dict[tuple[int, int], dict[str, str | int]] = {}

    for layer in level["layerInstances"]:
        if layer["__identifier"] != "World" or layer["__type"] != "IntGrid":
            continue

        csv = layer["intGridCsv"]
        for y in range(gh):
            for x in range(gw):
                idx = y * gw + x
                if idx < len(csv) and csv[idx] == 1:
                    tiles[(x, y)] = {
                        **DEFAULT_TILE,
                        "type": 1,
                    }
    return tiles


def apply_entities(
    level: dict[str, Any],
    gw: int,
    gh: int,
    tiles: dict[tuple[int, int], dict[str, str | int]],
    levels_by_iid: dict[str, dict[str, Any]],
) -> None:
    """
    Stamp entity tiles on top of blockers.
    Multi-tile entities (wide/tall triggers) write the same payload on every cell.
    """
    for layer in level["layerInstances"]:
        if layer["__type"] != "Entities":
            continue

        for entity in layer["entityInstances"]:
            payload = tile_from_entity(entity, levels_by_iid)
            if payload is None:
                continue

            gx, gy = entity.get("__grid", [0, 0])
            w_tiles = max(1, entity.get("width", TILE_SIZE) // TILE_SIZE)
            h_tiles = max(1, entity.get("height", TILE_SIZE) // TILE_SIZE)

            for dy in range(h_tiles):
                for dx in range(w_tiles):
                    tx, ty = gx + dx, gy + dy
                    if 0 <= tx < gw and 0 <= ty < gh:
                        tiles[(tx, ty)] = dict(payload)


def format_map_set(x: int, y: int, data: dict[str, str | int]) -> str:
    args = [
        str(x),
        str(y),
        ysl_token(data["id"]),
        str(data["type"]),
        ysl_token(data["v1"]),
        ysl_token(data["v2"]),
        ysl_token(data["t"]),
        str(data["state"]),
        str(data["sprite"]),
        str(data["entitysprite"]),
    ]
    return "\t$MAP_SET " + " ".join(args)


def generate_mapdata(
    level: dict[str, Any],
    levels_by_iid: dict[str, dict[str, Any]],
) -> str:
    name = level["identifier"].lower()
    gw, gh = grid_size(level)
    lines = [
        f"@MAPDATA:{name}",
        f"\t$MAP_FILL {gw} {gh}",
    ]

    spawn = find_player_start(level)
    if spawn is not None:
        sx, sy = spawn
        lines.append(f"\tSETVAR MAP_SPAWN_X {sx}")
        lines.append(f"\tSETVAR MAP_SPAWN_Y {sy}")
        lines.append("\tSETVAR MAP_HAS_SPAWN true")
    else:
        lines.append("\t# no PlayerStart found")

    tiles = collect_intgrid_blockers(level, gw, gh)
    apply_entities(level, gw, gh, tiles, levels_by_iid)

    for (x, y), data in sorted(tiles.items()):
        lines.append(format_map_set(x, y, data))

    lines.append("\tBREAK")
    return "\n".join(lines)


def export_ldtk(
    ldtk_path: Path,
    out_path: Path,
    only_levels: set[str] | None = None,
) -> None:
    data = json.loads(ldtk_path.read_text(encoding="utf-8"))
    levels_by_iid = {level["iid"]: level for level in data["levels"]}
    sections: list[str] = []

    for level in data["levels"]:
        ident = level["identifier"]
        if only_levels is not None and ident not in only_levels:
            continue
        print(f"exporting level: {ident}")
        sections.append(generate_mapdata(level, levels_by_iid))

    if not sections:
        raise SystemExit("no levels exported (check --only filter)")

    out_path.write_text("\n\n".join(sections) + "\n", encoding="utf-8")
    print(f"wrote {out_path}")


def main() -> None:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Convert LDtk mapdata to YSL MAPDATA.")
    parser.add_argument(
        "ldtk",
        nargs="?",
        default=str(script_dir / "mapdata.ldtk"),
        help="path to mapdata.ldtk",
    )
    parser.add_argument(
        "-o",
        "--output",
        default=str(script_dir / "mapdata.ysl"),
        help="output .ysl path",
    )
    parser.add_argument(
        "--only",
        action="append",
        default=None,
        help="export only this LDtk level identifier (repeatable). Example: --only Test",
    )
    args = parser.parse_args()

    only = set(args.only) if args.only else None
    export_ldtk(Path(args.ldtk), Path(args.output), only_levels=only)


if __name__ == "__main__":
    main()
