using System;

namespace Mammoth.LiteMapper
{
    public sealed class LiteMapperCycleException : InvalidOperationException
    {
        public LiteMapperCycleException(
            Type sourceType,
            Type destinationType,
            string mappingMethod,
            string memberPath)
            : base(CreateMessage(sourceType, destinationType, mappingMethod, memberPath))
        {
            SourceType = sourceType;
            DestinationType = destinationType;
            MappingMethod = mappingMethod;
            MemberPath = memberPath;
        }

        public Type SourceType { get; }

        public Type DestinationType { get; }

        public string MappingMethod { get; }

        public string MemberPath { get; }

        private static string CreateMessage(
            Type sourceType,
            Type destinationType,
            string mappingMethod,
            string memberPath)
        {
            return "A mapping cycle was detected while mapping '" +
                sourceType +
                "' to '" +
                destinationType +
                "' in '" +
                mappingMethod +
                "' at '" +
                memberPath +
                "'.";
        }
    }
}
