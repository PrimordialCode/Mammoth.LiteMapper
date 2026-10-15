using System;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Samples.Basic
{
    internal static class NestedTargetPolicyExample
    {
        public static void Run()
        {
            var mapped = NestedPolicyMapper.Map(new NestedPolicySource
            {
                Child = new NestedPolicyChild { Value = 7 },
            });
            if (mapped.Child.Value != 7 || mapped.Child.Extra != 12 || mapped.Child.Note != null)
            {
                throw new InvalidOperationException("Nested target policy failed.");
            }
        }
    }

    [LiteMapper(UnmappedTargetMembers = UnmappedMemberPolicy.Ignore)]
    internal static partial class NestedPolicyMapper
    {
        public static partial NestedPolicyTarget Map(NestedPolicySource source);
    }

    internal sealed class NestedPolicySource { public NestedPolicyChild Child { get; set; } = new NestedPolicyChild(); }
    internal sealed class NestedPolicyChild { public int Value { get; set; } }
    internal sealed class NestedPolicyTarget { public NestedPolicyChildTarget Child { get; set; } = null!; }
    internal sealed class NestedPolicyChildTarget
    {
        public int Value { get; set; }
        public int Extra { get; set; } = 12;
        public string? Note { get; set; }
    }
}
