using System;

namespace Mammoth.LiteMapper
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class LiteMapperAttribute : Attribute
    {
        public NameMatching NameMatching { get; set; }

        public UnmappedMemberPolicy UnmappedTargetMembers { get; set; }

        public UnmappedMemberPolicy UnmappedSourceMembers { get; set; }

        public NullableMismatchPolicy NullableMismatch { get; set; }

        public NullCollectionStrategy NullCollections { get; set; }

        public NumericConversion NumericConversion { get; set; }

        public bool AllowExplicitOperators { get; set; }

        public EnumMappingStrategy EnumMapping { get; set; }

        public EnumNumericConversion EnumNumericConversion { get; set; }

        public UnmatchedEnumValuePolicy UnmatchedEnumValues { get; set; }

        public ReferenceHandling ReferenceHandling { get; set; }

        public bool IgnoreNullSourceMembers { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class MappingOptionsAttribute : Attribute
    {
        public NameMatching NameMatching { get; set; }

        public UnmappedMemberPolicy UnmappedTargetMembers { get; set; }

        public UnmappedMemberPolicy UnmappedSourceMembers { get; set; }

        public NullableMismatchPolicy NullableMismatch { get; set; }

        public NullCollectionStrategy NullCollections { get; set; }

        public NumericConversion NumericConversion { get; set; }

        public OptionState AllowExplicitOperators { get; set; }

        public EnumMappingStrategy EnumMapping { get; set; }

        public EnumNumericConversion EnumNumericConversion { get; set; }

        public UnmatchedEnumValuePolicy UnmatchedEnumValues { get; set; }

        public ReferenceHandling ReferenceHandling { get; set; }

        public OptionState IgnoreNullSourceMembers { get; set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class LiteMapperDefaultsAttribute : Attribute
    {
        public NameMatching NameMatching { get; set; }

        public UnmappedMemberPolicy UnmappedTargetMembers { get; set; }

        public UnmappedMemberPolicy UnmappedSourceMembers { get; set; }

        public NullableMismatchPolicy NullableMismatch { get; set; }

        public NullCollectionStrategy NullCollections { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class UseMapperAttribute : Attribute
    {
        public UseMapperAttribute(Type mapperType)
        {
            MapperType = mapperType;
        }

        public Type MapperType { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class DefaultMappingAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class MappingConverterAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class MappingConstructorAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class MapPropertyAttribute : Attribute
    {
        public string? Source { get; set; }

        public string? Target { get; set; }

        public string? Use { get; set; }

        public Type? ConverterType { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class IgnoreTargetAttribute : Attribute
    {
        public IgnoreTargetAttribute(string member)
        {
            Member = member;
        }

        public string Member { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class IgnoreSourceAttribute : Attribute
    {
        public IgnoreSourceAttribute(string member)
        {
            Member = member;
        }

        public string Member { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class UseTargetDefaultAttribute : Attribute
    {
        public UseTargetDefaultAttribute(string member)
        {
            Member = member;
        }

        public string Member { get; }
    }
}
