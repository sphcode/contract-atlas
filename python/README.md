# ContractAtlas Python Pipeline

Python is responsible for transforming C# scanner intermediate artifacts into OpenAPI YAML.

## Layering

- `src/contract_atlas/application`: use-case orchestration
- `src/contract_atlas/domain`: core models
- `src/contract_atlas/infrastructure`: file IO, mapping, YAML output
- `src/contract_atlas/cli`: command-line entry

## Input

From repository root:

- `output/contracts.jsonl`
- `output/data-members.jsonl`

## Run

```bash
python3 python/scripts/generate_openapi.py \
  --contracts output/contracts.jsonl \
  --data-members output/data-members.jsonl \
  --output output/openapi.yaml
```
