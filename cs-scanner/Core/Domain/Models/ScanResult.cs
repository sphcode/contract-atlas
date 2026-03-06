namespace ContractScanner.Core.Domain.Models;

public readonly record struct ScanResult(
    string Type,
    string Name,
    DataMemberInfo[]? DataMembers = null,
    EnumMemberInfo[]? EnumMembers = null,
    OperationContractInfo[]? OperationContracts = null);
