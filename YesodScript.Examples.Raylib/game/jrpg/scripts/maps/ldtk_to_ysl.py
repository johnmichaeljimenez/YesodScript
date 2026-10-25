#!/usr/bin/env python3
"""
Export LDtk mapdata.ldtk -> mapdata.ysl MAPDATA sections.

Also provides helpers to extract visual tile data from IntGrid auto-tile
layers (everything except the "World" collider layer):

  extract_tilemap_tiles(level) -> list[(layer, png, list[(x, y, tile_id)])]
  extract_all_tilemaps(ldtk_path) -> dict[level_name, above]

MAP_SET layout (see map.ysl / map_set.ysl):
  $MAP_SET x y id type v1 v2 t state sprite entitysprite alias

TILESET_ADD layout (see tileset.ysl):
  $TILESET_ADD x y tileindex sort spriteid
  - emitted for Tilemap_Overworld / Tilemap_Walls autoLayerTiles
  - spriteid is the LOADASSETS key (png stem under game assets/)
  - sort: Overworld=-200, Walls=-150 (below entity MAP_SET at -100)


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
    v1 = Value1, v2 = Value2, t = Target iid, state = State (ActiveType)
    alias = Alias (optional stable name for EV_* handlers)
  trigger:
    v1 = Trigger_Key (optional; unused when Target is set)
    v2 = Parameter
        OR, when Target is set, the destination level identifier
        (lowercased) resolved from Target.levelIid — any Trigger with a
        Target is treated as an exit warp (no need to set Trigger_Key="exit")
    t = Target iid
    state = State (ActiveType)
    alias = Alias (optional; used by EV_TRIGGER:<map>:<alias>)

Aliases:
  - LDtk Alias field on Trigger / Interactable entities
  - exported as the final MAP_SET arg into TILE_ALIASES
  - empty / missing Alias becomes * (no custom event)
  - interaction.ysl dispatches EV_<KIND>:<MAP_NAME>:<alias>

Tile states (LDtk ActiveType -> int):
  0 Active            visible + interactable
  1 Non_Interactable  visible, no interaction
  2 Invisible         hidden, no interaction

Empty / missing string fields become 0 (same default as MAP_FILL),
except alias which defaults to *.
sprite and entitysprite are always * for now.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

# LDtk grid is 16px in this project.
TILE_SIZE = 16

# Visual IntGrid layers exported as TILESET_ADD (collider World is skipped).
# Depth must stay below entity MAP_SET renders (-100).
TILEMAP_LAYER_SORT = {
    "Tilemap_Overworld": -200,
    "Tilemap_Walls": -150,
}
TILEMAP_LAYER_SORT_DEFAULT = -180

# Interactable.Type enum -> MAP tile type
INTERACT_TYPE_TO_TILE = {
    "Misc": 2,       # gate / misc blocker-like entity
    "NPC": 3,
    "Item": 4,       # gold and other items (Value1/Value2 carry payload)
    "Switch": 5,
    "Savepoint": 7,
}

# LDtk ActiveType enum -> TILE_STATES int
STATE_ACTIVE = 0
STATE_NON_INTERACTABLE = 1
STATE_INVISIBLE = 2

STATE_TO_INT = {
    "Active": STATE_ACTIVE,
    "Non_Interactable": STATE_NON_INTERACTABLE,
    "Invisible": STATE_INVISIBLE,
}

DEFAULT_TILE = {
    "id": "0",
    "type": 0,
    "v1": "0",
    "v2": "0",
    "t": "0",
    "state": STATE_ACTIVE,
    "sprite": "*",
    "entitysprite": "*",
    "alias": "*",
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


def resolve_state(fields: dict[str, Any]) -> int:
    """
    Map LDtk State (ActiveType) to TILE_STATES int.
    Falls back to legacy bool Disabled if State is absent.
    """
    raw = fields.get("State")
    if raw is None:
        # legacy bool field from older LDtk defs
        return STATE_INVISIBLE if bool(fields.get("Disabled", False)) else STATE_ACTIVE

    if isinstance(raw, str):
        mapped = STATE_TO_INT.get(raw)
        if mapped is not None:
            return mapped
        print(f"  warning: unknown ActiveType '{raw}', using Active(0)")
        return STATE_ACTIVE

    if isinstance(raw, bool):
        return STATE_INVISIBLE if raw else STATE_ACTIVE

    if isinstance(raw, int):
        if raw in (STATE_ACTIVE, STATE_NON_INTERACTABLE, STATE_INVISIBLE):
            return raw
        print(f"  warning: out-of-range State int {raw}, using Active(0)")
        return STATE_ACTIVE

    print(f"  warning: unexpected State value {raw!r}, using Active(0)")
    return STATE_ACTIVE


def resolve_alias(fields: dict[str, Any]) -> str:
    """
    Optional LDtk Alias string used by EV_* event labels.
    Empty / missing -> * (no custom handler; interaction falls back to generic).
    """
    raw = fields.get("Alias")
    if raw is None:
        return "*"
    text = str(raw).strip()
    return text if text else "*"


def make_tile(
    *,
    entity_iid: str,
    tile_type: int,
    v1: Any = "0",
    v2: Any = "0",
    t: Any = "0",
    state: int = STATE_ACTIVE,
    alias: Any = "*",
) -> dict[str, str | int]:
    alias_text = "*" if alias in (None, "") else str(alias).strip() or "*"
    return {
        "id": short_iid(entity_iid),
        "type": tile_type,
        "v1": "0" if v1 in (None, "") else str(v1).strip(),
        "v2": "0" if v2 in (None, "") else str(v2).strip(),
        "t": "0" if t in (None, "") else str(t).strip(),
        "state": int(state),
        "sprite": "*",
        "entitysprite": "*",
        "alias": alias_text,
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
    state = resolve_state(fields)

    if etype == "PlayerStart":
        return None

    if etype == "Trigger":
        # trigger = type 6 | v1=Trigger_Key | v2=Parameter or exit level | t=target iid | alias
        # Any Trigger with a Target is an exit warp (Trigger_Key need not be "exit").
        trigger_key = fields.get("Trigger_Key")
        parameter = fields.get("Parameter")
        t = target_iid(fields)
        v2: Any = parameter
        if t != "0":
            # Exit warps need the destination MAPDATA name; Parameter is unused.
            level_name = target_level_name(fields, levels_by_iid)
            v2 = level_name if level_name else parameter

        return make_tile(
            entity_iid=iid,
            tile_type=6,
            v1=trigger_key,
            v2=v2,
            t=t,
            state=state,
            alias=resolve_alias(fields),
        )

    if etype == "Interactable":
        interact = fields.get("Type") or "Misc"
        tile_type = INTERACT_TYPE_TO_TILE.get(interact)
        if tile_type is None:
            print(f"  warning: unknown Interactable.Type '{interact}', using misc(2)")
            tile_type = 2

        # switch / gate / gold / npc / savepoint all share this shape:
        # id type | v1=Value1 | v2=Value2 | t=target iid | State | alias
        return make_tile(
            entity_iid=iid,
            tile_type=tile_type,
            v1=fields.get("Value1"),
            v2=fields.get("Value2"),
            t=target_iid(fields),
            state=state,
            alias=resolve_alias(fields),
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


def format_alias_token(alias: Any) -> str:
    """Alias defaults to bare * (no custom event); named aliases are quoted strings."""
    text = "*" if alias in (None, "") else str(alias).strip() or "*"
    if text == "*":
        return "*"
    return ysl_token(text)


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
        format_alias_token(data.get("alias", "*")),
    ]
    return "\t$MAP_SET " + " ".join(args)


def sprite_id_from_tileset_path(png: str) -> str:
    """
    Map LDtk __tilesetRelPath onto the runtime asset id from LOADASSETS.
    DirectoryExtractor keys are path stems under assets/, e.g.
      ../../assets/tile-16px.png -> tile-16px
      ../../assets/tile-16px-ref-wall.png -> tile-16px-ref-wall
    """
    name = Path(str(png).replace("\\", "/")).name
    return Path(name).stem


def layer_sort(layer_name: str) -> int:
    return TILEMAP_LAYER_SORT.get(layer_name, TILEMAP_LAYER_SORT_DEFAULT)


def format_tileset_add(
    x: int,
    y: int,
    tile_index: int,
    sort: int,
    sprite_id: str,
) -> str:
    return (
        f"\t$TILESET_ADD {x} {y} {tile_index} {sort} {ysl_token(sprite_id)}"
    )


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

    # Visual tiles first (behind entity/blocker MAP_SET renders).
    # Emit bottom layers first so later same-depth draws stay stable.
    for layer_name, png, tiles in reversed(extract_tilemap_tiles(level)):
        sprite_id = sprite_id_from_tileset_path(png)
        sort = layer_sort(layer_name)
        lines.append(f"\t# tileset layer {layer_name} -> {sprite_id} sort={sort}")
        for x, y, tid in tiles:
            lines.append(format_tileset_add(x, y, tid, sort, sprite_id))

    tiles = collect_intgrid_blockers(level, gw, gh)
    apply_entities(level, gw, gh, tiles, levels_by_iid)

    for (x, y), data in sorted(tiles.items()):
        lines.append(format_map_set(x, y, data))

    lines.append("\tBREAK")
    return "\n".join(lines)


def extract_tilemap_tiles(
    level: dict[str, Any],
) -> list[tuple[str, str, list[tuple[int, int, int]]]]:
    """
    Extract visual tile data from non-World IntGrid layers (Tilemap_Walls,
    Tilemap_Overworld, etc.).

    Skips the "World" layer (collider / blocker data).

    Returns:
        list of (layer_identifier, png_relpath, list of (grid_x, grid_y, tile_index))
        in LDtk layer order (topmost first).

    - png_relpath comes from the layer's __tilesetRelPath
    - grid_x / grid_y are pixel position // TILE_SIZE
    - tile_index is the LDtk auto-tile "t" value (tileset tile id)

    autoLayerTiles already contain the resolved tiles produced by the IntGrid
    auto-rules, so this is the painted visual result for each non-zero cell.
    """
    results: list[tuple[str, str, list[tuple[int, int, int]]]] = []

    for layer in level.get("layerInstances", []):
        layer_name = str(layer.get("__identifier") or "")
        if layer_name == "World":
            continue
        if layer.get("__type") != "IntGrid":
            continue

        # Prefer the named visual layers; still accept other IntGrid+tileset layers.
        png = layer.get("__tilesetRelPath")
        if not png:
            continue

        tiles: list[tuple[int, int, int]] = []

        # Auto-tiled tiles (the usual case for these layers)
        for tile_data in layer.get("autoLayerTiles", []):
            px = tile_data.get("px", [0, 0])
            gx = int(px[0] // TILE_SIZE)
            gy = int(px[1] // TILE_SIZE)
            tid = int(tile_data.get("t", 0))
            tiles.append((gx, gy, tid))

        # Manual grid tiles (rare for IntGrid, but included for completeness)
        for tile_data in layer.get("gridTiles", []):
            px = tile_data.get("px", [0, 0])
            gx = int(px[0] // TILE_SIZE)
            gy = int(px[1] // TILE_SIZE)
            tid = int(tile_data.get("t", 0))
            tiles.append((gx, gy, tid))

        if tiles:
            # Stable order: row-major (y then x)
            tiles.sort(key=lambda t: (t[1], t[0]))
            results.append((layer_name, str(png), tiles))

    return results


def extract_all_tilemaps(
    ldtk_path: Path,
) -> dict[str, list[tuple[str, str, list[tuple[int, int, int]]]]]:
    """
    Load an LDtk project and extract visual tiles for every level.

    Returns:
        { level_identifier: extract_tilemap_tiles(level), ... }
    """
    data = json.loads(ldtk_path.read_text(encoding="utf-8"))
    return {
        level["identifier"]: extract_tilemap_tiles(level)
        for level in data["levels"]
    }


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
