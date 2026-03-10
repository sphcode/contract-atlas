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
        service_operations: dict[str, dict[str, OperationContract]] = {}
        data_members: dict[str, dict[str, DataMember]] = {}
        enum_members: dict[str, dict[str, EnumMember]] = {}

        for row in self._read_jsonl(contracts_path):
            row_type = row.get("type")
            name = row.get("name")
            if not isinstance(name, str):
                continue

            if row_type == "ServiceContract":
                operations = self._parse_operations(row.get("operationContracts"))
                bucket = service_operations.setdefault(name, {})
                for operation in operations:
                    bucket[self._operation_key(operation)] = operation
                continue

            if row_type == "DataContract":
                snapshot.data_contracts.setdefault(name, DataContract(name=name))
                continue

            if row_type == "Enum":
                bucket = enum_members.setdefault(name, {})
                for member in self._parse_enum_members(row.get("enumMembers")):
                    bucket[self._enum_member_key(member)] = member

        for row in self._read_jsonl(data_members_path):
            if row.get("type") != "DataContract":
                continue

            name = row.get("name")
            if not isinstance(name, str):
                continue

            bucket = data_members.setdefault(name, {})
            for member in self._parse_data_members(row.get("dataMembers")):
                bucket[self._data_member_key(member)] = member

        for name, members in data_members.items():
            snapshot.data_contracts[name] = DataContract(
                name=name,
                members=tuple(sorted(members.values(), key=lambda item: item.name)),
            )

        for name, members in enum_members.items():
            snapshot.enum_contracts[name] = EnumContract(
                name=name,
                members=tuple(sorted(members.values(), key=lambda item: item.name)),
            )

        snapshot.service_contracts = [
            ServiceContract(
                name=name,
                operations=tuple(sorted(operations.values(), key=lambda item: item.name)),
            )
            for name, operations in sorted(service_operations.items(), key=lambda pair: pair[0])
        ]
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
        seen: set[str] = set()
        for item in raw_members or []:
            if not isinstance(item, dict):
                continue
            name = item.get("name")
            type_name = item.get("type")
            if isinstance(name, str) and isinstance(type_name, str):
                member = DataMember(name=name, type_name=type_name)
                key = JsonlContractRepository._data_member_key(member)
                if key in seen:
                    continue
                seen.add(key)
                members.append(member)
        return tuple(members)

    @staticmethod
    def _parse_enum_members(raw_members: object) -> tuple[EnumMember, ...]:
        members: list[EnumMember] = []
        seen: set[str] = set()
        for item in raw_members or []:
            if not isinstance(item, dict):
                continue
            name = item.get("name")
            value = item.get("value")
            if isinstance(name, str) and isinstance(value, str):
                member = EnumMember(name=name, value=value)
                key = JsonlContractRepository._enum_member_key(member)
                if key in seen:
                    continue
                seen.add(key)
                members.append(member)
        return tuple(members)

    @staticmethod
    def _parse_operations(raw_operations: object) -> tuple[OperationContract, ...]:
        operations: list[OperationContract] = []
        seen: set[str] = set()
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

            operation = OperationContract(
                name=name,
                return_type=return_type,
                effective_return_type=effective_return_type,
                is_one_way=is_one_way,
                parameters=tuple(params),
            )
            key = JsonlContractRepository._operation_key(operation)
            if key in seen:
                continue
            seen.add(key)
            operations.append(operation)

        return tuple(operations)

    @staticmethod
    def _data_member_key(item: DataMember) -> str:
        return f"{item.name}|{item.type_name}"

    @staticmethod
    def _enum_member_key(item: EnumMember) -> str:
        return f"{item.name}|{item.value}"

    @staticmethod
    def _operation_key(item: OperationContract) -> str:
        params = ",".join(
            f"{p.name}:{p.type_name}:{p.is_out}:{p.is_ref}:{p.is_optional}"
            for p in item.parameters
        )
        return f"{item.name}|{item.return_type}|{item.effective_return_type}|{item.is_one_way}|{params}"
