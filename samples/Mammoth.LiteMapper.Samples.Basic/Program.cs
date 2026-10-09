using System;
using System.Collections.Generic;
using Mammoth.LiteMapper;

namespace Mammoth.LiteMapper.Samples.Basic
{
    internal static class Program
    {
        private static void Main()
        {
            LazyCyclePathExample.Run();
            EnumPathExample.Run();
            PatchUpdaterExample.Run();
            var staticTarget = StaticMapper.Map(new StaticSource
            {
                Name = "Ada",
                Children = new[]
                {
                    new ChildSource { Value = 1 },
                    new ChildSource { Value = 2 },
                },
            });
            if (staticTarget.Name != "Ada" || staticTarget.DisplayName != "Name: Ada" || staticTarget.Children.Count != 2 || staticTarget.Children[1].Value != 2)
            {
                throw new InvalidOperationException("Static mapping failed.");
            }

            var instanceSource = new InstanceSource
            {
                Id = 42,
                Child = new ChildSource { Value = 7 },
                Children = new[] { new ChildSource { Value = 8 } },
            };
            var instanceTarget = new InstanceMapper("first:").Map(instanceSource);
            var otherTarget = new InstanceMapper("second:").Map(instanceSource);
            if (instanceTarget.Id != 42 || instanceTarget.Child.Value != "first:7" ||
                instanceTarget.Children[0].Value != "first:8" || otherTarget.Child.Value != "second:7" ||
                otherTarget.Children[0].Value != "second:8")
            {
                throw new InvalidOperationException("Instance mapping failed.");
            }

            var sharedNode = new NodeSource();
            var wrapped = new WrappedSource { Envelope = new EnvelopeSource { Nodes = new[] { sharedNode, sharedNode } } };
            var wrappedTarget = WrappedCycleMapper.Map(wrapped);
            if (wrappedTarget.Envelope.Nodes.Count != 2 ||
                ReferenceEquals(wrappedTarget.Envelope.Nodes[0], wrappedTarget.Envelope.Nodes[1]))
            {
                throw new InvalidOperationException("Wrapped mapping failed.");
            }
            sharedNode.Next = sharedNode;
            try
            {
                WrappedCycleMapper.Map(wrapped);
                throw new InvalidOperationException("Wrapped cycle detection failed.");
            }
            catch (LiteMapperCycleException error) when (
                error.MappingMethod == "Map" && error.MemberPath == "Envelope.Nodes.Next")
            {
            }

            var node = new NodeSource();
            node.Next = node;
            try
            {
                CycleMapper.Map(node);
                throw new InvalidOperationException("Cycle detection failed.");
            }
            catch (LiteMapperCycleException)
            {
            }
        }
    }

    [LiteMapper]
    public static partial class StaticMapper
    {
        [MapProperty(Target = nameof(StaticTarget.DisplayName), Use = nameof(BuildDisplayName))]
        public static partial StaticTarget Map(StaticSource source);

        private static string BuildDisplayName(StaticSource source) => "Name: " + source.Name;
    }

    [System.Diagnostics.DebuggerDisplay("Static mapper")]
    public static partial class StaticMapper
    {
    }

    [LiteMapper]
    public sealed partial class InstanceMapper
    {
        private readonly string prefix;

        public InstanceMapper(string prefix = "") => this.prefix = prefix;

        public partial InstanceTarget Map(InstanceSource source);

        [MappingConverter]
        private string Format(int value) => prefix + value;
    }

