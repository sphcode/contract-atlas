namespace ContractScanner.Core.Domain.Models;

public readonly record struct OperationParameterInfo(
    string Name,
    string Type,
    bool IsOut,
    bool IsRef,
    bool IsOptional);
