using System;

namespace Mammoth.LiteMapper.Samples.Basic
{
    public static class NullableEnumPathExample
    {
        public static void Run()
        {
            foreach (var map in new Func<NullableEnumPathSource, NullableEnumOutputState?>[]
            {
                source => NullableEnumPathMapper.Create(source).State,
                source => NullableEnumPathMapper.Construct(source).State,
                Update,
            })
            {
                Check(map, null, null);
                Check(map, NullableEnumInputState.Ready, NullableEnumOutputState.Ready);
                Check(map, (NullableEnumInputState)99, null, throws: true);
            }

            Check(source => NullableEnumPathMapper.ByValue(source).State, null, null);
            Check(source => NullableEnumPathMapper.ByValue(source).State,
                NullableEnumInputState.Ready, (NullableEnumOutputState)1);
            Check(source => NullableEnumPathMapper.ByValue(source).State,
                (NullableEnumInputState)99, (NullableEnumOutputState)99);

            var target = new NullableEnumPathTarget { State = (NullableEnumOutputState)77 };
            Func<NullableEnumPathSource, NullableEnumOutputState?> patch = source =>
            {
                NullableEnumPathMapper.Patch(source, target);
                return target.State;
            };
            Check(patch, null, (NullableEnumOutputState)77);
            Check(patch, NullableEnumInputState.Ready, NullableEnumOutputState.Ready);
            Check(patch, (NullableEnumInputState)99, null, throws: true);
            if (target.State != NullableEnumOutputState.Ready)
                throw new InvalidOperationException("A failed enum conversion must not replace the patched value.");
        }

        private static NullableEnumOutputState? Update(NullableEnumPathSource source)
        {
            var target = new NullableEnumPathTarget { State = (NullableEnumOutputState)77 };
            if (!ReferenceEquals(target, NullableEnumPathMapper.Apply(source, target)))
                throw new InvalidOperationException("An enum update must preserve the destination reference.");
            return target.State;
        }

        private static void Check(
            Func<NullableEnumPathSource, NullableEnumOutputState?> map,
            NullableEnumInputState? state,
            NullableEnumOutputState? expected,
            bool throws = false)
        {
            var child = state.HasValue ? new NullableEnumPathChild(state.Value) : null;
            var source = new NullableEnumPathSource(child);
            try
            {
                var actual = map(source);
                if (throws || actual != expected)
                    throw new InvalidOperationException("Nullable enum paths must preserve null and the selected enum strategy.");
            }
            catch (ArgumentOutOfRangeException error) when (
                throws && error.ParamName == "Child.State" &&
                error.ActualValue is NullableEnumInputState value && value == (NullableEnumInputState)99)
            {
            }

            if (source.Reads != 1 || (child != null && child.Reads != 1))
                throw new InvalidOperationException("Each enum source-path getter must be evaluated once.");
        }
    }

    [LiteMapper]
    public static partial class NullableEnumPathMapper
    {
        [MapProperty(Source = "Child.State", Target = nameof(NullableEnumPathTarget.State))]
        public static partial NullableEnumPathTarget Create(NullableEnumPathSource source);

        [MapProperty(Source = "Child.State", Target = nameof(NullableEnumPathConstructorTarget.State))]
        public static partial NullableEnumPathConstructorTarget Construct(NullableEnumPathSource source);

        [MapProperty(Source = "Child.State", Target = nameof(NullableEnumPathTarget.State))]
        public static partial NullableEnumPathTarget Apply(NullableEnumPathSource source, NullableEnumPathTarget target);

        [MappingOptions(EnumMapping = EnumMappingStrategy.ByValue)]
        [MapProperty(Source = "Child.State", Target = nameof(NullableEnumPathTarget.State))]
        public static partial NullableEnumPathTarget ByValue(NullableEnumPathSource source);

        [MappingOptions(IgnoreNullSourceMembers = OptionState.Enabled)]
        [MapProperty(Source = "Child.State", Target = nameof(NullableEnumPathTarget.State))]
        public static partial void Patch(NullableEnumPathSource source, NullableEnumPathTarget target);
    }

    public enum NullableEnumInputState { Ready = 1 }
    public enum NullableEnumOutputState { Ready = 10 }

    public sealed class NullableEnumPathSource
    {
        private readonly NullableEnumPathChild? child;
        public NullableEnumPathSource(NullableEnumPathChild? child) { this.child = child; }
        public int Reads { get; private set; }
        public NullableEnumPathChild? Child { get { Reads++; return Reads == 1 ? child : null; } }
    }

    public sealed class NullableEnumPathChild
    {
        private readonly NullableEnumInputState state;
        public NullableEnumPathChild(NullableEnumInputState state) { this.state = state; }
        public int Reads { get; private set; }
        public NullableEnumInputState State { get { Reads++; return Reads == 1 ? state : (NullableEnumInputState)98; } }
    }

    public sealed class NullableEnumPathTarget { public NullableEnumOutputState? State { get; set; } }

    public sealed class NullableEnumPathConstructorTarget
    {
        public NullableEnumPathConstructorTarget(NullableEnumOutputState? state) { State = state; }
        public NullableEnumOutputState? State { get; }
    }
}
