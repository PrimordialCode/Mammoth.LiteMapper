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
        public static readonly DiagnosticDescriptor UnmappedTargetMember = Warning("LITEMAPPER1001", "UnmappedTargetMember", "Target member '{0}' is not mapped");
        public static readonly DiagnosticDescriptor UnmappedTargetMemberError = Error("LITEMAPPER1001", "UnmappedTargetMember", "Target member '{0}' is not mapped");
        public static readonly DiagnosticDescriptor AmbiguousMemberMatch = Error("LITEMAPPER1004", "AmbiguousMemberMatch", "Multiple source members match target member '{0}'");
        public static readonly DiagnosticDescriptor HiddenMemberSelected = Warning("LITEMAPPER1005", "HiddenMemberSelected", "Hidden member '{0}' was selected");
        public static readonly DiagnosticDescriptor RequiredTargetMemberNotMapped = Error("LITEMAPPER1002", "RequiredTargetMemberNotMapped", "Required target member '{0}' is not mapped");
        public static readonly DiagnosticDescriptor ConstructorNotFound = Error("LITEMAPPER1010", "ConstructorNotFound", "Target type '{0}' does not have a legal construction path");
        public static readonly DiagnosticDescriptor AmbiguousConstructor = Error("LITEMAPPER1011", "AmbiguousConstructor", "Target type '{0}' has ambiguous satisfiable constructors");
        public static readonly DiagnosticDescriptor MultipleMappingConstructors = Error("LITEMAPPER1012", "MultipleMappingConstructors", "Target type '{0}' has multiple mapping constructors");
        public static readonly DiagnosticDescriptor MappingConstructorNotSatisfiable = Error("LITEMAPPER1013", "MappingConstructorNotSatisfiable", "Mapping constructor for target type '{0}' cannot be satisfied");
        public static readonly DiagnosticDescriptor NullableToNonNullable = Error("LITEMAPPER2001", "NullableToNonNullable", "Potential null value cannot be mapped to non-null target '{0}'");
        public static readonly DiagnosticDescriptor ConversionNotFound = Error("LITEMAPPER2004", "ConversionNotFound", "No conversion exists from '{0}' to '{1}'");
        public static readonly DiagnosticDescriptor InvalidMemberConfiguration = Error("LITEMAPPER2005", "InvalidMemberConfiguration", "Member configuration '{0}' is invalid");
        public static readonly DiagnosticDescriptor InvalidConverter = Error("LITEMAPPER2006", "InvalidConverter", "Converter '{0}' is invalid");
        public static readonly DiagnosticDescriptor AmbiguousConverter = Error("LITEMAPPER2007", "AmbiguousConverter", "Converter resolution for '{0}' is ambiguous");
        public static readonly DiagnosticDescriptor RuntimeObjectDispatchNotSupported = Error("LITEMAPPER2012", "RuntimeObjectDispatchNotSupported", "Runtime object dispatch is required for '{0}'");
        public static readonly DiagnosticDescriptor AmbiguousMapping = Error("LITEMAPPER3001", "AmbiguousMapping", "Mapping resolution for '{0}' is ambiguous");
        public static readonly DiagnosticDescriptor StructuralNestedMappingFailed = Error("LITEMAPPER3004", "StructuralNestedMappingFailed", "Structural nested mapping from '{0}' to '{1}' cannot be generated");
        public static readonly DiagnosticDescriptor AbstractDestinationNotSupported = Error("LITEMAPPER3005", "AbstractDestinationNotSupported", "Destination type '{0}' is abstract or an interface");
        public static readonly DiagnosticDescriptor InvalidNullCollectionMapping = Error("LITEMAPPER2002", "InvalidNullCollectionMapping", "Null collection strategy cannot satisfy target nullability");
        public static readonly DiagnosticDescriptor UnsupportedCollectionShape = Error("LITEMAPPER4001", "UnsupportedCollectionShape", "Collection shape is outside 1.0 support");
        public static readonly DiagnosticDescriptor RectangularArrayNotSupported = Error("LITEMAPPER4002", "RectangularArrayNotSupported", "Mapping uses a multidimensional rectangular array");
        public static readonly DiagnosticDescriptor CollectionTargetCannotBeConstructed = Error("LITEMAPPER4003", "CollectionTargetCannotBeConstructed", "No legal concrete destination collection can be created");
        public static readonly DiagnosticDescriptor CustomCollectionNotSupported = Error("LITEMAPPER4006", "CustomCollectionNotSupported", "Custom collection convention would be required");
        public static readonly DiagnosticDescriptor UnmappedSourceMember = Info("LITEMAPPER1003", "UnmappedSourceMember", "Source member '{0}' is not mapped");
        public static readonly DiagnosticDescriptor UnmappedSourceMemberWarning = Warning("LITEMAPPER1003", "UnmappedSourceMember", "Source member '{0}' is not mapped");
        public static readonly DiagnosticDescriptor InternalGeneratorFailure = Error("LITEMAPPER9001", "InternalGeneratorFailure", "Unexpected failure while processing mapper '{0}'");

        private static DiagnosticDescriptor Error(string id, string title, string message)
        {
            return new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);
        }

        private static DiagnosticDescriptor Warning(string id, string title, string message)
        {
            return new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
        }

        private static DiagnosticDescriptor Info(string id, string title, string message)
        {
            return new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Info, isEnabledByDefault: true);
        }
    }
}
