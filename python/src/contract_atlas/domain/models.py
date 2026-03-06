from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(frozen=True)
class DataMember:
    name: str
    type_name: str


@dataclass(frozen=True)
class OperationParameter:
    name: str
    type_name: str
    is_out: bool
    is_ref: bool
    is_optional: bool


@dataclass(frozen=True)
class OperationContract:
    name: str
    return_type: str
    effective_return_type: str
    is_one_way: bool
    parameters: tuple[OperationParameter, ...]


@dataclass(frozen=True)
class EnumMember:
    name: str
    value: str


@dataclass(frozen=True)
class ServiceContract:
    name: str
    operations: tuple[OperationContract, ...] = ()


@dataclass(frozen=True)
class DataContract:
    name: str
    members: tuple[DataMember, ...] = ()


@dataclass(frozen=True)
class EnumContract:
    name: str
    members: tuple[EnumMember, ...] = ()


@dataclass
class ContractSnapshot:
    service_contracts: list[ServiceContract] = field(default_factory=list)
    data_contracts: dict[str, DataContract] = field(default_factory=dict)
    enum_contracts: dict[str, EnumContract] = field(default_factory=dict)
