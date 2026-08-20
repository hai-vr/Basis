#!/usr/bin/env python3
from __future__ import annotations

import sys
from pathlib import Path


REQUIRED_PLAYER_SETTINGS = (
    (None, "AndroidTargetArchitectures", "2", "ARM64"),
    ("scriptingBackend", "Android", "1", "IL2CPP"),
)


def enforce_player_settings(settings_path: Path) -> list[str]:
    with settings_path.open(encoding="utf-8", newline="") as handle:
        lines = handle.readlines()

    corrections: list[str] = []
    block = ""

    for index, line in enumerate(lines):
        stripped = line.rstrip("\r\n")

        if stripped.startswith("  ") and not stripped.startswith("   ") and ":" in stripped:
            block = stripped[2:].split(":", 1)[0]

        for parent, key, value, meaning in REQUIRED_PLAYER_SETTINGS:
            if parent is not None and block != parent:
                continue

            prefix = f"{'  ' if parent is None else '    '}{key}:"
            if not stripped.startswith(prefix):
                continue

            current = stripped[len(prefix):].strip()
            if current == value:
                continue

            lines[index] = f"{prefix} {value}{line[len(stripped):]}"
            corrections.append(f"{key}: {current} -> {value} ({meaning})")

    if corrections:
        with settings_path.open("w", encoding="utf-8", newline="") as handle:
            handle.writelines(lines)

    return corrections


def main() -> int:
    project_root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("Basis")
    project_root = project_root.resolve()

    if not project_root.exists():
        print(f"Project root not found: {project_root}", file=sys.stderr)
        return 1

    settings_path = project_root / "ProjectSettings/ProjectSettings.asset"
    if not settings_path.exists():
        print(f"Player settings not found: {settings_path}", file=sys.stderr)
        return 1

    corrections = enforce_player_settings(settings_path)
    if not corrections:
        print("Android player settings are correct.")
        return 0

    print(
        "::warning title=Android player settings drifted::"
        f"{settings_path.name} had {len(corrections)} Android setting(s) reset to a default "
        "that cannot build. Corrected for this run only - commit the fix."
    )
    for correction in corrections:
        print(f"  - {correction}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
