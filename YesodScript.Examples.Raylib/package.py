"""
Usage:
  python package.py               packages default game (pong)
  python package.py pong          packages game/pong
  python package.py pong --no-push

Content lives under game/<id>/ (assets, scripts, game.json). Only that pack is shipped.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path


def run(cmd: list[str], cwd: Path | None = None, check: bool = True) -> subprocess.CompletedProcess:
    """Run a command and return the result. Prints the command for visibility."""
    print(f"> {' '.join(cmd)}")
    return subprocess.run(cmd, cwd=cwd, check=check)


def read_game_manifest(manifest: Path) -> tuple[str | None, str | None, str | None]:
    """Read title, icon, and itch target from game.json."""
    if not manifest.is_file():
        return None, None, None
    try:
        with manifest.open(encoding="utf-8") as f:
            data = json.load(f)
        return data.get("title"), data.get("icon"), data.get("itch")
    except (json.JSONDecodeError, OSError) as e:
        print(f"WARNING: Could not parse {manifest}: {e}")
        return None, None, None


def create_zip(source_dir: Path, zip_path: Path) -> None:
    """Create a zip of the *contents* of source_dir (matching Compress-Archive behaviour)."""
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for item in source_dir.rglob("*"):
            if item.is_file():
                arcname = item.relative_to(source_dir)
                zf.write(item, arcname)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Package a YesodScript Raylib game for itch.io (Windows x64)."
    )
    parser.add_argument(
        "game_id",
        nargs="?",
        default="pong",
        help="Game content pack id (folder under game/). Default: pong",
    )
    parser.add_argument(
        "--no-push",
        action="store_true",
        help="Skip the butler push step even if an itch target is defined.",
    )
    args = parser.parse_args()

    script_dir = Path(__file__).resolve().parent
    root = script_dir.parent
    project = script_dir / "YesodScript.Examples.Raylib.csproj"
    test_project = root / "YesodScript.Engine.Tests" / "YesodScript.Engine.Tests.csproj"

    rid = "win-x64"
    config = "Release"
    out_dir = script_dir / "publish" / rid
    dist_dir = script_dir / "dist"

    game_id = args.game_id
    push = not args.no_push

    game_dir = script_dir / "game" / game_id
    if not game_dir.is_dir():
        print(f"ERROR: Game content folder not found: {game_dir}")
        games_root = script_dir / "game"
        if games_root.is_dir():
            available = [p.name for p in games_root.iterdir() if p.is_dir()]
            print("Available games:")
            for g in sorted(available):
                print(f"  {g}")
        return 1

    manifest = game_dir / "game.json"
    game_title, game_icon, itch_target = read_game_manifest(manifest)

    if not game_title:
        game_title = game_id
    if not game_icon:
        game_icon = f"{game_title}.ico"

    game_icon_path = game_dir / game_icon
    if not game_icon_path.is_file():
        game_icon_path = None

    zip_name = f"{game_title}-{rid}.zip"
    zip_path = dist_dir / zip_name

    print("=" * 40)
    print(" YesodScript Raylib - itch.io package")
    print("=" * 40)
    print(f" Game:   {game_id}")
    print(f" Title:  {game_title}")
    print(f" Icon:   {game_icon}")
    print(f" Itch:   {itch_target or '(none)'}")
    print()

    if not shutil.which("dotnet"):
        print("ERROR: dotnet SDK not found on PATH.")
        print("Install the .NET 8 SDK to build. Players do not need .NET.")
        return 1

    print("[1/4] Running tests...")
    try:
        run(
            [
                "dotnet",
                "test",
                str(test_project),
                "--configuration",
                config,
                "--verbosity",
                "minimal",
            ]
        )
    except subprocess.CalledProcessError:
        print()
        print("ERROR: Tests failed. Aborting package.")
        return 1
    print()

    print(f"[2/4] Publishing self-contained {rid} build for game '{game_id}'...")
    if out_dir.exists():
        shutil.rmtree(out_dir)

    publish_cmd = [
        "dotnet",
        "publish",
        str(project),
        "--configuration",
        config,
        "--runtime",
        rid,
        "--self-contained",
        "true",
        "--output",
        str(out_dir),
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        f"-p:GameId={game_id}",
        f"-p:GameTitle={game_title}",
    ]
    if game_icon_path:
        publish_cmd.append(f"-p:GameIcon={game_icon_path}")

    try:
        run(publish_cmd)
    except subprocess.CalledProcessError:
        print()
        print("ERROR: Publish failed.")
        return 1
    print()

    expected_exe = out_dir / f"{game_title}.exe"
    if not expected_exe.is_file():
        print(f"ERROR: Expected exe not found: {expected_exe}")
        return 1
    if not (out_dir / "assets").is_dir():
        print("ERROR: assets folder missing from publish output.")
        return 1
    if not (out_dir / "scripts").is_dir():
        print("ERROR: scripts folder missing from publish output.")
        return 1

    nested_game = out_dir / "game"
    if nested_game.exists():
        print("Removing nested game/ folder from publish output (flattened content only)...")
        shutil.rmtree(nested_game)

    print("[3/4] Creating zip for itch.io...")
    try:
        create_zip(out_dir, zip_path)
    except Exception as e:
        print(f"ERROR: Failed to create zip: {e}")
        return 1
    print()

    print("[4/4] Done.")
    print()
    print(f"Publish folder: {out_dir}")
    print(f"Zip:            {zip_path}")
    print()
    print("itch.io upload tips:")
    print("  - Upload the zip as a Windows download")
    print(f"  - Players run {game_title}.exe  (no .NET install)")
    print("  - Keep assets/ and scripts/ next to the exe  (already in the zip)")
    if itch_target:
        print("  - Optional butler:")
        print(f'      butler push "{zip_path}" {itch_target}')
    print()

    if push:
        if itch_target:
            if shutil.which("butler"):
                try:
                    run(["butler", "push", str(zip_path), itch_target])
                except subprocess.CalledProcessError:
                    print("WARNING: butler push failed.")
                    return 1
            else:
                print("SKIP push: butler not found on PATH.")
        else:
            print("SKIP push: no itch target in game.json")

    return 0


if __name__ == "__main__":
    sys.exit(main())