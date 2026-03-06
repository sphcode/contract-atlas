namespace ContractScanner.Core.Domain.Models;

public readonly record struct OperationContractInfo(
    string Name,
    string ReturnType,
    string EffectiveReturnType,
    bool IsOneWay,
    OperationParameterInfo[] Parameters);
