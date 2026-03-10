from __future__ import annotations

from dataclasses import dataclass

from contract_atlas.domain.models import ContractSnapshot, DataContract


PRIMITIVE_SCHEMA_MAP = {
    "bool": {"type": "boolean"},
    "boolean": {"type": "boolean"},
    "byte": {"type": "integer", "format": "int32"},
    "short": {"type": "integer", "format": "int32"},
    "int": {"type": "integer", "format": "int32"},
    "long": {"type": "integer", "format": "int64"},
    "float": {"type": "number", "format": "float"},
    "double": {"type": "number", "format": "double"},
    "decimal": {"type": "number", "format": "double"},
    "string": {"type": "string"},
    "guid": {"type": "string", "format": "uuid"},
    "datetime": {"type": "string", "format": "date-time"},
    "object": {"type": "object"},
    "void": {"type": "null"},
}


@dataclass
class OpenApiDocumentBuilder:
    def build(self, snapshot: ContractSnapshot) -> dict:
        components = {"schemas": {}}
        schema_name_map = self._build_schema_name_map(snapshot)

        for enum_name, enum_contract in sorted(snapshot.enum_contracts.items()):
            schema_key = schema_name_map[enum_name]
            components["schemas"][schema_key] = {
                "type": "string",
                "enum": [item.name for item in enum_contract.members],
                "x-enum-values": [{"name": item.name, "value": item.value} for item in enum_contract.members],
            }

        for data_name, data_contract in sorted(snapshot.data_contracts.items()):
            schema_key = schema_name_map[data_name]
            components["schemas"][schema_key] = self._build_data_contract_schema(
                data_contract,
                schema_name_map,
            )

        paths = {}
        for service in snapshot.service_contracts:
            service_name = self._short_name(service.name)
            tag = service_name
            for operation in service.operations:
                path = f"/rpc/{service_name}/{operation.name}"
                request_properties = {}
                required = []
                for parameter in operation.parameters:
                    request_properties[parameter.name] = self._type_to_schema(parameter.type_name, schema_name_map)
                    if not parameter.is_optional:
                        required.append(parameter.name)

                operation_item = {
                    "tags": [tag],
                    "operationId": f"{service_name}_{operation.name}",
                    "requestBody": {
                        "required": True,
                        "content": {
                            "application/json": {
                                "schema": {
                                    "type": "object",
                                    "properties": request_properties,
                                    "required": required,
                                }
                            }
                        },
                    },
                    "responses": {
                        "200": {
                            "description": "Successful response",
                            "content": {
                                "application/json": {
                                    "schema": self._type_to_schema(operation.effective_return_type, schema_name_map)
                                }
                            },
                        }
                    },
                }

                if operation.is_one_way:
                    operation_item["responses"]["202"] = {
                        "description": "Accepted (one-way operation)",
                    }

                paths[path] = {"post": operation_item}

        return {
            "openapi": "3.0.3",
            "info": {
                "title": "ContractAtlas Generated API",
                "version": "0.1.0",
                "description": "Generated from WCF contract scanner artifacts.",
            },
            "paths": paths,
            "components": components,
        }

    def _build_data_contract_schema(self, contract: DataContract, schema_name_map: dict[str, str]) -> dict:
        properties = {}
        required = []
        for member in contract.members:
            properties[member.name] = self._type_to_schema(member.type_name, schema_name_map)
            if not member.type_name.endswith("?"):
                required.append(member.name)

        return {
            "type": "object",
            "properties": properties,
            "required": required,
        }

    def _type_to_schema(self, type_name: str, schema_name_map: dict[str, str]) -> dict:
        normalized = type_name.strip()
        nullable = normalized.endswith("?")
        if nullable:
            normalized = normalized[:-1]

        if normalized.endswith("[]"):
            item_schema = self._type_to_schema(normalized[:-2], schema_name_map)
            return self._nullable({"type": "array", "items": item_schema}, nullable)

        primitive = PRIMITIVE_SCHEMA_MAP.get(normalized.lower())
        if primitive is not None:
            return self._nullable(dict(primitive), nullable)

        full_name = self._resolve_full_type_name(normalized, schema_name_map)
        if full_name is not None:
            return self._nullable({"$ref": f"#/components/schemas/{schema_name_map[full_name]}"}, nullable)

        return self._nullable({"type": "string", "x-original-type": normalized}, nullable)

    @staticmethod
    def _nullable(schema: dict, nullable: bool) -> dict:
        if nullable:
            schema["nullable"] = True
        return schema

    @staticmethod
    def _build_schema_name_map(snapshot: ContractSnapshot) -> dict[str, str]:
        keys = list(snapshot.data_contracts.keys()) + list(snapshot.enum_contracts.keys())
        result: dict[str, str] = {}
        used: set[str] = set()
        for full_name in sorted(keys):
            base = full_name.replace(".", "_").replace("+", "_")
            candidate = base
            suffix = 2
            while candidate in used:
                candidate = f"{base}_{suffix}"
                suffix += 1
            used.add(candidate)
            result[full_name] = candidate
        return result

    @staticmethod
    def _short_name(full_name: str) -> str:
        if "." in full_name:
            return full_name.rsplit(".", 1)[1]
        return full_name

    @staticmethod
    def _resolve_full_type_name(type_name: str, schema_name_map: dict[str, str]) -> str | None:
        if type_name in schema_name_map:
            return type_name

        short = type_name.rsplit(".", 1)[-1]
        matches = [name for name in schema_name_map if name.rsplit(".", 1)[-1] == short]
        if len(matches) == 1:
            return matches[0]
        return None
