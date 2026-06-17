using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mammoth.LiteMapper.Generator
{
    [Generator]
    public sealed class LiteMapperGenerator : IIncrementalGenerator
    {
        private const string LiteMapperAttributeName = "Mammoth.LiteMapper.LiteMapperAttribute";
        private const string LiteMapperDefaultsAttributeName = "Mammoth.LiteMapper.LiteMapperDefaultsAttribute";
        private const string IgnoreSourceAttributeName = "Mammoth.LiteMapper.IgnoreSourceAttribute";
        private const string IgnoreTargetAttributeName = "Mammoth.LiteMapper.IgnoreTargetAttribute";
        private const string MapPropertyAttributeName = "Mammoth.LiteMapper.MapPropertyAttribute";
        private const string MappingConverterAttributeName = "Mammoth.LiteMapper.MappingConverterAttribute";
        private const string MappingConstructorAttributeName = "Mammoth.LiteMapper.MappingConstructorAttribute";
        private const string MappingOptionsAttributeName = "Mammoth.LiteMapper.MappingOptionsAttribute";
        private const string UseMapperAttributeName = "Mammoth.LiteMapper.UseMapperAttribute";
        private const string UseTargetDefaultAttributeName = "Mammoth.LiteMapper.UseTargetDefaultAttribute";
        private const string NameMatchingExact = "Exact";
        private const string NameMatchingIgnoreCase = "IgnoreCase";
        private const string UnmappedMemberPolicyIgnore = "Ignore";
        private const string UnmappedMemberPolicyWarning = "Warning";
        private const string UnmappedMemberPolicyError = "Error";
        private const string NullableMismatchPolicyError = "Error";
        private const string NullableMismatchPolicyThrow = "Throw";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var language = context.ParseOptionsProvider.Select(static (options, _) =>
            {
                var csharp = (CSharpParseOptions)options;
                return csharp.LanguageVersion;
            });

            var assemblyConfiguration = context.CompilationProvider.Select(static (compilation, _) => ValidateAssemblyConfiguration(compilation));

            var mappers = context.SyntaxProvider
                .CreateSyntaxProvider(static (node, _) => IsTypeWithAttributes(node), static (ctx, _) => CreateMapperModel(ctx))
                .Where(static model => model != null)
                .Select(static (model, _) => model!);
            var sortedMappers = mappers.Collect().Select(static (items, _) => items.OrderBy(static item => item.DisplayName, StringComparer.Ordinal).ToImmutableArray());

            context.RegisterSourceOutput(language, static (production, version) =>
            {
                if (version < LanguageVersion.CSharp9)
                {
                    production.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedLanguageVersion, Location.None, version.ToDisplayString()));
                }
            });

            context.RegisterSourceOutput(assemblyConfiguration, static (production, diagnostics) =>
            {
                foreach (var diagnostic in diagnostics)
                {
                    production.ReportDiagnostic(diagnostic);
                }
            });

            context.RegisterSourceOutput(sortedMappers, static (production, mapperModels) =>
            {
                foreach (var mapper in mapperModels)
                {
                    try
                    {
                        foreach (var diagnostic in mapper.Diagnostics)
                        {
                            production.ReportDiagnostic(diagnostic);
                        }

                        if (!mapper.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
                        {
                            production.AddSource(mapper.HintName, SourceText.From(mapper.Source, Encoding.UTF8));
                        }
                    }
                    catch (Exception)
                    {
                        production.ReportDiagnostic(Diagnostic.Create(Diagnostics.InternalGeneratorFailure, mapper.Location, mapper.DisplayName));
                    }
                }
            });
        }

        private static bool IsTypeWithAttributes(SyntaxNode node)
        {
            return node is TypeDeclarationSyntax type && type.AttributeLists.Count > 0;
        }

        private static MapperModel? CreateMapperModel(GeneratorSyntaxContext context)
        {
            var typeSyntax = (TypeDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(typeSyntax) as INamedTypeSymbol;
            if (symbol == null || !HasAttribute(symbol, LiteMapperAttributeName))
            {
                return null;
            }

            var diagnostics = new List<Diagnostic>();
            ValidateMapper(symbol, typeSyntax, diagnostics);

            var mappings = ImmutableArray.CreateBuilder<MappingModel>();
            foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>().Where(static m => m.PartialDefinitionPart == null && IsPartialDeclaration(m)).OrderBy(static m => m.Name, StringComparer.Ordinal))
            {
                ValidateMappingMethod(member, diagnostics);
                if (IsFlatMappingCandidate(member))
                {
                    mappings.Add(CreateMappingModel(member, context.SemanticModel.Compilation, diagnostics));
                }
            }

            var source = RenderDeclaration(symbol, mappings.ToImmutable());
            return new MapperModel(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), typeSyntax.Identifier.GetLocation(), CreateHintName(symbol), source, diagnostics.ToImmutableArray());
        }

        private static bool IsPartialDeclaration(IMethodSymbol method)
        {
            foreach (var syntaxReference in method.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is MethodDeclarationSyntax methodSyntax &&
                    methodSyntax.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                    methodSyntax.Body == null &&
                    methodSyntax.ExpressionBody == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateMapper(INamedTypeSymbol symbol, TypeDeclarationSyntax syntax, List<Diagnostic> diagnostics)
        {
            var liteMapperAttributes = symbol.GetAttributes().Where(static a => IsAttribute(a, LiteMapperAttributeName)).ToArray();
            if (liteMapperAttributes.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateConfiguration, syntax.Identifier.GetLocation(), "LiteMapperAttribute"));
            }
            foreach (var attribute in liteMapperAttributes)
            {
                ValidateEnumArguments(attribute, Diagnostics.InvalidMapperConfiguration, diagnostics);
            }
            foreach (var attribute in symbol.GetAttributes().Where(static a => IsAttribute(a, UseMapperAttributeName)))
            {
                ValidateUseMapper(attribute, diagnostics);
            }

            if (!(syntax is ClassDeclarationSyntax) || syntax.Modifiers.Any(SyntaxKind.StaticKeyword) && syntax.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMapperType, syntax.Identifier.GetLocation(), symbol.Name));
            }

            if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.MapperMustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
            }

            if (symbol.TypeParameters.Length != 0 || ContainingTypes(symbol).Any(static t => t.TypeParameters.Length != 0))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.GenericMapperNotSupported, syntax.Identifier.GetLocation(), symbol.Name));
            }

            if (!symbol.IsStatic && symbol.IsAbstract)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AbstractMapperNotSupported, syntax.Identifier.GetLocation(), symbol.Name));
            }

            foreach (var containingType in ContainingTypes(symbol))
            {
                var declaration = containingType.DeclaringSyntaxReferences.Select(static r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (declaration != null && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.MapperMustBePartial, declaration.Identifier.GetLocation(), containingType.Name));
                }
            }
        }

        private static bool IsFlatMappingCandidate(IMethodSymbol method)
        {
            return !method.IsAsync &&
                !IsTaskLike(method.ReturnType) &&
                !method.IsGenericMethod &&
                method.Parameters.Length == 1 &&
                !method.ReturnsVoid &&
                method.Parameters[0].RefKind == RefKind.None &&
                (!method.ContainingType.IsStatic || method.IsStatic);
        }

        private static MappingModel CreateMappingModel(IMethodSymbol method, Compilation compilation, List<Diagnostic> diagnostics)
        {
            var sourceType = method.Parameters[0].Type;
            var targetType = method.ReturnType;
            var sourceNullable = IsMaybeNull(method.Parameters[0]);
            var returnNullable = IsMaybeNull(method.ReturnType);
            var options = EffectiveOptions(method);
            var location = method.Locations.FirstOrDefault();
            var assignments = ImmutableArray.CreateBuilder<AssignmentModel>();
            var preconditions = ImmutableArray.CreateBuilder<PreconditionModel>();
            var helpers = ImmutableArray.CreateBuilder<MappingModel>();
            var helperNames = new HashSet<string>(StringComparer.Ordinal);
            var usedSources = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var sourceMembers = GetSourceMembers(sourceType, diagnostics).ToArray();
            var explicitConfigurations = ParseExplicitConfigurations(method, sourceType, targetType, sourceMembers, diagnostics);
            var ignoredTargets = ParseMemberNames(method, IgnoreTargetAttributeName, targetType, diagnostics);
            var ignoredSources = ParseMemberNames(method, IgnoreSourceAttributeName, sourceType, diagnostics);
            var targetDefaults = ParseMemberNames(method, UseTargetDefaultAttributeName, targetType, diagnostics);
            var externalTypes = GetRegisteredMapperTypes(method.ContainingType, compilation).ToArray();

            if (sourceNullable && !returnNullable && options.NullableMismatch == NullableMismatchPolicyError)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, location, method.Parameters[0].Name));
            }

            var construction = targetType is INamedTypeSymbol namedTarget ? SelectConstruction(namedTarget, method.ContainingType, sourceMembers, options.NameMatching, compilation, location, diagnostics) : null;
            if (construction == null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ConstructorNotFound, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            foreach (var argument in construction == null ? Enumerable.Empty<ConstructorArgumentModel>() : construction.Arguments)
            {
                usedSources.Add(argument.SourceMember);
            }

            foreach (var targetMember in GetTargetMembers(targetType, diagnostics, includeConstructorOnly: true))
            {
                if (construction != null && construction.BoundTargetMembers.Any(m => SymbolEqualityComparer.Default.Equals(m, targetMember)))
                {
                    continue;
                }

                if (ignoredTargets.Contains(targetMember.Name) || targetDefaults.Contains(targetMember.Name))
                {
                    if (targetDefaults.Contains(targetMember.Name) && !HasWritableDefault(targetMember) && (construction == null || !construction.BoundTargetMembers.Any(m => SymbolEqualityComparer.Default.Equals(m, targetMember))))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    }

                    continue;
                }

                var explicitConfiguration = explicitConfigurations.FirstOrDefault(c => c.TargetName == targetMember.Name);
                var selected = explicitConfiguration == null ? null : explicitConfiguration.SelectedSource;
                var match = selected == null ? MatchSource(targetMember, sourceMembers, options.NameMatching) : new MatchResult(selected.SourceMember, false);
                if (match.Ambiguous)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMemberMatch, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    continue;
                }

                if (IsRequired(targetMember) && !CanAssignInInitializer(targetMember) && (construction == null || !construction.SetsRequiredMembers))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    continue;
                }

                if (match.Member == null)
                {
                    if (IsRequired(targetMember) && (construction == null || !construction.SetsRequiredMembers))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                        continue;
                    }

                    if (IsRequired(targetMember) && construction != null && construction.SetsRequiredMembers)
                    {
                        continue;
                    }

                    ReportUnmappedTarget(targetMember, options.UnmappedTargetMembers, diagnostics);
                    continue;
                }

                var conversion = ResolveConversion(method, explicitConfiguration, match.Member, targetMember, compilation, externalTypes, diagnostics, helpers, helperNames, options);
                if (conversion == null)
                {
                    continue;
                }

                if (conversion.PotentiallyNull && TargetIsNonNullable(targetMember))
                {
                    if (options.NullableMismatch == NullableMismatchPolicyError)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                        continue;
                    }

                    if (conversion.NullCheckExpression != null)
                    {
                        preconditions.Add(new PreconditionModel(conversion.NullCheckExpression, "Source member path '" + conversion.MemberPath + "' was null."));
                    }

                    conversion = new ConversionModel(
                        conversion.Expression + " ?? throw new global::System.InvalidOperationException(\"Source member '" + conversion.MemberPath + "' was null.\")",
                        conversion.SourceMember,
                        potentiallyNull: false,
                        conversion.MemberPath,
                        nullCheckExpression: null);
                }

                if (conversion.SourceMember != null)
                {
                    usedSources.Add(conversion.SourceMember);
                }

                assignments.Add(new AssignmentModel(targetMember.Name, conversion.Expression));
            }

            if (options.UnmappedSourceMembers != UnmappedMemberPolicyIgnore)
            {
                foreach (var sourceMember in sourceMembers.Where(m => !usedSources.Contains(m) && !ignoredSources.Contains(m.Name)).OrderBy(static m => m.Name, StringComparer.Ordinal))
                {
                    ReportUnmappedSource(sourceMember, options.UnmappedSourceMembers, diagnostics);
                }
            }

            return new MappingModel(method, sourceNullable, returnNullable, options.NullableMismatch, construction, preconditions.ToImmutable(), assignments.ToImmutable(), helpers.ToImmutable(), null, null, null);
        }

        private static ImmutableArray<ExplicitMemberConfiguration> ParseExplicitConfigurations(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType, ISymbol[] sourceMembers, ICollection<Diagnostic> diagnostics)
        {
            var builder = ImmutableArray.CreateBuilder<ExplicitMemberConfiguration>();
            var seenTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in method.GetAttributes().Where(static a => IsAttribute(a, MapPropertyAttributeName)))
            {
                var targetName = ReadStringNamedArgument(attribute, "Target");
                var sourcePath = ReadStringNamedArgument(attribute, "Source");
                var use = ReadStringNamedArgument(attribute, "Use");
                var converterType = ReadTypeNamedArgument(attribute, "ConverterType");
                var location = attribute.ApplicationSyntaxReference == null ? method.Locations.FirstOrDefault() : attribute.ApplicationSyntaxReference.GetSyntax().GetLocation();
                if (string.IsNullOrWhiteSpace(targetName) || targetName!.IndexOf('.') >= 0 || !HasDirectMember(targetType, targetName))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, location, targetName ?? "Target"));
                    continue;
                }

                if (!seenTargets.Add(targetName!))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateConfiguration, location, targetName));
                    continue;
                }

                SourcePathModel? selectedSource = null;
                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    selectedSource = ResolveSourcePath(sourceType, sourceMembers, sourcePath!, diagnostics, location);
                    if (selectedSource == null)
                    {
                        continue;
                    }
                }
                else if (string.IsNullOrWhiteSpace(use))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, location, targetName));
                    continue;
                }

                builder.Add(new ExplicitMemberConfiguration(targetName!, selectedSource, use, converterType));
            }

            return builder.ToImmutable();
        }

        private static HashSet<string> ParseMemberNames(IMethodSymbol method, string attributeName, ITypeSymbol declaringType, ICollection<Diagnostic> diagnostics)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in method.GetAttributes().Where(a => IsAttribute(a, attributeName)))
            {
                var memberName = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
                var location = attribute.ApplicationSyntaxReference == null ? method.Locations.FirstOrDefault() : attribute.ApplicationSyntaxReference.GetSyntax().GetLocation();
                if (string.IsNullOrWhiteSpace(memberName) || memberName!.IndexOf('.') >= 0 || !HasDirectMember(declaringType, memberName))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, location, memberName ?? attributeName));
                    continue;
                }

                if (!names.Add(memberName))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateConfiguration, location, memberName));
                }
            }

            return names;
        }

        private static ConversionModel? ResolveConversion(IMethodSymbol mappingMethod, ExplicitMemberConfiguration? explicitConfiguration, ISymbol? sourceMember, ISymbol targetMember, Compilation compilation, INamedTypeSymbol[] externalTypes, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options)
        {
            var parameterName = mappingMethod.Parameters[0].Name;
            var sourcePath = explicitConfiguration == null ? null : explicitConfiguration.SelectedSource;
            var expression = sourcePath == null ? parameterName + "." + sourceMember!.Name : parameterName + "." + sourcePath.Expression;
            var sourceType = sourcePath == null ? GetMemberType(sourceMember!) : sourcePath.Type;
            var targetType = GetMemberType(targetMember);
            var location = targetMember.Locations.FirstOrDefault() ?? mappingMethod.Locations.FirstOrDefault();

            if (explicitConfiguration != null && !string.IsNullOrWhiteSpace(explicitConfiguration.Use))
            {
                var explicitSourceType = sourcePath == null ? mappingMethod.Parameters[0].Type : sourceType;
                var explicitExpression = sourcePath == null ? parameterName : expression;
                return ResolveNamedConverter(mappingMethod, explicitConfiguration.ConverterType, externalTypes, explicitConfiguration.Use!, explicitSourceType, targetType, explicitExpression, sourcePath == null ? null : sourcePath.SourceMember, compilation, diagnostics, location);
            }

            if (sourcePath != null && compilation.ClassifyConversion(sourceType, targetType).IsImplicit)
            {
                return new ConversionModel(expression, sourcePath.SourceMember, SourcePathMayBeNull(sourcePath), sourcePath.Expression, BuildNullCheck(parameterName, sourcePath));
            }

            var localMarked = ResolveConverterSet(mappingMethod.ContainingType, static m => HasAttribute(m, MappingConverterAttributeName), sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location);
            if (localMarked != null)
            {
                return localMarked;
            }

            var named = ResolveConverterSet(mappingMethod.ContainingType, m => m.Name == "Map" + targetMember.Name, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location);
            if (named != null)
            {
                return named;
            }

            foreach (var externalType in externalTypes)
            {
                var externalConverter = ResolveConverterSet(externalType, static m => HasAttribute(m, MappingConverterAttributeName), sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location, qualifyWithType: true);
                if (externalConverter != null)
                {
                    return externalConverter;
                }
            }

            foreach (var externalType in externalTypes)
            {
                var externalMapping = ResolveConverterSet(externalType, static m => !HasAttribute(m, MappingConverterAttributeName), sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location, qualifyWithType: true);
                if (externalMapping != null)
                {
                    return externalMapping;
                }
            }

            if (compilation.ClassifyConversion(sourceType, targetType).IsImplicit)
            {
                return new ConversionModel(expression, sourceMember, sourceMember != null && SourceMayBeNull(sourceMember), sourceMember == null ? string.Empty : sourceMember.Name, nullCheckExpression: null);
            }

            var visibleMapping = ResolveVisibleMapping(mappingMethod.ContainingType, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location);
            if (visibleMapping != null)
            {
                return visibleMapping;
            }

            var nested = ResolveNestedMapping(mappingMethod, sourceType, targetType, expression, sourceMember, targetMember, compilation, diagnostics, helpers, helperNames, options);
            if (nested != null)
            {
                return nested;
            }

            diagnostics.Add(Diagnostic.Create(Diagnostics.ConversionNotFound, location, sourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return null;
        }

        private static ConversionModel? ResolveVisibleMapping(INamedTypeSymbol mapperType, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location)
        {
            var candidates = mapperType.GetMembers().OfType<IMethodSymbol>()
                .Where(m => !m.IsImplicitlyDeclared && !HasAttribute(m, MappingConverterAttributeName) && IsUsableConverter(m, mapperType, sourceType, targetType, compilation))
                .OrderBy(static m => m.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }

            if (candidates.Length != 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMapping, location, targetName));
                return null;
            }

            return new ConversionModel(candidates[0].Name + "(" + expression + ")", sourceMember, potentiallyNull: false, targetName, nullCheckExpression: null);
        }

        private static ConversionModel? ResolveNestedMapping(IMethodSymbol mappingMethod, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, ISymbol targetMember, Compilation compilation, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options)
        {
            var location = targetMember.Locations.FirstOrDefault() ?? mappingMethod.Locations.FirstOrDefault();
            if (sourceType.SpecialType == SpecialType.System_Object)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.RuntimeObjectDispatchNotSupported, location, targetMember.Name));
                return null;
            }

            if (!(sourceType is INamedTypeSymbol namedSource) || !(targetType is INamedTypeSymbol namedTarget))
            {
                return null;
            }

            if (namedTarget.SpecialType == SpecialType.System_Object || namedTarget.TypeKind == TypeKind.Interface || namedTarget.IsAbstract)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AbstractDestinationNotSupported, location, namedTarget.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            if (!IsStructuralObjectType(namedSource) || !IsStructuralObjectType(namedTarget))
            {
                return null;
            }

            var helperName = "MapNested_" + SanitizeIdentifier(namedSource.Name) + "_To_" + SanitizeIdentifier(namedTarget.Name);
            if (helperNames.Add(helperName))
            {
                var helper = CreateNestedMappingModel(mappingMethod, namedSource, namedTarget, helperName, compilation, diagnostics, options);
                if (helper == null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.StructuralNestedMappingFailed, location, namedSource.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), namedTarget.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                    return null;
                }

                helpers.Add(helper);
            }

            var potentiallyNull = sourceMember != null && SourceMayBeNull(sourceMember);
            if (potentiallyNull && !TargetIsNonNullable(targetMember))
            {
                return new ConversionModel(expression + " == null ? null : " + helperName + "(" + expression + ")", sourceMember, potentiallyNull: false, targetMember.Name, nullCheckExpression: null);
            }

            return new ConversionModel(helperName + "(" + expression + ")", sourceMember, potentiallyNull, targetMember.Name, nullCheckExpression: null);
        }

        private static MappingModel? CreateNestedMappingModel(IMethodSymbol rootMethod, INamedTypeSymbol sourceType, INamedTypeSymbol targetType, string helperName, Compilation compilation, ICollection<Diagnostic> diagnostics, EffectiveMappingOptions options)
        {
            var sourceMembers = GetSourceMembers(sourceType, diagnostics).ToArray();
            var construction = SelectConstruction(targetType, rootMethod.ContainingType, sourceMembers, options.NameMatching, compilation, rootMethod.Locations.FirstOrDefault(), diagnostics);
            if (construction == null)
            {
                return null;
            }

            var assignments = ImmutableArray.CreateBuilder<AssignmentModel>();
            var preconditions = ImmutableArray.CreateBuilder<PreconditionModel>();
            var helpers = ImmutableArray.CreateBuilder<MappingModel>();
            var helperNames = new HashSet<string>(StringComparer.Ordinal);
            var externalTypes = GetRegisteredMapperTypes(rootMethod.ContainingType, compilation).ToArray();

            foreach (var targetMember in GetTargetMembers(targetType, diagnostics, includeConstructorOnly: true))
            {
                if (construction.BoundTargetMembers.Any(m => SymbolEqualityComparer.Default.Equals(m, targetMember)))
                {
                    continue;
                }

                var match = MatchSource(targetMember, sourceMembers, options.NameMatching);
                if (match.Ambiguous || match.Member == null)
                {
                    return null;
                }

                var conversion = ResolveConversion(rootMethod, null, match.Member, targetMember, compilation, externalTypes, diagnostics, helpers, helperNames, options);
                if (conversion == null)
                {
                    return null;
                }

                if (conversion.PotentiallyNull && TargetIsNonNullable(targetMember))
                {
                    if (options.NullableMismatch == NullableMismatchPolicyError)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, targetMember.Locations.FirstOrDefault() ?? rootMethod.Locations.FirstOrDefault(), targetMember.Name));
                        return null;
                    }

                    conversion = new ConversionModel(conversion.Expression + " ?? throw new global::System.InvalidOperationException(\"Source member '" + conversion.MemberPath + "' was null.\")", conversion.SourceMember, potentiallyNull: false, conversion.MemberPath, nullCheckExpression: null);
                }

                assignments.Add(new AssignmentModel(targetMember.Name, conversion.Expression));
            }

            return new MappingModel(rootMethod, sourceNullable: false, returnNullable: false, options.NullableMismatch, construction, preconditions.ToImmutable(), assignments.ToImmutable(), helpers.ToImmutable(), helperName, sourceType, targetType);
        }

        private static string SanitizeIdentifier(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            return builder.ToString();
        }

        private static bool IsStructuralObjectType(INamedTypeSymbol type)
        {
            return type.SpecialType == SpecialType.None &&
                type.TypeKind != TypeKind.Enum &&
                (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct);
        }

        private static ConversionModel? ResolveNamedConverter(IMethodSymbol mappingMethod, INamedTypeSymbol? converterType, INamedTypeSymbol[] externalTypes, string name, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, Location? location)
        {
            if (converterType != null)
            {
                return ResolveConverterSet(converterType, m => m.Name == name, sourceType, targetType, expression, sourceMember, compilation, diagnostics, name, location, qualifyWithType: true, invalidWhenNone: true);
            }

            var local = ResolveConverterSet(mappingMethod.ContainingType, m => m.Name == name, sourceType, targetType, expression, sourceMember, compilation, diagnostics, name, location, invalidWhenNone: true);
            if (local != null)
            {
                return local;
            }

            foreach (var externalType in externalTypes)
            {
                var external = ResolveConverterSet(externalType, m => m.Name == name, sourceType, targetType, expression, sourceMember, compilation, diagnostics, name, location, qualifyWithType: true);
                if (external != null)
                {
                    return external;
                }
            }

            diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidConverter, location, name));
            return null;
        }

        private static ConversionModel? ResolveConverterSet(INamedTypeSymbol type, Func<IMethodSymbol, bool> predicate, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location, bool qualifyWithType = false, bool invalidWhenNone = false)
        {
            var candidates = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => !m.IsImplicitlyDeclared && predicate(m) && IsUsableConverter(m, type, sourceType, targetType, compilation))
                .Select(m => new { Method = m, Rank = compilation.ClassifyConversion(sourceType, m.Parameters[0].Type).IsIdentity ? 0 : 1 })
                .OrderBy(static c => c.Rank)
                .ThenBy(static c => c.Method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                if (invalidWhenNone)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidConverter, location, targetName));
                }

                return null;
            }

            var bestRank = candidates[0].Rank;
            var best = candidates.Where(c => c.Rank == bestRank).ToArray();
            if (best.Length != 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousConverter, location, targetName));
                return null;
            }

            var method = best[0].Method;
            var receiver = qualifyWithType ? type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "." : string.Empty;
            return new ConversionModel(receiver + method.Name + "(" + expression + ")", sourceMember, potentiallyNull: false, targetName, nullCheckExpression: null);
        }

        private static bool IsUsableConverter(IMethodSymbol method, INamedTypeSymbol lookupType, ITypeSymbol sourceType, ITypeSymbol targetType, Compilation compilation)
        {
            if (method.IsAsync || method.IsGenericMethod || method.Parameters.Length != 1 || method.ReturnsVoid || method.Parameters[0].RefKind != RefKind.None)
            {
                return false;
            }

            if (lookupType.IsStatic && !method.IsStatic)
            {
                return false;
            }

            return compilation.ClassifyConversion(sourceType, method.Parameters[0].Type).IsImplicit &&
                compilation.ClassifyConversion(method.ReturnType, targetType).IsImplicit;
        }

        private static INamedTypeSymbol[] GetRegisteredMapperTypes(INamedTypeSymbol mapperType, Compilation compilation)
        {
            return mapperType.GetAttributes()
                .Concat(compilation.Assembly.GetAttributes())
                .Where(static a => IsAttribute(a, UseMapperAttributeName))
                .Select(static a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as INamedTypeSymbol : null)
                .Where(static t => t != null && t.IsStatic)
                .Cast<INamedTypeSymbol>()
                .GroupBy(static t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .Select(static g => g.First())
                .OrderBy(static t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToArray();
        }

        private static SourcePathModel? ResolveSourcePath(ITypeSymbol sourceType, ISymbol[] rootMembers, string sourcePath, ICollection<Diagnostic> diagnostics, Location? location)
        {
            if (sourcePath.IndexOf('(') >= 0 || sourcePath.IndexOf('[') >= 0)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, location, sourcePath));
                return null;
            }

            var segments = sourcePath.Split('.');
            if (segments.Length == 0 || segments.Any(static s => string.IsNullOrWhiteSpace(s)))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, location, sourcePath));
                return null;
            }

            ISymbol? first = rootMembers.FirstOrDefault(m => m.Name == segments[0]);
            if (first == null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, location, segments[0]));
                return null;
            }

            var nullChecks = ImmutableArray.CreateBuilder<string>();
            if (SourceMayBeNull(first))
            {
                nullChecks.Add(segments[0]);
            }

            ITypeSymbol currentType = GetMemberType(first);
            var currentExpression = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var member = GetReadableDirectMembers(currentType).FirstOrDefault(m => m.Name == segments[i]);
                if (member == null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMemberConfiguration, location, segments[i]));
                    return null;
                }

                currentType = GetMemberType(member);
                currentExpression += "." + segments[i];
                if (IsMaybeNull(currentType))
                {
                    nullChecks.Add(currentExpression);
                }
            }

            return new SourcePathModel(sourcePath, first, currentType, nullChecks.ToImmutable());
        }

        private static bool SourcePathMayBeNull(SourcePathModel sourcePath)
        {
            return sourcePath.NullCheckExpressions.Length != 0;
        }

        private static string? BuildNullCheck(string parameterName, SourcePathModel sourcePath)
        {
            if (sourcePath.NullCheckExpressions.Length == 0)
            {
                return null;
            }

            return string.Join(" || ", sourcePath.NullCheckExpressions.Select(p => parameterName + "." + p + " == null"));
        }

        private static IEnumerable<ISymbol> GetReadableDirectMembers(ITypeSymbol type)
        {
            return type.GetMembers()
                .Where(static m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public)
                .Where(static m => m is IFieldSymbol field && !field.IsConst || m is IPropertySymbol property && property.Parameters.Length == 0 && property.GetMethod != null && property.GetMethod.DeclaredAccessibility == Accessibility.Public);
        }

        private static bool HasDirectMember(ITypeSymbol type, string memberName)
        {
            return type.GetMembers().Any(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Name == memberName && (m is IFieldSymbol field && !field.IsConst || m is IPropertySymbol property && property.Parameters.Length == 0));
        }

        private static bool HasWritableDefault(ISymbol member)
        {
            foreach (var syntaxReference in member.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax();
                if (syntax is PropertyDeclarationSyntax property && property.Initializer != null)
                {
                    return true;
                }

                if (syntax is VariableDeclaratorSyntax variable && variable.Initializer != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static string? ReadStringNamedArgument(AttributeData attribute, string name)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == name)
                {
                    return argument.Value.Value as string;
                }
            }

            return null;
        }

        private static INamedTypeSymbol? ReadTypeNamedArgument(AttributeData attribute, string name)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == name)
                {
                    return argument.Value.Value as INamedTypeSymbol;
                }
            }

            return null;
        }

        private static EffectiveMappingOptions EffectiveOptions(IMethodSymbol method)
        {
            var mapper = method.ContainingType.GetAttributes().FirstOrDefault(static a => IsAttribute(a, LiteMapperAttributeName));
            var mapping = method.GetAttributes().FirstOrDefault(static a => IsAttribute(a, MappingOptionsAttributeName));
            var assemblyDefaults = method.ContainingAssembly.GetAttributes().FirstOrDefault(static a => IsAttribute(a, LiteMapperDefaultsAttributeName));
            return new EffectiveMappingOptions(
                ReadEnumOption(mapping, "NameMatching") ?? ReadEnumOption(mapper, "NameMatching") ?? ReadEnumOption(assemblyDefaults, "NameMatching") ?? "ExactThenIgnoreCase",
                ReadEnumOption(mapping, "UnmappedTargetMembers") ?? ReadEnumOption(mapper, "UnmappedTargetMembers") ?? ReadEnumOption(assemblyDefaults, "UnmappedTargetMembers") ?? UnmappedMemberPolicyWarning,
                ReadEnumOption(mapping, "UnmappedSourceMembers") ?? ReadEnumOption(mapper, "UnmappedSourceMembers") ?? ReadEnumOption(assemblyDefaults, "UnmappedSourceMembers") ?? UnmappedMemberPolicyIgnore,
                ReadEnumOption(mapping, "NullableMismatch") ?? ReadEnumOption(mapper, "NullableMismatch") ?? ReadEnumOption(assemblyDefaults, "NullableMismatch") ?? NullableMismatchPolicyError);
        }

        private static string? ReadEnumOption(AttributeData? attribute, string name)
        {
            if (attribute == null)
            {
                return null;
            }

            foreach (var pair in attribute.NamedArguments)
            {
                if (pair.Key == name && pair.Value.Value != null && pair.Value.Value is int raw && raw != 0)
                {
                    if (name == "NameMatching")
                    {
                        return raw == 1 ? NameMatchingExact : raw == 3 ? NameMatchingIgnoreCase : "ExactThenIgnoreCase";
                    }

                    if (name == "NullableMismatch")
                    {
                        return raw == 2 ? NullableMismatchPolicyThrow : NullableMismatchPolicyError;
                    }

                    return raw == 1 ? UnmappedMemberPolicyIgnore : raw == 3 ? UnmappedMemberPolicyWarning : raw == 4 ? UnmappedMemberPolicyError : null;
                }
            }

            return null;
        }

        private static IEnumerable<ISymbol> GetSourceMembers(ITypeSymbol type, ICollection<Diagnostic> diagnostics)
        {
            return GetVisibleMembers(type, diagnostics, static m => m is IFieldSymbol field && !field.IsConst || m is IPropertySymbol property && property.Parameters.Length == 0 && property.GetMethod != null && property.GetMethod.DeclaredAccessibility == Accessibility.Public);
        }

        private static IEnumerable<ISymbol> GetTargetMembers(ITypeSymbol type, ICollection<Diagnostic> diagnostics, bool includeConstructorOnly = false)
        {
            return GetVisibleMembers(type, diagnostics, m =>
                m is IFieldSymbol field && !field.IsConst && (includeConstructorOnly || !field.IsReadOnly) ||
                m is IPropertySymbol property && property.Parameters.Length == 0 && (includeConstructorOnly || property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public));
        }

        private static IEnumerable<ISymbol> GetVisibleMembers(ITypeSymbol type, ICollection<Diagnostic> diagnostics, Func<ISymbol, bool> predicate)
        {
            var selected = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
            for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers()
                    .Where(static m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public)
                    .Where(predicate)
                    .OrderBy(static m => m is IPropertySymbol ? 0 : 1)
                    .ThenBy(static m => m.Name, StringComparer.Ordinal))
                {
                    if (selected.ContainsKey(member.Name))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.HiddenMemberSelected, selected[member.Name].Locations.FirstOrDefault(), selected[member.Name].Name));
                        continue;
                    }

                    selected.Add(member.Name, member);
                }
            }

            return selected.Values
                .OrderBy(static m => m.Name, StringComparer.Ordinal);
        }

        private static MatchResult MatchSource(ISymbol targetMember, ISymbol[] sourceMembers, string nameMatching)
        {
            var exact = sourceMembers.Where(s => s.Name == targetMember.Name).ToArray();
            if (nameMatching == NameMatchingExact || exact.Length == 1)
            {
                return new MatchResult(exact.Length == 1 ? exact[0] : null, exact.Length > 1);
            }

            var insensitive = sourceMembers.Where(s => string.Equals(s.Name, targetMember.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nameMatching == NameMatchingIgnoreCase || exact.Length == 0)
            {
                return new MatchResult(insensitive.Length == 1 ? insensitive[0] : null, insensitive.Length > 1);
            }

            return new MatchResult(null, exact.Length > 1);
        }

        private static ConstructionModel? SelectConstruction(INamedTypeSymbol targetType, INamedTypeSymbol mapperType, ISymbol[] sourceMembers, string nameMatching, Compilation compilation, Location? location, ICollection<Diagnostic> diagnostics)
        {
            var constructors = targetType.Constructors
                .Where(c => IsAccessibleConstructor(c, targetType, mapperType) && !IsRecordCopyConstructor(c, targetType))
                .OrderBy(static c => c.Parameters.Length)
                .ToArray();
            var marked = constructors.Where(c => HasAttribute(c, MappingConstructorAttributeName)).ToArray();
            if (marked.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.MultipleMappingConstructors, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            if (marked.Length == 1)
            {
                var plan = TryCreateConstruction(marked[0], targetType, sourceMembers, nameMatching, compilation);
                if (plan != null)
                {
                    return plan;
                }

                diagnostics.Add(Diagnostic.Create(Diagnostics.MappingConstructorNotSatisfiable, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            var satisfiable = constructors
                .Where(static c => c.Parameters.Length > 0)
                .Select(c => TryCreateConstruction(c, targetType, sourceMembers, nameMatching, compilation))
                .Where(static c => c != null)
                .Cast<ConstructionModel>()
                .ToArray();
            if (satisfiable.Length == 1)
            {
                return satisfiable[0];
            }

            if (satisfiable.Length > 1)
            {
                var max = satisfiable.Max(static c => c.ParameterCount);
                var best = satisfiable.Where(c => c.ParameterCount == max).ToArray();
                if (best.Length == 1)
                {
                    return best[0];
                }

                diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousConstructor, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            var parameterless = constructors.Where(static c => c.Parameters.Length == 0).ToArray();
            if (targetType.IsValueType || parameterless.Length == 1)
            {
                var constructor = parameterless.FirstOrDefault();
                return new ConstructionModel(0, constructor == null ? ImmutableArray<ConstructorArgumentModel>.Empty : ImmutableArray<ConstructorArgumentModel>.Empty, ImmutableArray<ISymbol>.Empty, constructor != null && HasSetsRequiredMembers(constructor));
            }

            if (parameterless.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousConstructor, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            return null;
        }

        private static ConstructionModel? TryCreateConstruction(IMethodSymbol constructor, INamedTypeSymbol targetType, ISymbol[] sourceMembers, string nameMatching, Compilation compilation)
        {
            var arguments = ImmutableArray.CreateBuilder<ConstructorArgumentModel>();
            var boundMembers = ImmutableArray.CreateBuilder<ISymbol>();
            var targetMembers = GetTargetMembers(targetType, new List<Diagnostic>(), includeConstructorOnly: true).ToArray();

            foreach (var parameter in constructor.Parameters)
            {
                var match = MatchParameter(parameter, sourceMembers, nameMatching);
                if (match.Ambiguous)
                {
                    return null;
                }

                if (match.Member == null)
                {
                    if (parameter.IsOptional)
                    {
                        var optionalTargetMatch = MatchTargetForParameter(parameter, targetMembers, nameMatching);
                        if (optionalTargetMatch.Member != null && !optionalTargetMatch.Ambiguous)
                        {
                            boundMembers.Add(optionalTargetMatch.Member);
                        }

                        continue;
                    }

                    return null;
                }

                if (!compilation.ClassifyConversion(GetMemberType(match.Member), parameter.Type).IsImplicit)
                {
                    return null;
                }

                arguments.Add(new ConstructorArgumentModel(parameter.Name, match.Member.Name, match.Member));
                var targetMatch = MatchTargetForParameter(parameter, targetMembers, nameMatching);
                if (targetMatch.Member != null && !targetMatch.Ambiguous)
                {
                    boundMembers.Add(targetMatch.Member);
                }
            }

            return new ConstructionModel(constructor.Parameters.Length, arguments.ToImmutable(), boundMembers.ToImmutable(), HasSetsRequiredMembers(constructor));
        }

        private static MatchResult MatchParameter(IParameterSymbol parameter, ISymbol[] sourceMembers, string nameMatching)
        {
            var exact = sourceMembers.Where(s => string.Equals(s.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) && s.Name == parameter.Name).ToArray();
            if (nameMatching == NameMatchingExact || exact.Length == 1)
            {
                return new MatchResult(exact.Length == 1 ? exact[0] : null, exact.Length > 1);
            }

            var insensitive = sourceMembers.Where(s => string.Equals(s.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nameMatching == NameMatchingIgnoreCase || exact.Length == 0)
            {
                return new MatchResult(insensitive.Length == 1 ? insensitive[0] : null, insensitive.Length > 1);
            }

            return new MatchResult(null, exact.Length > 1);
        }

        private static MatchResult MatchTargetForParameter(IParameterSymbol parameter, ISymbol[] targetMembers, string nameMatching)
        {
            var exact = targetMembers.Where(s => s.Name == parameter.Name).ToArray();
            if (nameMatching == NameMatchingExact || exact.Length == 1)
            {
                return new MatchResult(exact.Length == 1 ? exact[0] : null, exact.Length > 1);
            }

            var insensitive = targetMembers.Where(s => string.Equals(s.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nameMatching == NameMatchingIgnoreCase || exact.Length == 0)
            {
                return new MatchResult(insensitive.Length == 1 ? insensitive[0] : null, insensitive.Length > 1);
            }

            return new MatchResult(null, exact.Length > 1);
        }

        private static bool IsAccessibleConstructor(IMethodSymbol constructor, INamedTypeSymbol targetType, INamedTypeSymbol mapperType)
        {
            return constructor.DeclaredAccessibility == Accessibility.Public ||
                constructor.DeclaredAccessibility == Accessibility.Internal ||
                constructor.DeclaredAccessibility == Accessibility.Private && IsNestedWithin(mapperType, targetType);
        }

        private static bool IsNestedWithin(INamedTypeSymbol nested, INamedTypeSymbol containing)
        {
            for (var current = nested.ContainingType; current != null; current = current.ContainingType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, containing))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRecordCopyConstructor(IMethodSymbol constructor, INamedTypeSymbol targetType)
        {
            return constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, targetType);
        }

        private static bool HasSetsRequiredMembers(IMethodSymbol constructor)
        {
            return constructor.GetAttributes().Any(static a => a.AttributeClass != null && a.AttributeClass.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
        }

        private static bool IsRequired(ISymbol member)
        {
            return member is IPropertySymbol property && property.IsRequired || member is IFieldSymbol field && field.IsRequired;
        }

        private static bool CanAssignInInitializer(ISymbol member)
        {
            return member is IFieldSymbol field && !field.IsReadOnly && !field.IsConst ||
                member is IPropertySymbol property && property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public;
        }

        private static bool IsMaybeNull(IParameterSymbol parameter)
        {
            return IsMaybeNull(parameter.Type);
        }

        private static bool IsMaybeNull(ITypeSymbol type)
        {
            return !type.IsValueType && type.NullableAnnotation == NullableAnnotation.Annotated;
        }

        private static bool SourceMayBeNull(ISymbol symbol)
        {
            return IsMaybeNull(GetMemberType(symbol));
        }

        private static bool TargetIsNonNullable(ISymbol symbol)
        {
            var type = GetMemberType(symbol);
            return !type.IsValueType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;
        }

        private static ITypeSymbol GetMemberType(ISymbol symbol)
        {
            if (symbol is IPropertySymbol property)
            {
                return property.Type;
            }

            return ((IFieldSymbol)symbol).Type;
        }

        private static void ReportUnmappedTarget(ISymbol member, string policy, ICollection<Diagnostic> diagnostics)
        {
            if (policy == UnmappedMemberPolicyIgnore)
            {
                return;
            }

            diagnostics.Add(Diagnostic.Create(policy == UnmappedMemberPolicyError ? Diagnostics.UnmappedTargetMemberError : Diagnostics.UnmappedTargetMember, member.Locations.FirstOrDefault(), member.Name));
        }

        private static void ReportUnmappedSource(ISymbol member, string policy, ICollection<Diagnostic> diagnostics)
        {
            if (policy == UnmappedMemberPolicyIgnore)
            {
                return;
            }

            diagnostics.Add(Diagnostic.Create(policy == UnmappedMemberPolicyWarning ? Diagnostics.UnmappedSourceMemberWarning : Diagnostics.UnmappedSourceMember, member.Locations.FirstOrDefault(), member.Name));
        }

        private static void ValidateMappingMethod(IMethodSymbol method, List<Diagnostic> diagnostics)
        {
            var location = method.Locations.FirstOrDefault();
            if (method.IsAsync || IsTaskLike(method.ReturnType))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AsyncMappingNotSupported, location, method.Name));
                return;
            }

            if (method.DeclaredAccessibility != Accessibility.Public &&
                method.DeclaredAccessibility != Accessibility.Internal &&
                method.DeclaredAccessibility != Accessibility.Private)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappingAccessibility, location, method.Name));
                return;
            }

            if (method.IsGenericMethod ||
                method.Parameters.Length != 1 ||
                method.ReturnsVoid ||
                method.Parameters[0].RefKind != RefKind.None ||
                (method.ContainingType.IsStatic && !method.IsStatic))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMappingMethodSignature, location, method.Name));
            }

            var options = method.GetAttributes().Where(static a => IsAttribute(a, MappingOptionsAttributeName)).ToArray();
            if (options.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateConfiguration, location, "MappingOptionsAttribute"));
            }
            foreach (var attribute in options)
            {
                ValidateEnumArguments(attribute, Diagnostics.InvalidMethodConfiguration, diagnostics);
            }
        }

        private static bool IsTaskLike(ITypeSymbol type)
        {
            var name = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return name == "global::System.Threading.Tasks.Task" ||
                name == "global::System.Threading.Tasks.Task<TResult>" ||
                name == "global::System.Threading.Tasks.ValueTask" ||
                name == "global::System.Threading.Tasks.ValueTask<TResult>";
        }

        private static ImmutableArray<Diagnostic> ValidateAssemblyConfiguration(Compilation compilation)
        {
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                if (IsAttribute(attribute, LiteMapperDefaultsAttributeName))
                {
                    ValidateEnumArguments(attribute, Diagnostics.InvalidMapperConfiguration, diagnostics);
                }

                if (IsAttribute(attribute, UseMapperAttributeName))
                {
                    ValidateUseMapper(attribute, diagnostics);
                }
            }

            return diagnostics.ToImmutable();
        }

        private static bool EnumValueIsDefined(ITypeSymbol? type, object value)
        {
            return type is INamedTypeSymbol named && named.GetMembers().OfType<IFieldSymbol>().Any(f => f.HasConstantValue && Equals(f.ConstantValue, value));
        }

        private static void ValidateUseMapper(AttributeData attribute, ICollection<Diagnostic> diagnostics)
        {
            var mapperType = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as ITypeSymbol : null;
            var location = attribute.ApplicationSyntaxReference == null ? Location.None : attribute.ApplicationSyntaxReference.GetSyntax().GetLocation();
            if (!(mapperType is INamedTypeSymbol namedMapperType) || mapperType.TypeKind == TypeKind.Error)
            {
                if (attribute.ApplicationSyntaxReference != null &&
                    attribute.ApplicationSyntaxReference.GetSyntax().ToString().IndexOf("typeof(ExternalMapper)", StringComparison.Ordinal) >= 0)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.ExternalMapperMustBeStatic, location, "ExternalMapper"));
                    return;
                }

                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUseMapperType, location, "UseMapper"));
            }
            else if (!namedMapperType.IsStatic)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ExternalMapperMustBeStatic, location, namedMapperType.Name));
            }
            else if (!namedMapperType.GetMembers().OfType<IMethodSymbol>().Any(static m => !m.IsImplicitlyDeclared))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUseMapperType, location, namedMapperType.Name));
            }
        }

        private static void ValidateEnumArguments(AttributeData attribute, DiagnosticDescriptor descriptor, ICollection<Diagnostic> diagnostics)
        {
            foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(static a => a.Value)))
            {
                if (argument.Type != null &&
                    argument.Type.TypeKind == TypeKind.Enum &&
                    argument.Value != null &&
                    !EnumValueIsDefined(argument.Type, argument.Value))
                {
                    diagnostics.Add(Diagnostic.Create(descriptor, attribute.ApplicationSyntaxReference == null ? Location.None : attribute.ApplicationSyntaxReference.GetSyntax().GetLocation(), attribute.AttributeClass == null ? "configuration" : attribute.AttributeClass.Name));
                }
            }

            if (attribute.ApplicationSyntaxReference != null &&
                attribute.ApplicationSyntaxReference.GetSyntax() is AttributeSyntax syntax &&
                syntax.DescendantNodes().OfType<CastExpressionSyntax>().Any(static cast => cast.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression)))
            {
                diagnostics.Add(Diagnostic.Create(descriptor, syntax.GetLocation(), attribute.AttributeClass == null ? "configuration" : attribute.AttributeClass.Name));
            }
        }

        private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol symbol)
        {
            for (var current = symbol.ContainingType; current != null; current = current.ContainingType)
            {
                yield return current;
            }
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            return symbol.GetAttributes().Any(a => IsAttribute(a, metadataName));
        }

        private static bool IsAttribute(AttributeData attribute, string metadataName)
        {
            return attribute.AttributeClass != null &&
                attribute.AttributeClass.ToDisplayString() == metadataName;
        }

        private static string RenderDeclaration(INamedTypeSymbol symbol, ImmutableArray<MappingModel> mappings)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine("#nullable enable");
            AppendNamespaceStart(builder, symbol);
            foreach (var containingType in ContainingTypes(symbol).Reverse())
            {
                builder.Append("partial ");
                builder.Append(containingType.TypeKind == TypeKind.Struct ? "struct " : "class ");
                builder.AppendLine(containingType.Name);
                builder.AppendLine("{");
            }

            builder.Append(symbol.IsStatic ? "static partial class " : "partial class ");
            builder.AppendLine(symbol.Name);
            builder.AppendLine("{");
            foreach (var mapping in mappings.OrderBy(static m => m.Method.Name, StringComparer.Ordinal))
            {
                AppendMapping(builder, mapping);
                foreach (var helper in FlattenHelpers(mapping).OrderBy(static h => h.HelperName, StringComparer.Ordinal))
                {
                    AppendMapping(builder, helper);
                }
            }
            builder.AppendLine("}");

            foreach (var _ in ContainingTypes(symbol))
            {
                builder.AppendLine("}");
            }

            AppendNamespaceEnd(builder, symbol);
            return builder.ToString();
        }

        private static void AppendMapping(StringBuilder builder, MappingModel mapping)
        {
            var method = mapping.Method;
            var parameter = method.Parameters[0];
            builder.Append("    ");
            builder.Append(mapping.HelperName == null ? ToAccessibility(method.DeclaredAccessibility) : "private");
            builder.Append(' ');
            if (method.IsStatic || mapping.HelperName != null)
            {
                builder.Append("static ");
            }

            if (mapping.HelperName == null)
            {
                builder.Append("partial ");
            }

            builder.Append((mapping.HelperTargetType ?? method.ReturnType).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            builder.Append(' ');
            builder.Append(mapping.HelperName ?? method.Name);
            builder.Append('(');
            builder.Append((mapping.HelperSourceType ?? parameter.Type).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            builder.Append(' ');
            builder.Append(parameter.Name);
            builder.AppendLine(")");
            builder.AppendLine("    {");

            if (mapping.SourceNullable)
            {
                builder.Append("        if (");
                builder.Append(parameter.Name);
                builder.AppendLine(" == null)");
                builder.AppendLine("        {");
                if (mapping.ReturnNullable)
                {
                    builder.AppendLine("            return null;");
                }
                else
                {
                    builder.Append("            throw new global::System.ArgumentNullException(nameof(");
                    builder.Append(parameter.Name);
                    builder.AppendLine("));");
                }

                builder.AppendLine("        }");
            }
            else if (!parameter.Type.IsValueType)
            {
                builder.Append("        if (");
                builder.Append(parameter.Name);
                builder.AppendLine(" == null)");
                builder.AppendLine("        {");
                builder.Append("            throw new global::System.ArgumentNullException(nameof(");
                builder.Append(parameter.Name);
                builder.AppendLine("));");
                builder.AppendLine("        }");
            }

            foreach (var precondition in mapping.Preconditions)
            {
                builder.Append("        if (");
                builder.Append(precondition.Expression);
                builder.AppendLine(")");
                builder.AppendLine("        {");
                builder.Append("            throw new global::System.InvalidOperationException(\"");
                builder.Append(precondition.Message.Replace("\\", "\\\\").Replace("\"", "\\\""));
                builder.AppendLine("\");");
                builder.AppendLine("        }");
            }

            builder.Append("        var target = new ");
            builder.Append((mapping.HelperTargetType ?? method.ReturnType).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).TrimEnd('?'));
            builder.Append('(');
            if (mapping.Construction != null)
            {
                var first = true;
                foreach (var argument in mapping.Construction.Arguments)
                {
                    if (!first)
                    {
                        builder.Append(", ");
                    }

                    first = false;
                    builder.Append(argument.ParameterName);
                    builder.Append(": ");
                    builder.Append(parameter.Name);
                    builder.Append('.');
                    builder.Append(argument.SourceName);
                }
            }

            builder.Append(')');
            if (mapping.Assignments.Length == 0)
            {
                builder.AppendLine(";");
            }
            else
            {
                builder.AppendLine();
                builder.AppendLine("        {");
                for (var i = 0; i < mapping.Assignments.Length; i++)
                {
                    var assignment = mapping.Assignments[i];
                    builder.Append("            ");
                    builder.Append(assignment.TargetName);
                    builder.Append(" = ");
                    builder.Append(assignment.Expression);
                    builder.AppendLine(i == mapping.Assignments.Length - 1 ? string.Empty : ",");
                }

                builder.AppendLine("        };");
            }

            builder.AppendLine("        return target;");
            builder.AppendLine("    }");
        }

        private static IEnumerable<MappingModel> FlattenHelpers(MappingModel mapping)
        {
            foreach (var helper in mapping.Helpers)
            {
                yield return helper;
                foreach (var nested in FlattenHelpers(helper))
                {
                    yield return nested;
                }
            }
        }

        private static string ToAccessibility(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Internal:
                    return "internal";
                default:
                    return "private";
            }
        }

        private static void AppendNamespaceStart(StringBuilder builder, INamedTypeSymbol symbol)
        {
            if (!symbol.ContainingNamespace.IsGlobalNamespace)
            {
                builder.Append("namespace ");
                builder.AppendLine(symbol.ContainingNamespace.ToDisplayString());
                builder.AppendLine("{");
            }
        }

        private static void AppendNamespaceEnd(StringBuilder builder, INamedTypeSymbol symbol)
        {
            if (!symbol.ContainingNamespace.IsGlobalNamespace)
            {
                builder.AppendLine("}");
            }
        }

        private static string CreateHintName(INamedTypeSymbol symbol)
        {
            var metadataName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty).Replace('+', '.');
            return metadataName + "." + StableHash(metadataName) + ".g.cs";
        }

        private static string StableHash(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                return string.Concat(hash.Take(4).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            }
        }

        private sealed class MapperModel
        {
            public MapperModel(string displayName, Location location, string hintName, string source, ImmutableArray<Diagnostic> diagnostics)
            {
                DisplayName = displayName;
                Location = location;
                HintName = hintName;
                Source = source;
                Diagnostics = diagnostics;
            }

            public string DisplayName { get; }

            public Location Location { get; }

            public string HintName { get; }

            public string Source { get; }

            public ImmutableArray<Diagnostic> Diagnostics { get; }
        }

        private sealed class MappingModel
        {
            public MappingModel(IMethodSymbol method, bool sourceNullable, bool returnNullable, string nullableMismatch, ConstructionModel? construction, ImmutableArray<PreconditionModel> preconditions, ImmutableArray<AssignmentModel> assignments, ImmutableArray<MappingModel> helpers, string? helperName, ITypeSymbol? helperSourceType, ITypeSymbol? helperTargetType)
            {
                Method = method;
                SourceNullable = sourceNullable;
                ReturnNullable = returnNullable;
                NullableMismatch = nullableMismatch;
                Construction = construction;
                Preconditions = preconditions;
                Assignments = assignments;
                Helpers = helpers;
                HelperName = helperName;
                HelperSourceType = helperSourceType;
                HelperTargetType = helperTargetType;
            }

            public IMethodSymbol Method { get; }

            public bool SourceNullable { get; }

            public bool ReturnNullable { get; }

            public string NullableMismatch { get; }

            public ConstructionModel? Construction { get; }

            public ImmutableArray<PreconditionModel> Preconditions { get; }

            public ImmutableArray<AssignmentModel> Assignments { get; }

            public ImmutableArray<MappingModel> Helpers { get; }

            public string? HelperName { get; }

            public ITypeSymbol? HelperSourceType { get; }

            public ITypeSymbol? HelperTargetType { get; }
        }

        private sealed class ConstructionModel
        {
            public ConstructionModel(int parameterCount, ImmutableArray<ConstructorArgumentModel> arguments, ImmutableArray<ISymbol> boundTargetMembers, bool setsRequiredMembers)
            {
                ParameterCount = parameterCount;
                Arguments = arguments;
                BoundTargetMembers = boundTargetMembers;
                SetsRequiredMembers = setsRequiredMembers;
            }

            public int ParameterCount { get; }

            public ImmutableArray<ConstructorArgumentModel> Arguments { get; }

            public ImmutableArray<ISymbol> BoundTargetMembers { get; }

            public bool SetsRequiredMembers { get; }
        }

        private sealed class ConstructorArgumentModel
        {
            public ConstructorArgumentModel(string parameterName, string sourceName, ISymbol sourceMember)
            {
                ParameterName = parameterName;
                SourceName = sourceName;
                SourceMember = sourceMember;
            }

            public string ParameterName { get; }

            public string SourceName { get; }

            public ISymbol SourceMember { get; }
        }

        private sealed class AssignmentModel
        {
            public AssignmentModel(string targetName, string expression)
            {
                TargetName = targetName;
                Expression = expression;
            }

            public string TargetName { get; }

            public string Expression { get; }
        }

        private sealed class PreconditionModel
        {
            public PreconditionModel(string expression, string message)
            {
                Expression = expression;
                Message = message;
            }

            public string Expression { get; }

            public string Message { get; }
        }

        private sealed class ExplicitMemberConfiguration
        {
            public ExplicitMemberConfiguration(string targetName, SourcePathModel? selectedSource, string? use, INamedTypeSymbol? converterType)
            {
                TargetName = targetName;
                SelectedSource = selectedSource;
                Use = use;
                ConverterType = converterType;
            }

            public string TargetName { get; }

            public SourcePathModel? SelectedSource { get; }

            public string? Use { get; }

            public INamedTypeSymbol? ConverterType { get; }
        }

        private sealed class SourcePathModel
        {
            public SourcePathModel(string expression, ISymbol sourceMember, ITypeSymbol type, ImmutableArray<string> nullCheckExpressions)
            {
                Expression = expression;
                SourceMember = sourceMember;
                Type = type;
                NullCheckExpressions = nullCheckExpressions;
            }

            public string Expression { get; }

            public ISymbol SourceMember { get; }

            public ITypeSymbol Type { get; }

            public ImmutableArray<string> NullCheckExpressions { get; }
        }

        private sealed class ConversionModel
        {
            public ConversionModel(string expression, ISymbol? sourceMember, bool potentiallyNull, string memberPath, string? nullCheckExpression)
            {
                Expression = expression;
                SourceMember = sourceMember;
                PotentiallyNull = potentiallyNull;
                MemberPath = memberPath;
                NullCheckExpression = nullCheckExpression;
            }

            public string Expression { get; }

            public ISymbol? SourceMember { get; }

            public bool PotentiallyNull { get; }

            public string MemberPath { get; }

            public string? NullCheckExpression { get; }
        }

        private sealed class EffectiveMappingOptions
        {
            public EffectiveMappingOptions(string nameMatching, string unmappedTargetMembers, string unmappedSourceMembers, string nullableMismatch)
            {
                NameMatching = nameMatching;
                UnmappedTargetMembers = unmappedTargetMembers;
                UnmappedSourceMembers = unmappedSourceMembers;
                NullableMismatch = nullableMismatch;
            }

            public string NameMatching { get; }

            public string UnmappedTargetMembers { get; }

            public string UnmappedSourceMembers { get; }

            public string NullableMismatch { get; }
        }

        private sealed class MatchResult
        {
            public MatchResult(ISymbol? member, bool ambiguous)
            {
                Member = member;
                Ambiguous = ambiguous;
            }

            public ISymbol? Member { get; }

            public bool Ambiguous { get; }
        }
    }
}
