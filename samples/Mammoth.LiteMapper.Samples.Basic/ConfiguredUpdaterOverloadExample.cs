using System;

namespace Mammoth.LiteMapper.Samples.Basic
{
    public static class ConfiguredUpdaterOverloadExample
    {
        public static void Run()
        {
            var direct = new ConfiguredUpdaterSource(new ConfiguredUpdaterValue { Value = 7 });
            var target = new ConfiguredUpdaterTarget();
            var child = target.Child;
            ConfiguredUpdaterMapper.ApplyDirect(direct, target);
            if (direct.Reads != 1 || target.Child.Value != 7 || !ReferenceEquals(child, target.Child))
                throw new InvalidOperationException("The configured direct path must preserve the nullable updater overload.");

            var leaf = new ConfiguredUpdaterSource(new ConfiguredUpdaterValue { Value = 11 });
            var path = new ConfiguredUpdaterPath(leaf);
            var returned = ConfiguredUpdaterMapper.ApplyPath(path, target);
            if (path.Reads != 1 || leaf.Reads != 1 || target.Child.Value != 11 ||
                !ReferenceEquals(target, returned) || !ReferenceEquals(child, target.Child))
                throw new InvalidOperationException("The configured dotted path must consume its selected nullable value once.");

            var missingLeaf = new ConfiguredUpdaterSource(null);
            ConfiguredUpdaterMapper.ApplyDirect(missingLeaf, target);
            var missingPath = new ConfiguredUpdaterPath(null);
            ConfiguredUpdaterMapper.ApplyPath(missingPath, target);
            if (missingLeaf.Reads != 1 || missingPath.Reads != 1 || target.Child.Value != 11 ||
                !ReferenceEquals(child, target.Child))
                throw new InvalidOperationException("A missing configured value must preserve the existing nested target.");
        }
    }

    [LiteMapper(IgnoreNullSourceMembers = true)]
    public static partial class ConfiguredUpdaterMapper
    {
        [MapProperty(Source = "Child", Target = nameof(ConfiguredUpdaterTarget.Child), Use = nameof(ApplyChild))]
        public static partial void ApplyDirect(ConfiguredUpdaterSource source, ConfiguredUpdaterTarget target);

        [MapProperty(Source = "Container.Child", Target = nameof(ConfiguredUpdaterTarget.Child), Use = nameof(ApplyChild))]
        public static partial ConfiguredUpdaterTarget ApplyPath(ConfiguredUpdaterPath source, ConfiguredUpdaterTarget target);

        private static ConfiguredUpdaterChild ApplyChild(ConfiguredUpdaterValue? source, ConfiguredUpdaterChild target)
        {
            target.Value = source?.Value ?? -1;
            return target;
        }

        private static ConfiguredUpdaterChild ApplyChild(ConfiguredUpdaterValue source, ConfiguredUpdaterChild target)
        {
            target.Value = 99;
            return target;
        }
    }

    public sealed class ConfiguredUpdaterSource
    {
        private readonly ConfiguredUpdaterValue? first;
        public ConfiguredUpdaterSource(ConfiguredUpdaterValue? first) { this.first = first; }
        public int Reads { get; private set; }
        public ConfiguredUpdaterValue? Child { get { Reads++; return Reads == 1 ? first : null; } }
    }

    public sealed class ConfiguredUpdaterPath
    {
        private readonly ConfiguredUpdaterSource? first;
        public ConfiguredUpdaterPath(ConfiguredUpdaterSource? first) { this.first = first; }
        public int Reads { get; private set; }
        public ConfiguredUpdaterSource? Container { get { Reads++; return Reads == 1 ? first : null; } }
    }

    public struct ConfiguredUpdaterValue { public int Value { get; set; } }
    public sealed class ConfiguredUpdaterChild { public int Value { get; set; } }
    public sealed class ConfiguredUpdaterTarget { public ConfiguredUpdaterChild Child { get; set; } = new ConfiguredUpdaterChild(); }
}
