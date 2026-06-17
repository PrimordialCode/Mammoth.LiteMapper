using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Runtime.Tests
{
    [TestClass]
    public sealed class Milestone2PublicApiTests
    {
        private static readonly string[] ExpectedPublicApi =
        {
            "Mammoth.LiteMapper.DefaultMappingAttribute",
            "Mammoth.LiteMapper.EnumMappingStrategy",
            "Mammoth.LiteMapper.EnumNumericConversion",
            "Mammoth.LiteMapper.IgnoreSourceAttribute",
            "Mammoth.LiteMapper.IgnoreTargetAttribute",
            "Mammoth.LiteMapper.LiteMapperAttribute",
            "Mammoth.LiteMapper.LiteMapperCycleException",
            "Mammoth.LiteMapper.LiteMapperDefaultsAttribute",
            "Mammoth.LiteMapper.MapPropertyAttribute",
            "Mammoth.LiteMapper.MappingConstructorAttribute",
            "Mammoth.LiteMapper.MappingConverterAttribute",
            "Mammoth.LiteMapper.MappingOptionsAttribute",
            "Mammoth.LiteMapper.NameMatching",
            "Mammoth.LiteMapper.NullCollectionStrategy",
            "Mammoth.LiteMapper.NullableMismatchPolicy",
            "Mammoth.LiteMapper.NumericConversion",
            "Mammoth.LiteMapper.OptionState",
            "Mammoth.LiteMapper.ReferenceHandling",
            "Mammoth.LiteMapper.UnmappedMemberPolicy",
            "Mammoth.LiteMapper.UnmatchedEnumValuePolicy",
            "Mammoth.LiteMapper.UseMapperAttribute",
            "Mammoth.LiteMapper.UseTargetDefaultAttribute",
        };

        [TestMethod]
        public void AbstractionsAssemblyExportsOnlySpecifiedPublicTypes()
        {
            var exported = typeof(LiteMapperAttribute).Assembly.GetExportedTypes()
                .Select(t => t.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(ExpectedPublicApi, exported);
            Assert.AreEqual(0, Assembly.Load("Mammoth.LiteMapper").GetExportedTypes().Length);
        }

        [TestMethod]
        public void ConfigurationEnumsUseSpecifiedValues()
        {
            AssertEnum<NameMatching>("Unspecified=0", "Exact=1", "ExactThenIgnoreCase=2", "IgnoreCase=3");
            AssertEnum<UnmappedMemberPolicy>("Unspecified=0", "Ignore=1", "Info=2", "Warning=3", "Error=4");
            AssertEnum<NullableMismatchPolicy>("Unspecified=0", "Error=1", "Throw=2");
            AssertEnum<NullCollectionStrategy>("Unspecified=0", "Error=1", "Preserve=2", "Empty=3");
            AssertEnum<NumericConversion>("Unspecified=0", "ImplicitOnly=1", "Checked=2", "Unchecked=3");
            AssertEnum<EnumMappingStrategy>("Unspecified=0", "ByName=1", "ByValue=2");
            AssertEnum<EnumNumericConversion>("Unspecified=0", "Checked=1", "Unchecked=2");
            AssertEnum<UnmatchedEnumValuePolicy>("Unspecified=0", "Error=1", "Throw=2", "ByValue=3");
            AssertEnum<ReferenceHandling>("Unspecified=0", "None=1", "ThrowOnCycle=2");
            AssertEnum<OptionState>("Unspecified=0", "Disabled=1", "Enabled=2");
        }

        [TestMethod]
        public void AttributeUsageAndMembersMatchSpecification()
        {
            AssertAttribute<LiteMapperAttribute>(AttributeTargets.Class, false);
            AssertSettableProperties<LiteMapperAttribute>(
                "AllowExplicitOperators:Boolean",
                "EnumMapping:EnumMappingStrategy",
                "EnumNumericConversion:EnumNumericConversion",
                "IgnoreNullSourceMembers:Boolean",
                "NameMatching:NameMatching",
                "NullCollections:NullCollectionStrategy",
                "NullableMismatch:NullableMismatchPolicy",
                "NumericConversion:NumericConversion",
                "ReferenceHandling:ReferenceHandling",
                "UnmappedSourceMembers:UnmappedMemberPolicy",
                "UnmappedTargetMembers:UnmappedMemberPolicy",
                "UnmatchedEnumValues:UnmatchedEnumValuePolicy");

            AssertAttribute<MappingOptionsAttribute>(AttributeTargets.Method, false);
            AssertSettableProperties<MappingOptionsAttribute>(
                "AllowExplicitOperators:OptionState",
                "EnumMapping:EnumMappingStrategy",
                "EnumNumericConversion:EnumNumericConversion",
                "IgnoreNullSourceMembers:OptionState",
                "NameMatching:NameMatching",
                "NullCollections:NullCollectionStrategy",
                "NullableMismatch:NullableMismatchPolicy",
                "NumericConversion:NumericConversion",
                "ReferenceHandling:ReferenceHandling",
                "UnmappedSourceMembers:UnmappedMemberPolicy",
                "UnmappedTargetMembers:UnmappedMemberPolicy",
                "UnmatchedEnumValues:UnmatchedEnumValuePolicy");

            AssertAttribute<LiteMapperDefaultsAttribute>(AttributeTargets.Assembly, false);
            AssertSettableProperties<LiteMapperDefaultsAttribute>(
                "NameMatching:NameMatching",
                "NullCollections:NullCollectionStrategy",
                "NullableMismatch:NullableMismatchPolicy",
                "UnmappedSourceMembers:UnmappedMemberPolicy",
                "UnmappedTargetMembers:UnmappedMemberPolicy");

            AssertAttribute<UseMapperAttribute>(AttributeTargets.Class | AttributeTargets.Assembly, true);
            AssertConstructor<UseMapperAttribute>(typeof(Type));
            AssertGetOnlyProperties<UseMapperAttribute>("MapperType:Type");

            AssertAttribute<DefaultMappingAttribute>(AttributeTargets.Method, false);
            AssertAttribute<MappingConverterAttribute>(AttributeTargets.Method, false);
            AssertAttribute<MappingConstructorAttribute>(AttributeTargets.Constructor, false);

            AssertAttribute<MapPropertyAttribute>(AttributeTargets.Method, true);
            AssertSettableProperties<MapPropertyAttribute>(
                "ConverterType:Type",
                "Source:String",
                "Target:String",
                "Use:String");

            AssertStringMemberAttribute<IgnoreTargetAttribute>();
            AssertStringMemberAttribute<IgnoreSourceAttribute>();
            AssertStringMemberAttribute<UseTargetDefaultAttribute>();
        }

        [TestMethod]
        public void CycleExceptionExposesOnlySpecifiedState()
        {
            var exception = new LiteMapperCycleException(
                typeof(SourceModel),
                typeof(TargetModel),
                "Map",
                "Child.Parent");

            Assert.IsInstanceOfType(exception, typeof(InvalidOperationException));
            Assert.AreEqual(typeof(SourceModel), exception.SourceType);
            Assert.AreEqual(typeof(TargetModel), exception.DestinationType);
            Assert.AreEqual("Map", exception.MappingMethod);
            Assert.AreEqual("Child.Parent", exception.MemberPath);
            StringAssert.Contains(exception.Message, typeof(SourceModel).ToString());
            StringAssert.Contains(exception.Message, typeof(TargetModel).ToString());
            StringAssert.Contains(exception.Message, "Map");
            StringAssert.Contains(exception.Message, "Child.Parent");
            Assert.IsFalse(typeof(LiteMapperCycleException).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.PropertyType == typeof(object)));
        }

        [TestMethod]
        public void PublicApiBaselinesExist()
        {
            Assert.IsTrue(File.Exists(Repository.Path("src/Mammoth.LiteMapper.Abstractions/PublicApi.Shipped.txt")));
            Assert.IsTrue(File.Exists(Repository.Path("src/Mammoth.LiteMapper.Abstractions/PublicApi.Unshipped.txt")));
        }

        private static void AssertAttribute<T>(AttributeTargets targets, bool allowMultiple)
            where T : Attribute
        {
            var type = typeof(T);
            Assert.IsTrue(type.IsSealed, type.FullName);
            Assert.AreEqual("Mammoth.LiteMapper", type.Namespace);

            var usage = type.GetCustomAttribute<AttributeUsageAttribute>();
            Assert.IsNotNull(usage, type.FullName);
            Assert.AreEqual(targets, usage.ValidOn, type.FullName);
            Assert.AreEqual(allowMultiple, usage.AllowMultiple, type.FullName);
            Assert.IsFalse(usage.Inherited, type.FullName);
        }

        private static void AssertStringMemberAttribute<T>()
            where T : Attribute
        {
            AssertAttribute<T>(AttributeTargets.Method, true);
            AssertConstructor<T>(typeof(string));
            AssertGetOnlyProperties<T>("Member:String");
        }

        private static void AssertConstructor<T>(params Type[] parameters)
        {
            Assert.IsNotNull(typeof(T).GetConstructor(parameters), typeof(T).FullName);
        }

        private static void AssertSettableProperties<T>(params string[] expected)
        {
            var actual = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => p.Name + ":" + p.PropertyType.Name)
                .ToArray();

            CollectionAssert.AreEqual(expected, actual, typeof(T).FullName);
            Assert.IsTrue(typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .All(p => p.SetMethod != null), typeof(T).FullName);
        }

        private static void AssertGetOnlyProperties<T>(params string[] expected)
        {
            var actual = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => p.Name + ":" + p.PropertyType.Name)
                .ToArray();

            CollectionAssert.AreEqual(expected, actual, typeof(T).FullName);
            Assert.IsTrue(typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .All(p => p.SetMethod == null), typeof(T).FullName);
        }

        private static void AssertEnum<T>(params string[] expected)
            where T : Enum
        {
            var actual = Enum.GetNames(typeof(T))
                .Select(name => name + "=" + Convert.ToInt32(Enum.Parse(typeof(T), name)))
                .ToArray();

            CollectionAssert.AreEqual(expected, actual, typeof(T).FullName);
        }

        private sealed class SourceModel
        {
        }

        private sealed class TargetModel
        {
        }
    }
}
