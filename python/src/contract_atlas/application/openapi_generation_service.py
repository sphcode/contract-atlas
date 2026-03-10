from __future__ import annotations

from pathlib import Path

from contract_atlas.infrastructure.input.jsonl_contract_repository import JsonlContractRepository
from contract_atlas.infrastructure.mapping.openapi_document_builder import OpenApiDocumentBuilder
from contract_atlas.infrastructure.output.yaml_writer import YamlWriter


class OpenApiGenerationService:
    def __init__(self) -> None:
        self._repository = JsonlContractRepository()
        self._builder = OpenApiDocumentBuilder()
        self._writer = YamlWriter()

    def generate(self, contracts_path: Path, data_members_path: Path, output_path: Path) -> None:
        snapshot = self._repository.load(contracts_path, data_members_path)
        document = self._builder.build(snapshot)
        self._writer.write(output_path, document)
