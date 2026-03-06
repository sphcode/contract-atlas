using System;
using System.Collections.Generic;
using ContractScanner.Core.Domain.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class SyntaxDataMemberCollector
{
    public static DataMemberInfo[]? Collect(string matchedType, TypeDeclarationSyntax typeDecl)
    {
        if (!string.Equals(matchedType, "DataContract", StringComparison.Ordinal))
        {
            return null;
        }

        var members = new List<DataMemberInfo>();
        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property
                    when SyntaxAttributeMatcher.HasAttribute(property.AttributeLists, "DataMember"):
                    members.Add(new DataMemberInfo(property.Identifier.Text, property.Type.ToString()));
                    break;
                case FieldDeclarationSyntax field
                    when SyntaxAttributeMatcher.HasAttribute(field.AttributeLists, "DataMember"):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        members.Add(new DataMemberInfo(variable.Identifier.Text, field.Declaration.Type.ToString()));
                    }
                    break;
            }
        }

        return members.Count > 0 ? members.ToArray() : null;
    }
}