    [LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
    public static partial class CycleMapper
    {
        public static partial NodeTarget Map(NodeSource source);
    }

    public sealed class StaticSource
    {
        public string? Name { get; set; }

        public ChildSource[] Children { get; set; } = new ChildSource[0];
    }

    public sealed class StaticTarget
    {
        public string DisplayName { get; set; } = string.Empty;

        public string? Name { get; set; }

        public List<ChildTarget> Children { get; set; } = new List<ChildTarget>();
    }

    public sealed class ChildSource
    {
        public int Value { get; set; }
    }

    public sealed class ChildTarget
    {
        public int Value { get; set; }
    }

    public sealed class InstanceSource
    {
        public int Id { get; set; }

        public ChildSource Child { get; set; } = new ChildSource();

        public ChildSource[] Children { get; set; } = new ChildSource[0];
    }

    public sealed class InstanceTarget
    {
        public int Id { get; set; }

        public FormattedChildTarget Child { get; set; } = new FormattedChildTarget();

        public List<FormattedChildTarget> Children { get; set; } = new List<FormattedChildTarget>();
    }

    public sealed class FormattedChildTarget
    {
        public string Value { get; set; } = string.Empty;
    }

    [LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
    public static partial class WrappedCycleMapper
    {
        public static partial WrappedTarget Map(WrappedSource source);
    }

    public sealed class WrappedSource
    {
        public EnvelopeSource Envelope { get; set; } = new EnvelopeSource();
    }

    public sealed class WrappedTarget
    {
        public EnvelopeTarget Envelope { get; set; } = new EnvelopeTarget();
    }

    public sealed class EnvelopeSource
    {
        public NodeSource[] Nodes { get; set; } = new NodeSource[0];
    }

    public sealed class EnvelopeTarget
    {
        public List<NodeTarget> Nodes { get; set; } = new List<NodeTarget>();
    }

    public sealed class NodeSource
    {
        public NodeSource? Next { get; set; }
    }

    public sealed class NodeTarget
    {
        public NodeTarget? Next { get; set; }
    }
}

namespace Mammoth.LiteMapper.Samples.Basic
{
    public static class EnumPathExample
    {
        public static void Run()
        {
            var source = new EnumPathSource { Child = new EnumPathChild { State = InputState.Ready } };
            if (EnumPathMapper.Map(source).State != OutputState.Ready)
                throw new System.InvalidOperationException("Enum source-path mapping failed.");
            source.Child.State = (InputState)99;
            try
            {
                EnumPathMapper.Map(source);
                throw new System.InvalidOperationException("Unknown enum value was accepted.");
            }
            catch (System.ArgumentOutOfRangeException error) when (
                error.ParamName == "Child.State" && error.ActualValue is InputState value && value == (InputState)99)
            {
            }
            source.Child = null;
            try
            {
                EnumPathMapper.Map(source);
                throw new System.InvalidOperationException("Missing enum source path was accepted.");
            }
            catch (System.InvalidOperationException error) when (error.Message.Contains("Child.State"))
            {
            }
        }
    }

    [LiteMapper(NullableMismatch = NullableMismatchPolicy.Throw)]
    public static partial class EnumPathMapper
    {
        [MapProperty(Source = "Child.State", Target = nameof(EnumPathTarget.State))]
        public static partial EnumPathTarget Map(EnumPathSource source);
    }

    public enum InputState { Ready = 1 }
    public enum OutputState { Ready = 10 }
    public sealed class EnumPathSource { public EnumPathChild? Child { get; set; } }
    public sealed class EnumPathChild { public InputState State { get; set; } }
    public sealed class EnumPathTarget { public OutputState State { get; set; } }
}

namespace Mammoth.LiteMapper.Samples.Basic
{
    public static class PatchUpdaterExample
    {
        public static void Run()
        {
            var source = new PatchUpdaterSource(new PatchUpdaterChild { Value = 7 });
            var target = new PatchUpdaterTarget();
            var original = target.Child;
            PatchUpdaterMapper.Apply(source, target);
            if (source.Reads != 1 || target.Child.Value != 7 || !object.ReferenceEquals(original, target.Child))
                throw new System.InvalidOperationException("Patch updater must use its captured child once.");
            var missing = new PatchUpdaterSource(null);
            PatchUpdaterMapper.Apply(missing, target);
            if (missing.Reads != 1 || target.Child.Value != 7 || !object.ReferenceEquals(original, target.Child))
                throw new System.InvalidOperationException("A null patch child must preserve the existing destination.");
        }
    }

    [LiteMapper(IgnoreNullSourceMembers = true)]
    public static partial class PatchUpdaterMapper
    {
        public static partial void Apply(PatchUpdaterSource source, PatchUpdaterTarget target);
        public static partial void ApplyChild(PatchUpdaterChild source, PatchUpdaterChildTarget target);
    }

    public sealed class PatchUpdaterSource
    {
        private readonly PatchUpdaterChild? first;
        public PatchUpdaterSource(PatchUpdaterChild? first) { this.first = first; }
        public int Reads;
        public PatchUpdaterChild? Child { get { Reads++; return Reads == 1 ? first : null; } }
    }
    public sealed class PatchUpdaterChild { public int Value { get; set; } }
    public sealed class PatchUpdaterChildTarget { public int Value { get; set; } }
    public sealed class PatchUpdaterTarget { public PatchUpdaterChildTarget Child { get; } = new PatchUpdaterChildTarget(); }
}
