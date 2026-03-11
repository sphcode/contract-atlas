from __future__ import annotations

from pathlib import Path


class YamlWriter:
    def write(self, output_path: Path, document: dict) -> None:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        yaml_text = _to_yaml(document)
        output_path.write_text(yaml_text + "\n", encoding="utf-8")


def _to_yaml(value: object, indent: int = 0) -> str:
    if isinstance(value, dict):
        lines: list[str] = []
        for key, item in value.items():
            prefix = " " * indent + f"{_scalar_to_yaml(key)}:"
            if _is_scalar(item):
                lines.append(prefix + f" {_scalar_to_yaml(item)}")
            else:
                lines.append(prefix)
                lines.append(_to_yaml(item, indent + 2))
        return "\n".join(lines) if lines else " " * indent + "{}"

    if isinstance(value, list):
        if not value:
            return " " * indent + "[]"
        lines = []
        for item in value:
            prefix = " " * indent + "-"
            if _is_scalar(item):
                lines.append(prefix + f" {_scalar_to_yaml(item)}")
            else:
                lines.append(prefix)
                lines.append(_to_yaml(item, indent + 2))
        return "\n".join(lines)

    return " " * indent + _scalar_to_yaml(value)


def _is_scalar(value: object) -> bool:
    return value is None or isinstance(value, (str, int, float, bool))


def _scalar_to_yaml(value: object) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    if not isinstance(value, str):
        value = str(value)

    escaped = value.replace("\\", "\\\\").replace('"', '\\"')
    return f"\"{escaped}\""
