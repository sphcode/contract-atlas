namespace ContractScanner.Core;

public readonly record struct ScanResult(string Type, string Name, DataMemberInfo[]? DataMembers = null);
