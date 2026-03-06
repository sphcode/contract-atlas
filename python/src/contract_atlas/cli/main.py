from __future__ import annotations

import argparse
from pathlib import Path

from contract_atlas.application.openapi_generation_service import OpenApiGenerationService


def run(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Generate OpenAPI YAML from C# scanner intermediate artifacts."
    )
    parser.add_argument("--contracts", default="output/contracts.jsonl", help="Path to contracts.jsonl")
    parser.add_argument("--data-members", default="output/data-members.jsonl", help="Path to data-members.jsonl")
    parser.add_argument("--output", default="output/openapi.yaml", help="Path to openapi yaml output")
    args = parser.parse_args(argv)

    service = OpenApiGenerationService()
    service.generate(
        contracts_path=Path(args.contracts),
        data_members_path=Path(args.data_members),
        output_path=Path(args.output),
    )
    print(f"Generated OpenAPI YAML: {args.output}")
    return 0
