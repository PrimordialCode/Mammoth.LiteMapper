namespace Mammoth.LiteMapper
{
    public enum NameMatching
    {
        Unspecified = 0,
        Exact = 1,
        ExactThenIgnoreCase = 2,
        IgnoreCase = 3
    }

    public enum UnmappedMemberPolicy
    {
        Unspecified = 0,
        Ignore = 1,
        Info = 2,
        Warning = 3,
        Error = 4
    }

    public enum NullableMismatchPolicy
    {
        Unspecified = 0,
        Error = 1,
        Throw = 2
    }

    public enum NullCollectionStrategy
    {
        Unspecified = 0,
        Error = 1,
        Preserve = 2,
        Empty = 3
    }

    public enum NumericConversion
    {
        Unspecified = 0,
        ImplicitOnly = 1,
        Checked = 2,
        Unchecked = 3
    }

    public enum EnumMappingStrategy
    {
        Unspecified = 0,
        ByName = 1,
        ByValue = 2
    }

    public enum EnumNumericConversion
    {
        Unspecified = 0,
        Checked = 1,
        Unchecked = 2
    }

    public enum UnmatchedEnumValuePolicy
    {
        Unspecified = 0,
        Error = 1,
        Throw = 2,
        ByValue = 3
    }

    public enum ReferenceHandling
    {
        Unspecified = 0,
        None = 1,
        ThrowOnCycle = 2
    }

    public enum OptionState
    {
        Unspecified = 0,
        Disabled = 1,
        Enabled = 2
    }
}
