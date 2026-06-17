using Microsoft.CodeAnalysis;

namespace Mammoth.LiteMapper.Generator
{
    internal static class Diagnostics
    {
        private const string Category = "Mammoth.LiteMapper";

        public static readonly DiagnosticDescriptor MapperMustBePartial = Error("LITEMAPPER0001", "MapperMustBePartial", "Mapper or containing type '{0}' must be partial");
        public static readonly DiagnosticDescriptor UnsupportedMapperType = Error("LITEMAPPER0002", "UnsupportedMapperType", "Mapper '{0}' uses an unsupported declaration kind");
        public static readonly DiagnosticDescriptor GenericMapperNotSupported = Error("LITEMAPPER0003", "GenericMapperNotSupported", "Mapper or containing type '{0}' must not be generic");
        public static readonly DiagnosticDescriptor AbstractMapperNotSupported = Error("LITEMAPPER0004", "AbstractMapperNotSupported", "Instance mapper '{0}' must not be abstract");
        public static readonly DiagnosticDescriptor InvalidMappingMethodSignature = Error("LITEMAPPER0005", "InvalidMappingMethodSignature", "Mapping method '{0}' has an unsupported signature");
        public static readonly DiagnosticDescriptor AsyncMappingNotSupported = Error("LITEMAPPER0006", "AsyncMappingNotSupported", "Mapping method or converter '{0}' must not be asynchronous");
        public static readonly DiagnosticDescriptor UnsupportedMappingAccessibility = Error("LITEMAPPER0007", "UnsupportedMappingAccessibility", "Mapping method '{0}' has unsupported accessibility");
        public static readonly DiagnosticDescriptor InvalidMapperConfiguration = Error("LITEMAPPER0008", "InvalidMapperConfiguration", "Mapper configuration '{0}' is invalid");
        public static readonly DiagnosticDescriptor InvalidMethodConfiguration = Error("LITEMAPPER0009", "InvalidMethodConfiguration", "Method configuration '{0}' is invalid");
        public static readonly DiagnosticDescriptor InvalidUseMapperType = Error("LITEMAPPER0010", "InvalidUseMapperType", "Registered mapper type '{0}' is invalid");
        public static readonly DiagnosticDescriptor ExternalMapperMustBeStatic = Error("LITEMAPPER0011", "ExternalMapperMustBeStatic", "Registered mapper type '{0}' must be static");
        public static readonly DiagnosticDescriptor UnsupportedLanguageVersion = Error("LITEMAPPER0012", "UnsupportedLanguageVersion", "Language version '{0}' is older than C# 9");
        public static readonly DiagnosticDescriptor DuplicateConfiguration = Error("LITEMAPPER0013", "DuplicateConfiguration", "Configuration '{0}' is declared more than once");
        public static readonly DiagnosticDescriptor InternalGeneratorFailure = Error("LITEMAPPER9001", "InternalGeneratorFailure", "Unexpected failure while processing mapper '{0}'");

        private static DiagnosticDescriptor Error(string id, string title, string message)
        {
            return new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);
        }
    }
}
