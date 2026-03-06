from __future__ import annotations

import json
from pathlib import Path

from contract_atlas.domain.models import (
    ContractSnapshot,
    DataContract,
    DataMember,
    EnumContract,
    EnumMember,
    OperationContract,
    OperationParameter,
    ServiceContract,
)


class JsonlContractRepository:
    def load(self, contracts_path: Path, data_members_path: Path) -> ContractSnapshot:
        if not contracts_path.exists():
            raise FileNotFoundError(f"contracts file not found: {contracts_path}")
        if not data_members_path.exists():
            raise FileNotFoundError(f"data-members file not found: {data_members_path}")

        snapshot = ContractSnapshot()

        for row in self._read_jsonl(contracts_path):
            row_type = row.get("type")
            name = row.get("name")
            if not isinstance(name, str):
                continue

            if row_type == "ServiceContract":
                snapshot.service_contracts.append(
                    ServiceContract(
                        name=name,
                        operations=self._parse_operations(row.get("operationContracts")),
                    )
                )
                continue

            if row_type == "DataContract":
                snapshot.data_contracts.setdefault(name, DataContract(name=name))
                continue

            if row_type == "Enum":
                snapshot.enum_contracts[name] = EnumContract(
                    name=name,
                    members=self._parse_enum_members(row.get("enumMembers")),
                )

        for row in self._read_jsonl(data_members_path):
            if row.get("type") != "DataContract":
                continue

            name = row.get("name")
            if not isinstance(name, str):
                continue

            members = self._parse_data_members(row.get("dataMembers"))
            snapshot.data_contracts[name] = DataContract(name=name, members=members)

        snapshot.service_contracts.sort(key=lambda item: item.name)
        return snapshot

    @staticmethod
    def _read_jsonl(path: Path) -> list[dict]:
        records: list[dict] = []
        with path.open("r", encoding="utf-8") as handle:
            for line_number, line in enumerate(handle, start=1):
                text = line.strip()
                if not text:
                    continue
                try:
                    row = json.loads(text)
                except json.JSONDecodeError as exc:
                    raise ValueError(f"invalid json at {path}:{line_number}: {exc}") from exc
                if isinstance(row, dict):
                    records.append(row)
        return records

    @staticmethod
    def _parse_data_members(raw_members: object) -> tuple[DataMember, ...]:
        members: list[DataMember] = []
        for item in raw_members or []:
            if not isinstance(item, dict):
                continue
            name = item.get("name")
            type_name = item.get("type")
            if isinstance(name, str) and isinstance(type_name, str):
                members.append(DataMember(name=name, type_name=type_name))
        return tuple(members)

    @staticmethod
    def _parse_enum_members(raw_members: object) -> tuple[EnumMember, ...]:
        members: list[EnumMember] = []
        for item in raw_members or []:
            if not isinstance(item, dict):
                continue
            name = item.get("name")
            value = item.get("value")
            if isinstance(name, str) and isinstance(value, str):
                members.append(EnumMember(name=name, value=value))
        return tuple(members)

    @staticmethod
    def _parse_operations(raw_operations: object) -> tuple[OperationContract, ...]:
        operations: list[OperationContract] = []
        for op in raw_operations or []:
            if not isinstance(op, dict):
                continue

            name = op.get("name")
            return_type = op.get("returnType")
            effective_return_type = op.get("effectiveReturnType")
            is_one_way = op.get("isOneWay")
            if (
                not isinstance(name, str)
                or not isinstance(return_type, str)
                or not isinstance(effective_return_type, str)
                or not isinstance(is_one_way, bool)
            ):
                continue

            params: list[OperationParameter] = []
            for p in op.get("parameters") or []:
                if not isinstance(p, dict):
                    continue
                p_name = p.get("name")
                p_type = p.get("type")
                p_is_out = p.get("isOut")
                p_is_ref = p.get("isRef")
                p_is_optional = p.get("isOptional")
                if (
                    isinstance(p_name, str)
                    and isinstance(p_type, str)
                    and isinstance(p_is_out, bool)
                    and isinstance(p_is_ref, bool)
                    and isinstance(p_is_optional, bool)
                ):
                    params.append(
                        OperationParameter(
                            name=p_name,
                            type_name=p_type,
                            is_out=p_is_out,
                            is_ref=p_is_ref,
                            is_optional=p_is_optional,
                        )
                    )

            operations.append(
                OperationContract(
                    name=name,
                    return_type=return_type,
                    effective_return_type=effective_return_type,
                    is_one_way=is_one_way,
                    parameters=tuple(params),
                )
            )

        return tuple(operations)
