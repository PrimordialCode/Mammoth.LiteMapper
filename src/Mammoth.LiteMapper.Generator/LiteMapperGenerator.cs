using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
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
        private const string DefaultMappingAttributeName = "Mammoth.LiteMapper.DefaultMappingAttribute";
        private const string MappingConstructorAttributeName = "Mammoth.LiteMapper.MappingConstructorAttribute";
        private const string MappingOptionsAttributeName = "Mammoth.LiteMapper.MappingOptionsAttribute";
        private const string UseMapperAttributeName = "Mammoth.LiteMapper.UseMapperAttribute";
        private const string UseTargetDefaultAttributeName = "Mammoth.LiteMapper.UseTargetDefaultAttribute";
        private const string NameMatchingExact = "Exact";
        private const string NameMatchingIgnoreCase = "IgnoreCase";
        private const string UnmappedMemberPolicyIgnore = "Ignore";
        private const string UnmappedMemberPolicyInfo = "Info";
        private const string UnmappedMemberPolicyWarning = "Warning";
        private const string UnmappedMemberPolicyError = "Error";
        private const string NullableMismatchPolicyError = "Error";
        private const string NullableMismatchPolicyThrow = "Throw";
        private const string NullCollectionStrategyError = "Error";
        private const string NullCollectionStrategyPreserve = "Preserve";
        private const string NullCollectionStrategyEmpty = "Empty";
        private const string EnumMappingStrategyByName = "ByName";
        private const string EnumMappingStrategyByValue = "ByValue";
        private const string EnumNumericConversionChecked = "Checked";
        private const string EnumNumericConversionUnchecked = "Unchecked";
        private const string UnmatchedEnumValuePolicyError = "Error";
        private const string UnmatchedEnumValuePolicyThrow = "Throw";
        private const string UnmatchedEnumValuePolicyByValue = "ByValue";
        private const string ReferenceHandlingNone = "None";
        private const string ReferenceHandlingThrowOnCycle = "ThrowOnCycle";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            InitializeCore(context, null);
        }

        /// <summary>
        /// Registers the production pipeline with an optional internal fault-injection callback.
        /// Tests use the callback to verify mapper isolation without mutable generator state.
        /// </summary>
        internal static void InitializeCore(IncrementalGeneratorInitializationContext context, Action<INamedTypeSymbol>? beforePlanning)
        {
            var language = context.ParseOptionsProvider.Select(static (options, _) =>
            {
                var csharp = (CSharpParseOptions)options;
                return csharp.LanguageVersion;
            });
            var generatorOptions = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => GeneratorOptions.From(options.GlobalOptions));

            var assemblyConfiguration = context.CompilationProvider.Select(static (compilation, _) => ValidateAssemblyConfiguration(compilation));
            var canGenerateFromAssembly = assemblyConfiguration.Select(static (diagnostics, _) => !diagnostics.Any(IsFatalDiagnostic));

            var mappers = context.SyntaxProvider
                .CreateSyntaxProvider(static (node, _) => IsTypeWithAttributes(node), static (ctx, _) => ctx)
                .Combine(generatorOptions)
                .Select((input, cancellationToken) => CreateMapperModel(input.Left, input.Right, beforePlanning, cancellationToken))
                .Where(static model => model != null)
                .Select(static (model, _) => model!);
            var sortedMappers = mappers.Collect().Select(static (items, _) => items.OrderBy(static item => item.DisplayName, StringComparer.Ordinal).ToImmutableArray());
            var emissions = mappers.Where(static mapper => mapper.CanGenerate)
                .Combine(canGenerateFromAssembly).Where(static input => input.Right).Select(static (input, _) => input.Left)
                .Combine(language).Combine(generatorOptions)
                .Select(static (input, _) => new MapperEmission(input.Left.Left, input.Left.Right, input.Right))
                .WithComparer(EqualityComparer<MapperEmission>.Default)
                .WithTrackingName("MapperEmission");

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
                    foreach (var diagnostic in mapper.Diagnostics)
                    {
                        production.ReportDiagnostic(diagnostic);
                    }
                }
            });

            context.RegisterSourceOutput(emissions, static (production, emission) =>
            {
                try
                {
                    if (emission.Source == null)
                    {
                        production.ReportDiagnostic(Diagnostic.Create(Diagnostics.InternalGeneratorFailure,
                            Location.Create(emission.Path, emission.Span, emission.LineSpan), emission.DisplayName));
                        return;
                    }

                    production.AddSource(emission.HintName, SourceText.From(emission.Source, Encoding.UTF8));
                }
                catch (Exception)
                {
                    if (emission.TreatInternalGeneratorErrorsAsExceptions)
                    {
                        throw;
                    }

                    production.ReportDiagnostic(Diagnostic.Create(Diagnostics.InternalGeneratorFailure,
                        Location.Create(emission.Path, emission.Span, emission.LineSpan), emission.DisplayName));
                }
            });
        }

        /// <summary>
        /// Holds the source-output payload without retaining compilation symbols or syntax trees.
        /// Structural equality lets unchanged mapper outputs remain cached after semantic replanning.
        /// </summary>
        private sealed class MapperEmission : IEquatable<MapperEmission>
        {
            public MapperEmission(MapperModel mapper, LanguageVersion version, GeneratorOptions options)
            {
                DisplayName = mapper.DisplayName;
                HintName = mapper.HintName;
                TreatInternalGeneratorErrorsAsExceptions = options.TreatInternalGeneratorErrorsAsExceptions;
                var lineSpan = mapper.Location.GetLineSpan();
                Path = lineSpan.Path;
                Span = mapper.Location.SourceSpan;
                LineSpan = lineSpan.Span;
                try
                {
                    Source = ApplyGeneratorOptions(mapper, version, options);
                }
                catch (Exception)
                {
                    if (TreatInternalGeneratorErrorsAsExceptions)
                    {
                        throw;
                    }
                }
            }

            public string DisplayName { get; }
            public string HintName { get; }
            public string? Source { get; }
            public bool TreatInternalGeneratorErrorsAsExceptions { get; }
            public string Path { get; }
            public TextSpan Span { get; }
            public LinePositionSpan LineSpan { get; }

            public bool Equals(MapperEmission? other) => other != null &&
                DisplayName == other.DisplayName && HintName == other.HintName && Source == other.Source &&
                TreatInternalGeneratorErrorsAsExceptions == other.TreatInternalGeneratorErrorsAsExceptions &&
                (Source != null || (Path == other.Path && Span.Equals(other.Span) && LineSpan.Equals(other.LineSpan)));

            public override bool Equals(object? obj) => Equals(obj as MapperEmission);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(HintName) * 397) ^ (Source == null ? 0 : StringComparer.Ordinal.GetHashCode(Source));
                }
            }
        }

        private static string ApplyGeneratorOptions(MapperModel mapper, LanguageVersion version, GeneratorOptions options)
        {
            if (!options.IncludeGeneratedSourceComments && !options.EmitDebugMetadata)
            {
                return mapper.Source;
            }

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated/>");
            if (options.IncludeGeneratedSourceComments)
            {
                builder.Append("// LiteMapper generated mapper: ");
                builder.AppendLine(mapper.DisplayName);
            }

            if (options.EmitDebugMetadata)
            {
                builder.Append("// LiteMapper language version: ");
                builder.AppendLine(version.ToDisplayString());
            }

            var firstLineEnd = mapper.Source.IndexOf('\n');
            return firstLineEnd < 0 ? builder.ToString() : builder + mapper.Source.Substring(firstLineEnd + 1);
        }

        private static bool IsTypeWithAttributes(SyntaxNode node)
        {
            return node is TypeDeclarationSyntax type && type.AttributeLists.Count > 0;
        }

        private static MapperModel? CreateMapperModel(GeneratorSyntaxContext context, GeneratorOptions options, Action<INamedTypeSymbol>? beforePlanning, CancellationToken cancellationToken)
        {
            var typeSyntax = (TypeDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(typeSyntax, cancellationToken) as INamedTypeSymbol;
            if (symbol == null || !HasAttribute(symbol, LiteMapperAttributeName))
            {
                return null;
            }

            try
            {
                beforePlanning?.Invoke(symbol);
                return PlanMapper(symbol, typeSyntax, context.SemanticModel.Compilation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (!options.TreatInternalGeneratorErrorsAsExceptions)
            {
                var displayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                var location = typeSyntax.Identifier.GetLocation();
                return new MapperModel(displayName, location, CreateHintName(symbol), string.Empty,
                    ImmutableArray.Create(Diagnostic.Create(Diagnostics.InternalGeneratorFailure, location, displayName)), false);
            }
        }

        private static MapperModel PlanMapper(INamedTypeSymbol symbol, TypeDeclarationSyntax typeSyntax, Compilation compilation)
        {
            var diagnostics = new List<Diagnostic>();
            ValidateMapper(symbol, typeSyntax, compilation, diagnostics);
            var hasMapperErrors = diagnostics.Any(IsFatalDiagnostic);

            var mappings = ImmutableArray.CreateBuilder<MappingModel>();
            foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>().Where(static m => m.PartialDefinitionPart == null && IsPartialDeclaration(m)).OrderBy(static m => m.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal))
            {
                var methodDiagnostics = new List<Diagnostic>();
                ValidateMappingMethod(member, methodDiagnostics);
                MappingModel? mapping = null;
                if (!hasMapperErrors && !methodDiagnostics.Any(IsFatalDiagnostic))
                {
                    if (IsNewObjectMappingCandidate(member))
                    {
                        mapping = CreateMappingModel(member, compilation, methodDiagnostics);
                    }
                    else if (IsUpdateMappingCandidate(member))
                    {
                        mapping = CreateUpdateMappingModel(member, compilation, methodDiagnostics);
                    }
                }

                if (mapping != null)
                {
                    var recursiveNames = FindRecursiveHelperNames(mapping);
                    if (recursiveNames.Count != 0 && mapping.ReferenceHandling == ReferenceHandlingNone)
                    {
                        methodDiagnostics.Add(Diagnostic.Create(Diagnostics.RecursiveMappingWithoutCycleDetection, mapping.Method.Locations.FirstOrDefault(), mapping.Method.Name));
                    }
                    if (!methodDiagnostics.Any(IsFatalDiagnostic))
                    {
                        mappings.Add(mapping);
                    }
                }

                diagnostics.AddRange(methodDiagnostics);
            }

            var completedMappings = mappings.ToImmutable();
            var canGenerate = !hasMapperErrors && (completedMappings.Length != 0 || !diagnostics.Any(IsFatalDiagnostic));
            var source = RenderDeclaration(symbol, completedMappings);
            return new MapperModel(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), typeSyntax.Identifier.GetLocation(), CreateHintName(symbol), source, diagnostics.ToImmutableArray(), canGenerate);
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

        private static bool IsFatalDiagnostic(Diagnostic diagnostic)
        {
            return diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable);
        }

        private static void ValidateMapper(INamedTypeSymbol symbol, TypeDeclarationSyntax syntax, Compilation compilation, List<Diagnostic> diagnostics)
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
                ValidateUseMapper(attribute, diagnostics, compilation, symbol);
            }
            foreach (var attribute in compilation.Assembly.GetAttributes().Where(static a => IsAttribute(a, UseMapperAttributeName)))
            {
                // Global validation owns malformed registrations; accessibility depends on this mapper.
                var registrationDiagnostics = new List<Diagnostic>();
                ValidateUseMapper(attribute, registrationDiagnostics);
                if (registrationDiagnostics.Count == 0)
                {
                    ValidateUseMapper(attribute, diagnostics, compilation, symbol);
                }
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

        private static bool IsNewObjectMappingCandidate(IMethodSymbol method)
        {
            return !IsAsyncMethod(method) &&
                !IsTaskLike(method.ReturnType) &&
                !method.IsGenericMethod &&
                method.Parameters.Length == 1 &&
                !method.ReturnsVoid &&
                method.Parameters[0].RefKind == RefKind.None &&
                (!method.ContainingType.IsStatic || method.IsStatic);
        }

        private static bool IsUpdateMappingCandidate(IMethodSymbol method)
        {
            return !IsAsyncMethod(method) &&
                !IsTaskLike(method.ReturnType) &&
                !method.IsGenericMethod &&
                method.Parameters.Length == 2 &&
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
            if (options.IgnoreNullSourceMembers)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.IgnoreNullOnlyValidForUpdate, location, method.Name));
            }

            var externalTypes = GetRegisteredMapperTypes(method.ContainingType, compilation).ToArray();
            var topLevelConversion = ResolveTopLevelConversion(method, sourceType, targetType, compilation, externalTypes, diagnostics, options);
            if (topLevelConversion != null)
            {
                return new MappingModel(method, sourceNullable, returnNullable, options.NullableMismatch, options.ReferenceHandling, options.GuardNonNullSource, null, ImmutableArray<PreconditionModel>.Empty, ImmutableArray<AssignmentModel>.Empty, ImmutableArray<MappingModel>.Empty, null, null, null, "        return " + topLevelConversion.Expression + ";\n");
            }

            if (IsTupleBoundary(sourceType, targetType))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ConversionNotFound, location,
                    sourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return new MappingModel(method, sourceNullable, returnNullable, options.NullableMismatch, options.ReferenceHandling, options.GuardNonNullSource, null, ImmutableArray<PreconditionModel>.Empty, ImmutableArray<AssignmentModel>.Empty, ImmutableArray<MappingModel>.Empty, null, null, null, string.Empty);
            }

            var topLevelCollection = CreateCollectionMappingModel(method, sourceType, targetType, method.Parameters[0].Name, null, compilation, diagnostics, ImmutableArray.CreateBuilder<MappingModel>(), new HashSet<string>(StringComparer.Ordinal), options, helperName: null);
            if (topLevelCollection != null)
            {
                return topLevelCollection;
            }

            if (sourceType.IsRefLikeType || targetType.IsRefLikeType)
            {
                var unsupported = sourceType.IsRefLikeType ? sourceType : targetType;
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedGeneratedType, location, unsupported.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return new MappingModel(method, sourceNullable, returnNullable, options.NullableMismatch, options.ReferenceHandling, options.GuardNonNullSource, null, ImmutableArray<PreconditionModel>.Empty, ImmutableArray<AssignmentModel>.Empty, ImmutableArray<MappingModel>.Empty, null, null, null, string.Empty);
            }

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

            if (sourceNullable && !returnNullable && options.NullableMismatch == NullableMismatchPolicyError)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, location, method.Parameters[0].Name));
            }

            var construction = targetType is INamedTypeSymbol namedTarget ? SelectConstruction(namedTarget, method, sourceMembers, compilation, diagnostics, options, helpers, helperNames, explicitConfigurations, targetDefaults) : null;
            if (construction == null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ConstructorNotFound, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            foreach (var argument in construction == null ? Enumerable.Empty<ConstructorArgumentModel>() : construction.Arguments)
            {
                if (argument.SourceMember != null)
                {
                    usedSources.Add(argument.SourceMember);
                }
            }

            foreach (var targetMember in GetTargetMembers(targetType, diagnostics, includeConstructorOnly: true))
            {
                if (construction != null && construction.BoundTargetMembers.Any(m => SymbolEqualityComparer.Default.Equals(m, targetMember)))
                {
                    if (IsRequired(targetMember) && !construction.SetsRequiredMembers)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    }

                    continue;
                }

                if (ignoredTargets.Contains(targetMember.Name) || targetDefaults.Contains(targetMember.Name))
                {
                    if (targetDefaults.Contains(targetMember.Name) && !HasWritableDefault(targetMember) && (construction == null || !construction.BoundTargetMembers.Any(m => SymbolEqualityComparer.Default.Equals(m, targetMember))))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.TargetDefaultMissing, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    }

                    var constructorSatisfiesRequired = IsRequired(targetMember) && construction != null && construction.SetsRequiredMembers;
                    if (IsRequired(targetMember) && !constructorSatisfiesRequired ||
                        ignoredTargets.Contains(targetMember.Name) && !targetDefaults.Contains(targetMember.Name) &&
                        TargetIsNonNullable(targetMember) && !constructorSatisfiesRequired)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    }

                    continue;
                }

                if (!CanAssignInInitializer(targetMember))
                {
                    if (IsRequired(targetMember) || TargetIsNonNullable(targetMember))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    }
                    else
                    {
                        ReportUnmappedTarget(targetMember, options.UnmappedTargetMembers, diagnostics);
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

                    if (TargetIsNonNullable(targetMember))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
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
                    if (IsCollectionType(GetMemberType(targetMember), compilation) && options.NullCollections == NullCollectionStrategyEmpty)
                    {
                        conversion = new ConversionModel(conversion.Expression, conversion.SourceMember, potentiallyNull: false, conversion.MemberPath, conversion.NullCheckExpression);
                    }
                    else
                    {
                        if (options.NullableMismatch == NullableMismatchPolicyError)
                        {
                            diagnostics.Add(Diagnostic.Create(IsCollectionType(GetMemberType(targetMember), compilation) ? Diagnostics.InvalidNullCollectionMapping : Diagnostics.NullableToNonNullable, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
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
                }

                if (conversion.SourceMember != null)
                {
                    usedSources.Add(conversion.SourceMember);
                }

                var directMemberExpression = conversion.SourceMember == null
                    ? null
                    : EscapeIdentifier(method.Parameters[0].Name) + "." + EscapeIdentifier(conversion.SourceMember.Name);
                assignments.Add(new AssignmentModel(targetMember.Name, conversion.Expression,
                    isDirectMemberCopy: string.Equals(conversion.Expression, directMemberExpression, StringComparison.Ordinal)));
            }

            if (options.UnmappedSourceMembers != UnmappedMemberPolicyIgnore)
            {
                foreach (var sourceMember in sourceMembers.Where(m => !usedSources.Contains(m) && !ignoredSources.Contains(m.Name)).OrderBy(static m => m.Name, StringComparer.Ordinal))
                {
                    ReportUnmappedSource(sourceMember, options.UnmappedSourceMembers, diagnostics);
                }
            }

            var emitAggressiveInlining = HasAggressiveInlining(compilation) &&
                !sourceNullable &&
                !options.GuardNonNullSource &&
                options.ReferenceHandling == ReferenceHandlingNone &&
                construction != null &&
                construction.Arguments.Length == 0 &&
                preconditions.Count == 0 &&
                helpers.Count == 0 &&
                assignments.Count != 0 &&
                assignments.All(static assignment => assignment.IsDirectMemberCopy);
            return new MappingModel(method, sourceNullable, returnNullable, options.NullableMismatch, options.ReferenceHandling, options.GuardNonNullSource, construction, preconditions.ToImmutable(), assignments.ToImmutable(), helpers.ToImmutable(), null, null, null, customBody: null, emitAggressiveInlining: emitAggressiveInlining);
        }

        private static MappingModel CreateUpdateMappingModel(IMethodSymbol method, Compilation compilation, List<Diagnostic> diagnostics)
        {
            var source = method.Parameters[0];
            var destination = method.Parameters[1];
            var sourceType = source.Type;
            var targetType = destination.Type;
            var sourceNullable = IsMaybeNull(source);
            var destinationNullable = IsMaybeNull(destination);
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

            if (targetType is IArrayTypeSymbol)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.TopLevelArrayUpdateNotSupported, location, method.Name));
            }

            if (targetType.IsValueType && destination.RefKind != RefKind.Ref)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.StructUpdateRequiresRef, location, method.Name));
            }

            if (!targetType.IsValueType && destination.RefKind != RefKind.None)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUpdateSignature, location, method.Name));
            }

            if (method.ReturnsVoid && destinationNullable)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.NullableVoidDestinationNotSupported, location, method.Name));
            }

            if (!method.ReturnsVoid && !SymbolEqualityComparer.Default.Equals(method.ReturnType, targetType))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUpdateSignature, location, method.Name));
            }

            ConstructionModel? construction = null;
            if (!targetType.IsValueType && method.ReturnsVoid == false && destinationNullable)
            {
                construction = targetType is INamedTypeSymbol namedTarget ? SelectConstruction(namedTarget, method, sourceMembers, compilation, diagnostics, options, helpers, helperNames, explicitConfigurations, targetDefaults) : null;
                if (construction == null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.ConstructorNotFound, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                }
            }

            foreach (var targetMember in GetVisibleMembers(targetType, diagnostics, static m => m is IFieldSymbol field && !field.IsConst || m is IPropertySymbol property && property.Parameters.Length == 0))
            {
                if (construction != null && IsRequired(targetMember) && !construction.SetsRequiredMembers &&
                    (construction.BoundTargetMembers.Any(member => SymbolEqualityComparer.Default.Equals(member, targetMember)) ||
                     ignoredTargets.Contains(targetMember.Name) || targetDefaults.Contains(targetMember.Name)))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                    continue;
                }

                if (ignoredTargets.Contains(targetMember.Name) || targetDefaults.Contains(targetMember.Name))
                {
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

                if (match.Member == null && explicitConfiguration?.Use != null && selected == null)
                {
                    match = new MatchResult(method.Parameters[0], false);
                }

                if (match.Member == null)
                {
                    ReportUnmappedTarget(targetMember, options.UnmappedTargetMembers, diagnostics);
                    continue;
                }

                string? sourcePathCaptureName = null;
                string? sourcePathCaptureGuard = null;
                if (options.IgnoreNullSourceMembers && selected != null && SourcePathMayBeNull(selected))
                {
                    sourcePathCaptureName = CreateExpressionCaptureName(method, targetMember.Name, selected.Expression);
                    sourcePathCaptureGuard = BuildNullSafeSourcePathExpression(source.Name, selected) + " is { } " + sourcePathCaptureName;
                }
                else if (options.IgnoreNullSourceMembers && selected == null && !(match.Member is IParameterSymbol) &&
                    GetMemberType(match.Member) is INamedTypeSymbol nullableMemberType &&
                    nullableMemberType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    var directExpression = EscapeIdentifier(source.Name) + "." + EscapeIdentifier(match.Member.Name);
                    sourcePathCaptureName = CreateExpressionCaptureName(method, targetMember.Name, match.Member.Name);
                    sourcePathCaptureGuard = directExpression + " is { } " + sourcePathCaptureName;
                }

                if (!CanAssignAfterConstruction(targetMember) || explicitConfiguration?.Use != null)
                {
                    var memberType = GetMemberType(targetMember);
                    if (targetMember is IPropertySymbol nestedProperty && (nestedProperty.SetMethod == null || CanAssignAfterConstruction(targetMember)) &&
                        memberType.IsReferenceType && !IsCollectionType(memberType, compilation) &&
                        !(nestedProperty.SetMethod == null && IsMaybeNull(memberType)))
                    {
                        var diagnosticCount = diagnostics.Count;
                        var rootArgument = explicitConfiguration?.Use != null && selected == null;
                        var nestedSourceType = selected?.Type ?? (rootArgument ? sourceType : GetMemberType(match.Member));
                        var updater = ResolveNestedUpdater(method, nestedSourceType, memberType, compilation, externalTypes, diagnostics, targetMember, explicitConfiguration);
                        if (updater != null)
                        {
                            var receiver = updater.IsStatic ? DisplayType(updater.ContainingType) + "." : "this.";
                            var escapedSourceName = EscapeIdentifier(source.Name);
                            var sourcePath = selected?.Expression ?? match.Member.Name;
                            var sourceMayBeNull = !rootArgument && (selected != null ? SourcePathMayBeNull(selected) : SourceMayBeNull(match.Member));
                            var throwCaptureName = sourceMayBeNull && selected != null && !options.IgnoreNullSourceMembers &&
                                !IsMaybeNull(updater.Parameters[0].Type) && options.NullableMismatch != NullableMismatchPolicyError
                                ? CreateExpressionCaptureName(method, targetMember.Name, selected.Expression)
                                : null;
                            var argument = rootArgument
                                ? escapedSourceName
                                : selected != null
                                    ? sourcePathCaptureName ?? throwCaptureName ?? BuildNullSafeSourcePathExpression(source.Name, selected)
                                    : escapedSourceName + "." + EscapeIdentifier(match.Member.Name);
                            string? updaterGuard = null;
                            if (sourceMayBeNull)
                            {
                                if (options.IgnoreNullSourceMembers)
                                {
                                    updaterGuard = sourcePathCaptureGuard ?? BuildPatchGuard(source.Name, explicitConfiguration, match.Member);
                                }
                                else if (!IsMaybeNull(updater.Parameters[0].Type))
                                {
                                    if (options.NullableMismatch == NullableMismatchPolicyError)
                                    {
                                        diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                                        continue;
                                    }

                                    var nullCheck = throwCaptureName != null
                                        ? "!(" + BuildNullSafeSourcePathExpression(source.Name, selected!) + " is { } " + throwCaptureName + ")"
                                        : selected == null ? argument + " == null" : BuildNullCheck(source.Name, selected);
                                    if (nullCheck != null)
                                    {
                                        preconditions.Add(new PreconditionModel(nullCheck, "Source member path '" + sourcePath + "' was null."));
                                    }
                                }
                            }
                            assignments.Add(new AssignmentModel(targetMember.Name,
                                receiver + EscapeIdentifier(updater.Name) + "(" + argument + ", " + EscapeIdentifier(destination.Name) + "." + EscapeIdentifier(targetMember.Name) + ")",
                                guard: updaterGuard, isStatement: updater.ReturnsVoid || !CanAssignAfterConstruction(targetMember)));
                            usedSources.Add(match.Member);
                            continue;
                        }
                        if (diagnostics.Count != diagnosticCount)
                        {
                            continue;
                        }
                    }
                    if (!CanAssignAfterConstruction(targetMember))
                    {
                        var descriptor = targetMember is IPropertySymbol property && property.SetMethod?.IsInitOnly == true
                            ? Diagnostics.InitOnlyMemberCannotBeUpdated
                            : IsCollectionType(memberType, compilation) ? Diagnostics.GetOnlyCollectionUpdateNotSupported
                            : memberType is INamedTypeSymbol namedMemberType && IsStructuralObjectType(namedMemberType) ? Diagnostics.NestedUpdateMappingRequired
                            : Diagnostics.TargetMemberNotWritable;
                        diagnostics.Add(Diagnostic.Create(descriptor, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
                        continue;
                    }
                }

                var conversion = ResolveConversion(method, explicitConfiguration, match.Member, targetMember, compilation, externalTypes, diagnostics, helpers, helperNames, options, sourcePathCaptureName);
                if (conversion == null)
                {
                    continue;
                }

                if (conversion.PotentiallyNull && TargetIsNonNullable(targetMember))
                {
                    if (options.IgnoreNullSourceMembers)
                    {
                        conversion = new ConversionModel(conversion.Expression, conversion.SourceMember, potentiallyNull: false, conversion.MemberPath, conversion.NullCheckExpression);
                    }
                    else if (IsCollectionType(GetMemberType(targetMember), compilation) && options.NullCollections == NullCollectionStrategyEmpty)
                    {
                        conversion = new ConversionModel(conversion.Expression, conversion.SourceMember, potentiallyNull: false, conversion.MemberPath, conversion.NullCheckExpression);
                    }
                    else
                    {
                        if (options.NullableMismatch == NullableMismatchPolicyError)
                        {
                            diagnostics.Add(Diagnostic.Create(IsCollectionType(GetMemberType(targetMember), compilation) ? Diagnostics.InvalidNullCollectionMapping : Diagnostics.NullableToNonNullable, targetMember.Locations.FirstOrDefault() ?? location, targetMember.Name));
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
                }

                if (conversion.SourceMember != null)
                {
                    usedSources.Add(conversion.SourceMember);
                }

                var guard = options.IgnoreNullSourceMembers ? sourcePathCaptureGuard ?? BuildPatchGuard(source.Name, explicitConfiguration, match.Member) : null;
                var initializeDuringConstruction = construction != null && IsRequired(targetMember) && !construction.SetsRequiredMembers;
                var initializationExpression = conversion.Expression;
                if (initializeDuringConstruction && sourcePathCaptureName != null)
                {
                    var initializationSource = selected != null
                        ? BuildNullSafeSourcePathExpression(source.Name, selected)
                        : EscapeIdentifier(source.Name) + "." + EscapeIdentifier(match.Member.Name);
                    var memberPath = selected?.Expression ?? match.Member.Name;
                    if (!IsMaybeNull(GetMemberType(targetMember)) || conversion.RequiresNonNullSourceExpression)
                    {
                        initializationSource = "(" + initializationSource + " ?? throw new global::System.InvalidOperationException(\"Source member path '" + memberPath + "' was null.\"))";
                    }

                    initializationExpression = conversion.Expression.Replace(sourcePathCaptureName, initializationSource);
                }
                assignments.Add(new AssignmentModel(targetMember.Name, conversion.Expression, guard, initializeDuringConstruction, initializationExpression: initializationExpression));
            }

            if (options.UnmappedSourceMembers != UnmappedMemberPolicyIgnore)
            {
                foreach (var sourceMember in sourceMembers.Where(m => !usedSources.Contains(m) && !ignoredSources.Contains(m.Name)).OrderBy(static m => m.Name, StringComparer.Ordinal))
                {
                    ReportUnmappedSource(sourceMember, options.UnmappedSourceMembers, diagnostics);
                }
            }

            return new MappingModel(method, sourceNullable, returnNullable: !method.ReturnsVoid && IsMaybeNull(method.ReturnType), options.NullableMismatch, options.ReferenceHandling, options.GuardNonNullSource, construction, preconditions.ToImmutable(), assignments.ToImmutable(), helpers.ToImmutable(), null, null, null, customBody: null, isUpdate: true, destinationParameter: destination, destinationNullable: destinationNullable);
        }

        private static IMethodSymbol? ResolveNestedUpdater(IMethodSymbol current, ITypeSymbol sourceType, ITypeSymbol targetType,
            Compilation compilation, INamedTypeSymbol[] externalTypes, ICollection<Diagnostic> diagnostics, ISymbol target, ExplicitMemberConfiguration? configuration)
        {
            var types = configuration?.ConverterType != null
                ? new[] { configuration.ConverterType }
                : new[] { current.ContainingType }.Concat(externalTypes);
            var classRegistrations = current.ContainingType.GetAttributes()
                .Where(static a => IsAttribute(a, UseMapperAttributeName))
                .Select(static a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as INamedTypeSymbol : null)
                .ToArray();
            var stages = types.GroupBy(type => SymbolEqualityComparer.Default.Equals(type, current.ContainingType) ? 0 :
                    classRegistrations.Any(t => SymbolEqualityComparer.Default.Equals(t, type)) ? 1 : 2)
                .OrderBy(static stage => stage.Key);
            foreach (var stage in stages)
            {
                var local = stage.Key == 0;
                var candidates = stage.SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
                    .Where(m => !SymbolEqualityComparer.Default.Equals(m, current) && m.MethodKind == MethodKind.Ordinary &&
                        m.Parameters.Length == 2 && !m.IsGenericMethod && !IsAsyncMethod(m) && !IsTaskLike(m.ReturnType) &&
                        (!current.IsStatic || m.IsStatic) && (local || m.IsStatic) &&
                        (configuration?.Use == null ? !local || IsPartialDeclaration(m) || HasAttribute(m, DefaultMappingAttributeName) : m.Name == configuration.Use) &&
                        compilation.IsSymbolAccessibleWithin(m, current.ContainingType) &&
                        m.Parameters.All(static p => p.RefKind == RefKind.None) &&
                        compilation.ClassifyConversion(sourceType, m.Parameters[0].Type).IsImplicit &&
                        SymbolEqualityComparer.Default.Equals(targetType, m.Parameters[1].Type) &&
                        (m.ReturnsVoid || SymbolEqualityComparer.Default.Equals(m.ReturnType, targetType)))
                    .OrderBy(static m => m.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal).ToArray();
                if (candidates.Length == 0)
                {
                    continue;
                }
                if (configuration?.Use != null)
                {
                    var identityCandidates = candidates.Where(m => compilation.ClassifyConversion(sourceType, m.Parameters[0].Type).IsIdentity).ToArray();
                    if (identityCandidates.Length > 0)
                    {
                        candidates = identityCandidates;
                    }
                }
                var defaults = candidates.Where(static m => HasAttribute(m, DefaultMappingAttributeName)).ToArray();
                if (defaults.Length > 1)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateDefaultMapping, target.Locations.FirstOrDefault(), target.Name));
                    return null;
                }
                if (defaults.Length == 1)
                {
                    return defaults[0];
                }
                if (candidates.Length != 1)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMapping, target.Locations.FirstOrDefault(), target.Name));
                    return null;
                }
                return candidates[0];
            }
            return null;
        }

        private static ImmutableArray<ExplicitMemberConfiguration> ParseExplicitConfigurations(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType, ISymbol[] sourceMembers, ICollection<Diagnostic> diagnostics)
        {
            var builder = ImmutableArray.CreateBuilder<ExplicitMemberConfiguration>();
            var seenTargets = new HashSet<string>(method.GetAttributes()
                .Where(static a => IsAttribute(a, IgnoreTargetAttributeName) || IsAttribute(a, UseTargetDefaultAttributeName))
                .Select(static a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
                .Where(static name => name != null).Cast<string>(), StringComparer.Ordinal);
            foreach (var attribute in method.GetAttributes().Where(static a => IsAttribute(a, MapPropertyAttributeName)))
            {
                var targetName = ReadStringNamedArgument(attribute, "Target");
                var sourcePath = ReadStringNamedArgument(attribute, "Source");
                var use = ReadStringNamedArgument(attribute, "Use");
                var converterType = ReadTypeNamedArgument(attribute, "ConverterType");
                var location = attribute.ApplicationSyntaxReference == null ? method.Locations.FirstOrDefault() : attribute.ApplicationSyntaxReference.GetSyntax().GetLocation();
                if (targetName != null && targetName.IndexOf('.') >= 0)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.TargetPathNotSupported, location, targetName));
                    continue;
                }

                if (targetName != null && HasIndexer(targetType, targetName))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.IndexerNotSupported, location, targetName));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(targetName) || !HasDirectMember(targetType, targetName!))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMethodConfiguration, location, targetName ?? "Target"));
                    continue;
                }

                if (!seenTargets.Add(targetName!))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateTargetMapping, location, targetName));
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
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMethodConfiguration, location, targetName));
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
                    diagnostics.Add(Diagnostic.Create(attributeName == UseTargetDefaultAttributeName ? Diagnostics.InvalidMethodConfiguration : Diagnostics.InvalidIgnoredMember, location, memberName ?? attributeName));
                    continue;
                }

                if (!names.Add(memberName))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateConfiguration, location, memberName));
                }
            }

            return names;
        }

        private static ConversionModel? ResolveConversion(IMethodSymbol mappingMethod, ExplicitMemberConfiguration? explicitConfiguration, ISymbol? sourceMember, ISymbol targetMember, Compilation compilation, INamedTypeSymbol[] externalTypes, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options, string? sourceExpressionOverride = null)
        {
            var parameterName = mappingMethod.Parameters[0].Name;
            var sourcePath = explicitConfiguration == null ? null : explicitConfiguration.SelectedSource;
            var escapedParameterName = EscapeIdentifier(parameterName);
            var expression = sourceExpressionOverride ?? (sourcePath == null
                ? escapedParameterName + "." + EscapeIdentifier(sourceMember!.Name)
                : BuildNullSafeSourcePathExpression(parameterName, sourcePath));
            var sourceType = sourcePath == null ? GetMemberType(sourceMember!) : sourcePath.Type;
            var sourceExpressionMayBeNull = sourcePath != null && SourcePathMayBeNull(sourcePath);
            var capturedNullableSourceExpression = sourceExpressionOverride != null && sourceExpressionMayBeNull;
            if (sourceExpressionOverride != null)
            {
                sourceType = RemoveNullableAnnotation(sourceType);
                sourceExpressionMayBeNull = false;
            }
            var targetType = GetMemberType(targetMember);
            var allowNonNullInputFallback = sourceExpressionMayBeNull ||
                options.IgnoreNullSourceMembers && sourceMember != null && SourceMayBeNull(sourceMember);
            var location = targetMember.Locations.FirstOrDefault() ?? mappingMethod.Locations.FirstOrDefault();
            if (ContainsUnsupportedSignatureType(sourceType) || ContainsUnsupportedSignatureType(targetType))
            {
                var unsupported = ContainsUnsupportedSignatureType(sourceType) ? sourceType : targetType;
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedGeneratedType, location, unsupported.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            if (explicitConfiguration != null && !string.IsNullOrWhiteSpace(explicitConfiguration.Use))
            {
                var explicitSourceType = sourcePath == null ? mappingMethod.Parameters[0].Type : sourceType;
                var explicitExpression = sourcePath == null ? escapedParameterName : expression;
                if (ReportDuplicateVisibleDefaults(mappingMethod, explicitSourceType, targetType, compilation, diagnostics, targetMember.Name, location, sourcePath != null && allowNonNullInputFallback))
                {
                    return null;
                }
                return ResolveNamedConverter(mappingMethod, explicitConfiguration.ConverterType, externalTypes, explicitConfiguration.Use!, explicitSourceType, targetType, explicitExpression, sourcePath == null ? null : sourcePath.SourceMember, compilation, diagnostics, location, targetMember.Name, sourceExpressionMayBeNull, sourcePath?.Expression);
            }

            if (ReportDuplicateVisibleDefaults(mappingMethod, sourceType, targetType, compilation, diagnostics, targetMember.Name, location, allowNonNullInputFallback))
            {
                return null;
            }

            if (sourcePath != null && !RequiresCollectionCopy(sourceType, targetType, compilation) && compilation.ClassifyConversion(sourceType, targetType).IsImplicit &&
                !(IsMaybeNull(sourceType) && !IsMaybeNull(targetType)) &&
                !(sourceExpressionMayBeNull && !IsMaybeNull(targetType)))
            {
                return new ConversionModel(expression, sourcePath.SourceMember, potentiallyNull: false, sourcePath.Expression, nullCheckExpression: null);
            }

            var converterDiagnosticCount = diagnostics.Count;
            var localMarked = ResolveConverterSet(mappingMethod, mappingMethod.ContainingType, static m => HasAttribute(m, MappingConverterAttributeName), sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location, sourceExpressionMayBeNull: sourceExpressionMayBeNull, sourceMemberPath: sourcePath?.Expression);
            if (localMarked != null || diagnostics.Count != converterDiagnosticCount)
            {
                return localMarked;
            }

            var named = ResolveConverterSet(mappingMethod, mappingMethod.ContainingType,
                m => !IsPartialDeclaration(m) && m.Name == "Map" + targetMember.Name,
                sourceType, targetType, expression, sourceMember, compilation, diagnostics,
                targetMember.Name, location, sourceExpressionMayBeNull: sourceExpressionMayBeNull,
                sourceMemberPath: sourcePath?.Expression);
            if (named != null || diagnostics.Count != converterDiagnosticCount)
            {
                return named;
            }

            var mappingDiagnosticCount = diagnostics.Count;
            var visibleMapping = ResolveVisibleMapping(mappingMethod, mappingMethod.ContainingType, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location, sourceExpressionMayBeNull: sourceExpressionMayBeNull, sourceMemberPath: sourcePath?.Expression);
            if (visibleMapping != null || diagnostics.Count != mappingDiagnosticCount)
            {
                return visibleMapping;
            }

            var externalConverter = ResolveExternalConversion(mappingMethod, externalTypes, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location, mappingStage: false, sourceExpressionMayBeNull: sourceExpressionMayBeNull, sourceMemberPath: sourcePath?.Expression);
            if (externalConverter != null || diagnostics.Count != mappingDiagnosticCount)
            {
                return externalConverter;
            }

            var externalMapping = ResolveExternalConversion(mappingMethod, externalTypes, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetMember.Name, location, mappingStage: true, sourceExpressionMayBeNull: sourceExpressionMayBeNull, sourceMemberPath: sourcePath?.Expression);
            if (externalMapping != null || diagnostics.Count != mappingDiagnosticCount)
            {
                return externalMapping;
            }

            var nullableSourcePathToNonNullableTarget = sourceExpressionMayBeNull &&
                !IsMaybeNull(sourceType) && !IsCollectionType(targetType, compilation) && !IsMaybeNull(targetType) &&
                !options.IgnoreNullSourceMembers;
            if (nullableSourcePathToNonNullableTarget)
            {
                if (options.NullableMismatch == NullableMismatchPolicyError)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, location, sourcePath!.Expression));
                    return null;
                }

                expression = "(" + expression + " ?? throw new global::System.InvalidOperationException(\"Source member path '" +
                    sourcePath!.Expression + "' was null.\"))";
                sourceExpressionMayBeNull = false;
            }

            if (IsTupleBoundary(sourceType, targetType))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ConversionNotFound, location,
                    sourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            var languageDiagnosticCount = diagnostics.Count;
            var languageConversion = RequiresCollectionCopy(sourceType, targetType, compilation) ? null :
                ResolveLanguageConversion(sourceType, targetType, expression, compilation, diagnostics, location, options, memberPath: sourcePath?.Expression ?? targetMember.Name);
            if (languageConversion != null)
            {
                var handledNullableSource = IsMaybeNull(sourceType) && !IsMaybeNull(targetType);
                return new ConversionModel(languageConversion, sourceMember,
                    !handledNullableSource && sourceMember != null && SourceMayBeNull(sourceMember),
                    sourcePath?.Expression ?? (sourceMember == null ? string.Empty : sourceMember.Name),
                    nullCheckExpression: null);
            }

            if (diagnostics.Count != languageDiagnosticCount)
            {
                return null;
            }

            var enumMapping = ResolveEnumMapping(mappingMethod, sourceType, targetType, expression, sourceMember, sourcePath?.Expression ?? targetMember.Name, diagnostics, location, options);
            if (enumMapping != null || diagnostics.Count != languageDiagnosticCount)
            {
                return enumMapping;
            }

            var collection = ResolveCollectionMapping(mappingMethod, sourceType, targetType, expression, sourceMember, targetMember, compilation, diagnostics, helpers, helperNames, options, sourceExpressionMayBeNull);
            if (collection != null || diagnostics.Count != languageDiagnosticCount)
            {
                return collection;
            }

            var nested = ResolveNestedMapping(mappingMethod, sourceType, targetType, expression, sourceMember, targetMember, compilation, diagnostics, helpers, helperNames, options, sourceExpressionMayBeNull, sourcePath?.Expression, capturedNullableSourceExpression);
            if (nested != null)
            {
                return nested;
            }

            diagnostics.Add(Diagnostic.Create(Diagnostics.ConversionNotFound, location, sourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return null;
        }

        private static ConversionModel? ResolveTopLevelConversion(IMethodSymbol mappingMethod, ITypeSymbol sourceType, ITypeSymbol targetType, Compilation compilation, INamedTypeSymbol[] externalTypes, ICollection<Diagnostic> diagnostics, EffectiveMappingOptions options)
        {
            var location = mappingMethod.Locations.FirstOrDefault();
            if (ReportDuplicateVisibleDefaults(mappingMethod, sourceType, targetType, compilation, diagnostics, mappingMethod.Name, location))
            {
                return null;
            }
            var expression = EscapeIdentifier(mappingMethod.Parameters[0].Name);
            var diagnosticCount = diagnostics.Count;
            var localMarked = ResolveConverterSet(mappingMethod, mappingMethod.ContainingType, static m => HasAttribute(m, MappingConverterAttributeName), sourceType, targetType, expression, null, compilation, diagnostics, mappingMethod.Name, location);
            if (localMarked != null || diagnostics.Count != diagnosticCount)
            {
                return localMarked;
            }

            var visibleMapping = ResolveVisibleMapping(mappingMethod, mappingMethod.ContainingType, sourceType, targetType, expression, null, compilation, diagnostics, mappingMethod.Name, location, includePartialDeclarations: false);
            if (visibleMapping != null || diagnostics.Count != diagnosticCount)
            {
                return visibleMapping;
            }

            var externalConverter = ResolveExternalConversion(mappingMethod, externalTypes, sourceType, targetType, expression, null, compilation, diagnostics, mappingMethod.Name, location, mappingStage: false);
            if (externalConverter != null || diagnostics.Count != diagnosticCount)
            {
                return externalConverter;
            }

            var externalMapping = ResolveExternalConversion(mappingMethod, externalTypes, sourceType, targetType, expression, null, compilation, diagnostics, mappingMethod.Name, location, mappingStage: true);
            if (externalMapping != null || diagnostics.Count != diagnosticCount)
            {
                return externalMapping;
            }

            var languageConversion = RequiresCollectionCopy(sourceType, targetType, compilation) ? null :
                ResolveLanguageConversion(sourceType, targetType, expression, compilation, diagnostics, location, options, isRoot: true);
            if (languageConversion != null)
            {
                return new ConversionModel(languageConversion, null, potentiallyNull: false, mappingMethod.Name, nullCheckExpression: null);
            }

            return ResolveEnumMapping(mappingMethod, sourceType, targetType, expression, null, mappingMethod.Name, diagnostics, location, options, isRoot: true);
        }

        private static bool RequiresCollectionCopy(ITypeSymbol sourceType, ITypeSymbol targetType, Compilation compilation)
        {
            return GetCollectionShape(sourceType, compilation) != null && GetCollectionShape(targetType, compilation) != null;
        }

        private static string? ResolveLanguageConversion(ITypeSymbol sourceType, ITypeSymbol targetType, string expression, Compilation compilation, ICollection<Diagnostic> diagnostics, Location? location, EffectiveMappingOptions options, bool isRoot = false, bool isElement = false, string? memberPath = null)
        {
            var nullableValueSource = sourceType as INamedTypeSymbol;
            var isNullableValueSource = nullableValueSource != null && nullableValueSource.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var isNullableReferenceSource = !sourceType.IsValueType && sourceType.NullableAnnotation == NullableAnnotation.Annotated;
            if ((isNullableValueSource || isNullableReferenceSource) &&
                !IsMaybeNull(targetType) && (targetType.IsValueType || targetType.NullableAnnotation == NullableAnnotation.NotAnnotated))
            {
                if (options.NullableMismatch == NullableMismatchPolicyError && !(options.IgnoreNullSourceMembers && !isRoot && !isElement))
                {
                    diagnostics.Add(Diagnostic.Create(isElement ? Diagnostics.NullableElementMismatch : Diagnostics.NullableToNonNullable, location, memberPath ?? expression));
                    return null;
                }

                var exception = isRoot
                    ? "new global::System.ArgumentNullException(nameof(" + expression + "))"
                    : "new global::System.InvalidOperationException(\"Source member" +
                        (memberPath != null && memberPath.IndexOf('.') >= 0 ? " path" : string.Empty) + " '" +
                        (memberPath ?? expression) + "' was null.\")";
                expression = "(" + expression + " ?? throw " + exception + ")";
                sourceType = isNullableValueSource
                    ? nullableValueSource!.TypeArguments[0]
                    : sourceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            }

            var conversion = compilation.ClassifyConversion(sourceType, targetType);
            DiagnosticDescriptor? failure = null;
            if (conversion.IsUserDefined && conversion.MethodSymbol == null)
            {
                failure = Diagnostics.AmbiguousConversion;
            }
            else if (conversion.IsImplicit)
            {
                return expression;
            }
            else if (conversion.IsUserDefined)
            {
                if (options.AllowExplicitOperators)
                {
                    return "(" + DisplayType(targetType) + ")(" + expression + ")";
                }

                failure = Diagnostics.ExplicitOperatorDisabled;
            }
            else if (conversion.Exists && IsNumericType(sourceType) && IsNumericType(targetType))
            {
                if (options.NumericConversion != "ImplicitOnly")
                {
                    var context = options.NumericConversion == "Unchecked" ? "unchecked" : "checked";
                    return context + "((" + DisplayType(targetType) + ")(" + expression + "))";
                }

                failure = Diagnostics.NarrowingNumericConversionDisabled;
            }

            if (failure != null)
            {
                diagnostics.Add(Diagnostic.Create(failure, location, sourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            return null;
        }

        private static bool IsNumericType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                type = named.TypeArguments[0];
            }

            return type.SpecialType == SpecialType.System_SByte || type.SpecialType == SpecialType.System_Byte ||
                type.SpecialType == SpecialType.System_Int16 || type.SpecialType == SpecialType.System_UInt16 ||
                type.SpecialType == SpecialType.System_Int32 || type.SpecialType == SpecialType.System_UInt32 ||
                type.SpecialType == SpecialType.System_Int64 || type.SpecialType == SpecialType.System_UInt64 ||
                type.SpecialType == SpecialType.System_Char || type.SpecialType == SpecialType.System_Single ||
                type.SpecialType == SpecialType.System_Double || type.SpecialType == SpecialType.System_Decimal ||
                type.SpecialType == SpecialType.System_IntPtr || type.SpecialType == SpecialType.System_UIntPtr;
        }

        private static ConversionModel? ResolveEnumMapping(IMethodSymbol mappingMethod, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, string targetName, ICollection<Diagnostic> diagnostics, Location? location, EffectiveMappingOptions options, bool isRoot = false, bool isElement = false)
        {
            var nullableSource = sourceType is INamedTypeSymbol sourceValue && sourceValue.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var nullableTarget = targetType is INamedTypeSymbol targetValue && targetValue.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            if (nullableSource || nullableTarget)
            {
                var underlyingSource = nullableSource ? ((INamedTypeSymbol)sourceType).TypeArguments[0] : sourceType;
                var underlyingTarget = nullableTarget ? ((INamedTypeSymbol)targetType).TypeArguments[0] : targetType;
                if (underlyingSource.TypeKind != TypeKind.Enum || underlyingTarget.TypeKind != TypeKind.Enum)
                {
                    return null;
                }

                if (nullableSource && !nullableTarget && options.NullableMismatch == NullableMismatchPolicyError && !(options.IgnoreNullSourceMembers && !isRoot && !isElement))
                {
                    diagnostics.Add(Diagnostic.Create(isElement ? Diagnostics.NullableElementMismatch : Diagnostics.NullableToNonNullable, location, targetName));
                    return null;
                }

                var valueName = "__nullableEnumValue";
                while (mappingMethod.Parameters.Any(parameter => parameter.Name == valueName))
                {
                    valueName += "_";
                }

                var underlyingMapping = ResolveEnumMapping(mappingMethod, underlyingSource, underlyingTarget, nullableSource ? valueName : expression, sourceMember, targetName, diagnostics, location, options);
                if (underlyingMapping == null)
                {
                    return null;
                }

                var mappedExpression = nullableTarget ? "(" + DisplayType(targetType) + ")(" + underlyingMapping.Expression + ")" : underlyingMapping.Expression;
                if (nullableSource)
                {
                    var nullResult = nullableTarget ? "(" + DisplayType(targetType) + ")null"
                        : isRoot ? "throw new global::System.ArgumentNullException(nameof(" + expression + "))"
                        : "throw new global::System.InvalidOperationException(\"Source member '" + (isElement ? expression : targetName) + "' was null.\")";
                    mappedExpression = expression + " switch { " + DisplayType(underlyingSource) + " " + valueName + " => " + mappedExpression + ", _ => " + nullResult + " }";
                }

                return new ConversionModel(mappedExpression, sourceMember, potentiallyNull: nullableSource && nullableTarget, targetName, nullCheckExpression: null);
            }

            if (!(sourceType is INamedTypeSymbol sourceEnum) || !(targetType is INamedTypeSymbol targetEnum) ||
                sourceEnum.TypeKind != TypeKind.Enum || targetEnum.TypeKind != TypeKind.Enum)
            {
                return null;
            }

            if (options.EnumMapping == EnumMappingStrategyByValue)
            {
                if (options.EnumNumericConversion != EnumNumericConversionUnchecked)
                {
                    ReportEnumValueOverflow(sourceEnum, targetEnum, diagnostics, location);
                }
                return new ConversionModel(EnumUnderlyingConversion(expression, sourceEnum, targetEnum, options), sourceMember, potentiallyNull: false, targetName, nullCheckExpression: null);
            }

            var members = GetEnumMembers(sourceEnum);
            var targetByName = GetEnumMembers(targetEnum).GroupBy(static m => m.SourceName, StringComparer.Ordinal).ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);
            var sourceBitMask = EnumBitMask(sourceEnum.EnumUnderlyingType!);
            var mappedAtomicMask = members.Where(m => IsPowerOfTwo(m.Value & sourceBitMask) && targetByName.ContainsKey(m.SourceName))
                .Aggregate(0UL, (mask, member) => mask | (member.Value & sourceBitMask));
            var mapped = new Dictionary<ulong, EnumMemberModel>();
            var hasError = false;
            foreach (var member in members.OrderBy(static m => m.SourceName, StringComparer.Ordinal))
            {
                if (!targetByName.TryGetValue(member.SourceName, out var target))
                {
                    var memberBits = member.Value & sourceBitMask;
                    if (IsFlags(sourceEnum) && memberBits != 0 && !IsPowerOfTwo(memberBits) && (memberBits & ~mappedAtomicMask) == 0)
                    {
                        continue;
                    }

                    if (member.Value == 0)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.EnumZeroMemberNotMapped, location, member.SourceName));
                    }
                    else if (IsFlags(sourceEnum) && IsPowerOfTwo(memberBits))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.EnumFlagNotMapped, location, member.SourceName));
                    }
                    else if (options.UnmatchedEnumValues == UnmatchedEnumValuePolicyError)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.EnumMemberNotMapped, location, member.SourceName));
                    }

                    if (options.UnmatchedEnumValues == UnmatchedEnumValuePolicyByValue &&
                        (options.EnumNumericConversion == EnumNumericConversionUnchecked || EnumValueFits(member.Value, sourceEnum, targetEnum)))
                    {
                        var targetBitMask = EnumBitMask(targetEnum.EnumUnderlyingType!);
                        var convertedTargetValue = member.Value & targetBitMask;
                        if (IsSignedIntegral(targetEnum.EnumUnderlyingType!) && (convertedTargetValue & ((targetBitMask >> 1) + 1)) != 0)
                        {
                            convertedTargetValue |= ~targetBitMask;
                        }
                        if (mapped.TryGetValue(member.Value, out var existingAlias) && existingAlias.TargetValue != convertedTargetValue)
                        {
                            diagnostics.Add(Diagnostic.Create(Diagnostics.EnumAliasConflict, location, member.Value.ToString(CultureInfo.InvariantCulture)));
                            hasError = true;
                        }
                        else
                        {
                            mapped[member.Value] = new EnumMemberModel(member.SourceName, member.Value, EnumUnderlyingConversion(DisplayType(sourceEnum) + "." + EscapeIdentifier(member.SourceName), sourceEnum, targetEnum, options), convertedTargetValue, targetIsExpression: true);
                        }
                    }
                    else if (options.UnmatchedEnumValues == UnmatchedEnumValuePolicyByValue)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.EnumValueOverflow, location, member.SourceName));
                        hasError = true;
                    }
                    else if (options.UnmatchedEnumValues == UnmatchedEnumValuePolicyError || member.Value == 0 || IsFlags(sourceEnum) && IsPowerOfTwo(memberBits))
                    {
                        hasError = true;
                    }

                    continue;
                }

                var compositeBits = member.Value & sourceBitMask;
                if (IsFlags(sourceEnum) && compositeBits != 0 && !IsPowerOfTwo(compositeBits))
                {
                    var expectedTargetValue = members
                        .Where(atomic => IsPowerOfTwo(atomic.Value & sourceBitMask) &&
                            (compositeBits & (atomic.Value & sourceBitMask)) != 0 && targetByName.ContainsKey(atomic.SourceName))
                        .Aggregate(0UL, (value, atomic) => value | targetByName[atomic.SourceName].Value);
                    if (target.Value != expectedTargetValue)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.EnumAliasConflict, location, member.Value.ToString(CultureInfo.InvariantCulture)));
                        hasError = true;
                        continue;
                    }
                }

                if (mapped.TryGetValue(member.Value, out var existing) && existing.TargetValue != target.Value)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.EnumAliasConflict, location, member.Value.ToString(CultureInfo.InvariantCulture)));
                    hasError = true;
                    continue;
                }

                mapped[member.Value] = new EnumMemberModel(member.SourceName, member.Value, target.SourceName, target.Value);
            }

            if (hasError)
            {
                return null;
            }

            var arms = mapped.Values
                .GroupBy(static m => m.Value)
                .Select(static g => g.OrderBy(static m => m.SourceName, StringComparer.Ordinal).First())
                .OrderBy(static m => m.Value)
                .Select(m => DisplayType(sourceEnum) + "." + EscapeIdentifier(m.SourceName) + " => " +
                    (m.TargetIsExpression ? m.TargetName : DisplayType(targetEnum) + "." + EscapeIdentifier(m.TargetName)));
            var enumValueName = "enumValue";
            while (mappingMethod.Parameters.Any(parameter => parameter.Name == enumValueName))
            {
                enumValueName += "_";
            }
            if (IsFlags(sourceEnum))
            {
                var flagsArm = CreateFlagsCompositeArm(sourceEnum, targetEnum, enumValueName, mapped.Values);
                if (flagsArm != null)
                {
                    arms = arms.Concat(new[] { flagsArm });
                }
            }

            var captureValue = !(SyntaxFactory.ParseExpression(expression) is IdentifierNameSyntax);
            arms = arms.Concat(new[] { (captureValue ? "var " + enumValueName : "_") + " => throw new global::System.ArgumentOutOfRangeException(nameof(" + expression + "), " + (captureValue ? enumValueName : expression) + ", \"Unmapped enum value.\")" });
            return new ConversionModel(expression + " switch\n            {\n                " + string.Join(",\n                ", arms) + "\n            }", sourceMember, potentiallyNull: false, targetName, nullCheckExpression: null);
        }

        private static string? CreateFlagsCompositeArm(INamedTypeSymbol sourceEnum, INamedTypeSymbol targetEnum, string valueName, IEnumerable<EnumMemberModel> mappedMembers)
        {
            var sourceBitMask = EnumBitMask(sourceEnum.EnumUnderlyingType!);
            var atomics = mappedMembers
                .Where(m => IsPowerOfTwo(m.Value & sourceBitMask) && !m.TargetIsExpression)
                .GroupBy(static m => m.Value)
                .Select(static g => g.First())
                .OrderBy(static m => m.Value)
                .ToArray();
            if (atomics.Length == 0)
            {
                return null;
            }

            var mask = atomics.Aggregate(0UL, (current, item) => current | (item.Value & sourceBitMask));
            var bits = "(unchecked((ulong)" + valueName + ") & " + sourceBitMask.ToString(CultureInfo.InvariantCulture) + "UL)";
            var terms = atomics.Select(m => "((" + bits + " & " + (m.Value & sourceBitMask).ToString(CultureInfo.InvariantCulture) + "UL) != 0 ? " + DisplayType(targetEnum) + "." + EscapeIdentifier(m.TargetName) + " : (" + DisplayType(targetEnum) + ")0)");
            return "var " + valueName + " when (" + bits + " & ~" + mask.ToString(CultureInfo.InvariantCulture) + "UL) == 0UL => " + string.Join(" | ", terms);
        }

        private static ulong EnumBitMask(ITypeSymbol underlyingType)
        {
            switch (underlyingType.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                    return byte.MaxValue;
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                    return ushort.MaxValue;
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                    return uint.MaxValue;
                default:
                    return ulong.MaxValue;
            }
        }

        private static string EnumUnderlyingConversion(string expression, INamedTypeSymbol sourceEnum, INamedTypeSymbol targetEnum, EffectiveMappingOptions options)
        {
            var inner = "(" + DisplayType(targetEnum) + ")" + expression;
            return options.EnumNumericConversion == EnumNumericConversionUnchecked ? "unchecked(" + inner + ")" : "checked(" + inner + ")";
        }

        private static void ReportEnumValueOverflow(INamedTypeSymbol sourceEnum, INamedTypeSymbol targetEnum, ICollection<Diagnostic> diagnostics, Location? location)
        {
            foreach (var member in GetEnumMembers(sourceEnum))
            {
                if (!EnumValueFits(member.Value, sourceEnum, targetEnum))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.EnumValueOverflow, location, member.SourceName));
                }
            }
        }

        private static bool EnumValueFits(ulong value, INamedTypeSymbol sourceEnum, INamedTypeSymbol targetEnum)
        {
            var signedSource = IsSignedIntegral(sourceEnum.EnumUnderlyingType!);
            var signedTarget = IsSignedIntegral(targetEnum.EnumUnderlyingType!);
            if (signedSource)
            {
                var signed = unchecked((long)value);
                return signedTarget
                    ? signed >= MinSigned(targetEnum.EnumUnderlyingType!) && signed <= MaxSigned(targetEnum.EnumUnderlyingType!)
                    : signed >= 0 && (ulong)signed <= MaxUnsigned(targetEnum.EnumUnderlyingType!);
            }

            return signedTarget
                ? value <= (ulong)MaxSigned(targetEnum.EnumUnderlyingType!)
                : value <= MaxUnsigned(targetEnum.EnumUnderlyingType!);
        }

        private static bool IsSignedIntegral(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_SByte ||
                type.SpecialType == SpecialType.System_Int16 ||
                type.SpecialType == SpecialType.System_Int32 ||
                type.SpecialType == SpecialType.System_Int64;
        }

        private static long MinSigned(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                    return sbyte.MinValue;
                case SpecialType.System_Int16:
                    return short.MinValue;
                case SpecialType.System_Int32:
                    return int.MinValue;
                default:
                    return long.MinValue;
            }
        }

        private static long MaxSigned(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                    return sbyte.MaxValue;
                case SpecialType.System_Int16:
                    return short.MaxValue;
                case SpecialType.System_Int32:
                    return int.MaxValue;
                default:
                    return long.MaxValue;
            }
        }

        private static ulong MaxUnsigned(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte:
                    return byte.MaxValue;
                case SpecialType.System_UInt16:
                    return ushort.MaxValue;
                case SpecialType.System_UInt32:
                    return uint.MaxValue;
                default:
                    return ulong.MaxValue;
            }
        }

        private static bool IsFlags(INamedTypeSymbol enumType)
        {
            return enumType.GetAttributes().Any(static a => a.AttributeClass != null && a.AttributeClass.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.FlagsAttribute");
        }

        private static bool IsPowerOfTwo(ulong value)
        {
            return value != 0 && (value & (value - 1)) == 0;
        }

        private static EnumMemberModel[] GetEnumMembers(INamedTypeSymbol enumType)
        {
            return enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(static f => f.HasConstantValue)
                .Select(static f => new EnumMemberModel(f.Name, ConvertEnumConstant(f.ConstantValue), f.Name, ConvertEnumConstant(f.ConstantValue)))
                .OrderBy(static f => f.SourceName, StringComparer.Ordinal)
                .ToArray();
        }

        private static ulong ConvertEnumConstant(object? value)
        {
            switch (value)
            {
                case sbyte v:
                    return unchecked((ulong)v);
                case short v:
                    return unchecked((ulong)v);
                case int v:
                    return unchecked((ulong)v);
                case long v:
                    return unchecked((ulong)v);
                case byte v:
                    return v;
                case ushort v:
                    return v;
                case uint v:
                    return v;
                case ulong v:
                    return v;
                default:
                    return 0;
            }
        }

        private static bool ReportDuplicateVisibleDefaults(IMethodSymbol mappingMethod, ITypeSymbol sourceType, ITypeSymbol targetType, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location, bool allowNonNullInputFallback = false)
        {
            var nonNullSourceType = allowNonNullInputFallback ? RemoveNullableAnnotation(sourceType) : sourceType;
            var candidates = new[] { mappingMethod.ContainingType }.Concat(GetRegisteredMapperTypes(mappingMethod.ContainingType, compilation))
                .SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .Where(method => HasAttribute(method, DefaultMappingAttributeName) && method.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(method.ReturnType, targetType) &&
                    compilation.IsSymbolAccessibleWithin(method, mappingMethod.ContainingType))
                .ToArray();
            var options = EffectiveOptions(mappingMethod);
            var exactDefaults = candidates.Count(method => SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, sourceType) &&
                IsUsableConverter(method, method.ContainingType, sourceType, targetType, compilation, options));
            var fallbackDefaults = allowNonNullInputFallback
                ? candidates.Count(method => SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, nonNullSourceType) &&
                    IsUsableConverter(method, method.ContainingType, nonNullSourceType, targetType, compilation, options))
                : 0;
            var defaults = Math.Max(exactDefaults, fallbackDefaults);
            if (defaults < 2)
            {
                return false;
            }

            diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateDefaultMapping, location, targetName));
            return true;
        }

        private static ConversionModel? ResolveVisibleMapping(IMethodSymbol currentMethod, INamedTypeSymbol mapperType, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location, bool includePartialDeclarations = true, bool sourceExpressionMayBeNull = false, string? sourceMemberPath = null)
        {
            var options = EffectiveOptions(currentMethod);
            var rawDirectSourceMayBeNull = sourceMember != null && SourceMayBeNull(sourceMember);
            var allowNonNullInputFallback = sourceExpressionMayBeNull || options.IgnoreNullSourceMembers && rawDirectSourceMayBeNull;
            if (ReportUnusableDefault(mapperType.GetMembers().OfType<IMethodSymbol>().Where(m => !SymbolEqualityComparer.Default.Equals(m, currentMethod)), currentMethod, sourceType, targetType, compilation, diagnostics, targetName, location))
            {
                return null;
            }

            var candidates = mapperType.GetMembers().OfType<IMethodSymbol>()
                .Where(m => !SymbolEqualityComparer.Default.Equals(m, currentMethod) && !m.IsImplicitlyDeclared &&
                    (includePartialDeclarations || !IsPartialDeclaration(m)) &&
                    !HasAttribute(m, MappingConverterAttributeName) &&
                    (HasAttribute(m, DefaultMappingAttributeName) || IsPartialDeclaration(m) && IsNewObjectMappingCandidate(m)) &&
                    GetConverterInputRank(m, mapperType, sourceType, targetType, compilation, options, allowNonNullInputFallback) != int.MaxValue)
                .Select(m => new { Method = m, Rank = GetConverterInputRank(m, mapperType, sourceType, targetType, compilation, options, allowNonNullInputFallback) })
                .OrderBy(static candidate => candidate.Rank)
                .ThenBy(static candidate => candidate.Method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }

            var bestRank = candidates[0].Rank;
            candidates = candidates.Where(candidate => candidate.Rank == bestRank).ToArray();
            var defaults = candidates.Where(static candidate => HasAttribute(candidate.Method, DefaultMappingAttributeName)).ToArray();
            if (defaults.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateDefaultMapping, location, targetName));
                return null;
            }

            if (defaults.Length == 1)
            {
                candidates = defaults;
            }

            if (candidates.Length != 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMapping, location, targetName));
                return null;
            }

            var directSourceMayBeNull = rawDirectSourceMayBeNull && !options.IgnoreNullSourceMembers;
            if (!sourceExpressionMayBeNull && directSourceMayBeNull &&
                !IsMaybeNull(candidates[0].Method.Parameters[0].Type) && IsMaybeNull(targetType))
            {
                var captureName = CreateExpressionCaptureName(currentMethod, targetName, expression);
                var mapped = CreateConverterResult(currentMethod, candidates[0].Method, targetType,
                    EscapeIdentifier(candidates[0].Method.Name) + "(" + captureName + ")", sourceMember,
                    compilation, diagnostics, targetName, location);
                return mapped == null
                    ? null
                    : new ConversionModel(expression + " is { } " + captureName + " ? " + mapped.Expression + " : null",
                        sourceMember, potentiallyNull: false, sourceMemberPath ?? targetName, nullCheckExpression: null);
            }

            var converterInput = ApplyConverterInputNullability(currentMethod, candidates[0].Method, expression,
                sourceExpressionMayBeNull || directSourceMayBeNull, sourceMemberPath ?? targetName, diagnostics, location);
            return converterInput == null ? null : CreateConverterResult(currentMethod, candidates[0].Method, targetType, EscapeIdentifier(candidates[0].Method.Name) + "(" + converterInput + ")", sourceMember, compilation, diagnostics, targetName, location);
        }

        private static ConversionModel? ResolveNestedMapping(IMethodSymbol mappingMethod, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, ISymbol targetMember, Compilation compilation, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options, bool sourceExpressionMayBeNull = false, string? sourceMemberPath = null, bool capturedNullableSourceExpression = false)
        {
            var location = targetMember.Locations.FirstOrDefault() ?? mappingMethod.Locations.FirstOrDefault();
            if (sourceType.IsRefLikeType || targetType.IsRefLikeType)
            {
                var unsupported = sourceType.IsRefLikeType ? sourceType : targetType;
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedGeneratedType, location, unsupported.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }
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

            var helperName = CreateHelperName("MapNested_", namedSource, namedTarget);
            if (helperNames.Add(helperName))
            {
                var helper = CreateNestedMappingModel(mappingMethod, namedSource, namedTarget, helperName, compilation, diagnostics, helpers, helperNames, options);
                if (helper == null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.StructuralNestedMappingFailed, location, namedSource.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), namedTarget.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                    return null;
                }

                helpers.Add(helper);
            }

            var potentiallyNull = sourceExpressionMayBeNull || capturedNullableSourceExpression || sourceMember != null && SourceMayBeNull(sourceMember);
            if (potentiallyNull && !TargetIsNonNullable(targetMember))
            {
                var captureName = CreateExpressionCaptureName(mappingMethod, targetMember.Name, expression);
                return new ConversionModel(expression + " is { } " + captureName + " ? " + helperName + "(" + captureName + ") : null", sourceMember, potentiallyNull: false, targetMember.Name, nullCheckExpression: null);
            }

            if (potentiallyNull && !options.IgnoreNullSourceMembers && options.NullableMismatch == NullableMismatchPolicyThrow)
            {
                var memberPath = sourceMemberPath ?? sourceMember?.Name ?? targetMember.Name;
                var nonNullExpression = "(" + expression + " ?? throw new global::System.InvalidOperationException(\"Source member path '" + memberPath + "' was null.\"))";
                return new ConversionModel(helperName + "(" + nonNullExpression + ")", sourceMember, potentiallyNull: false, memberPath, nullCheckExpression: null);
            }

            if (potentiallyNull && options.NullCollections == NullCollectionStrategyEmpty)
            {
                potentiallyNull = false;
            }

            return new ConversionModel(helperName + "(" + expression + ")", sourceMember, potentiallyNull, targetMember.Name, nullCheckExpression: null);
        }

        private static ConversionModel? ResolveCollectionMapping(IMethodSymbol mappingMethod, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, ISymbol targetMember, Compilation compilation, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options, bool sourceExpressionMayBeNull = false)
        {
            var sourceShape = GetCollectionShape(sourceType, compilation);
            var targetShape = GetCollectionShape(targetType, compilation);
            if (sourceShape == null && targetShape == null)
            {
                return null;
            }

            var location = targetMember.Locations.FirstOrDefault() ?? mappingMethod.Locations.FirstOrDefault();
            if (sourceShape == null || targetShape == null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedCollectionShape, location));
                return null;
            }

            if (sourceShape.IsRectangularArray || targetShape.IsRectangularArray)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.RectangularArrayNotSupported, location));
                return null;
            }

            if (sourceShape.Unsupported || targetShape.Unsupported || targetShape.Custom)
            {
                diagnostics.Add(Diagnostic.Create(sourceShape.Custom || targetShape.Custom ? Diagnostics.CustomCollectionNotSupported : Diagnostics.UnsupportedCollectionShape, location));
                return null;
            }

            var targetMaybeNull = IsMaybeNull(targetType);
            var rejectsNullableSource = options.NullCollections == NullCollectionStrategyError &&
                (options.NullCollectionsExplicit || !targetMaybeNull);
            var cannotPreserveNull = options.NullCollections == NullCollectionStrategyPreserve && !targetMaybeNull;
            if (sourceExpressionMayBeNull && !options.IgnoreNullSourceMembers && (rejectsNullableSource || cannotPreserveNull))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidNullCollectionMapping, location, targetMember.Name));
                return null;
            }

            var helperName = CreateHelperName(
                sourceShape.IsDictionary || targetShape.IsDictionary ? "MapDictionary_" : "MapCollection_",
                sourceType,
                targetType);
            if (helperNames.Add(helperName))
            {
                var helper = CreateCollectionMappingModel(mappingMethod, sourceType, targetType, mappingMethod.Parameters[0].Name, targetMember, compilation, diagnostics, helpers, helperNames, options, helperName);
                if (helper == null)
                {
                    return null;
                }

                helpers.Add(helper);
            }

            var potentiallyNull = options.NullCollections != NullCollectionStrategyEmpty &&
                (sourceExpressionMayBeNull || sourceMember != null && SourceMayBeNull(sourceMember));
            return new ConversionModel(helperName + "(" + expression + ")", sourceMember, potentiallyNull, targetMember.Name, nullCheckExpression: null);
        }

        private static MappingModel? CreateCollectionMappingModel(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType, string parameterName, ISymbol? targetMember, Compilation compilation, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder outerHelpers, HashSet<string> outerHelperNames, EffectiveMappingOptions options, string? helperName)
        {
            var location = targetMember == null ? method.Locations.FirstOrDefault() : targetMember.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault();
            var sourceShape = GetCollectionShape(sourceType, compilation);
            var targetShape = GetCollectionShape(targetType, compilation, forDestinationConstruction: true);
            if (sourceShape == null || targetShape == null)
            {
                return null;
            }

            var sourceMaybeNull = IsMaybeNull(sourceType);
            var targetMaybeNull = IsMaybeNull(targetType);
            var rejectsNullableSource = options.NullCollections == NullCollectionStrategyError &&
                (options.NullCollectionsExplicit || !targetMaybeNull);
            var cannotPreserveNull = options.NullCollections == NullCollectionStrategyPreserve && !targetMaybeNull;
            if (sourceMaybeNull && !options.IgnoreNullSourceMembers && (rejectsNullableSource || cannotPreserveNull))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidNullCollectionMapping, location, targetMember?.Name ?? method.Name));
                return null;
            }

            if (sourceShape.IsRectangularArray || targetShape.IsRectangularArray)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.RectangularArrayNotSupported, location));
                return null;
            }

            if (sourceShape.Unsupported || targetShape.Unsupported || targetShape.Custom)
            {
                diagnostics.Add(Diagnostic.Create(sourceShape.Custom || targetShape.Custom ? Diagnostics.CustomCollectionNotSupported : Diagnostics.UnsupportedCollectionShape, location));
                return null;
            }

            var helpers = ImmutableArray.CreateBuilder<MappingModel>();
            var helperNames = outerHelperNames;
            var body = RenderCollectionBody(method, sourceShape, targetShape, sourceType, targetType, parameterName, compilation, diagnostics, helpers, helperNames, options, location);
            if (body == null)
            {
                return null;
            }

            var sourceNullable = helperName == null && IsMaybeNull(method.Parameters[0]) && options.NullCollections != NullCollectionStrategyEmpty;
            var returnNullable = helperName == null && IsMaybeNull(method.ReturnType);
            return new MappingModel(method, sourceNullable, returnNullable, options.NullableMismatch, options.ReferenceHandling, helperName == null && !IsMaybeNull(method.Parameters[0]) && options.GuardNonNullSource, null, ImmutableArray<PreconditionModel>.Empty, ImmutableArray<AssignmentModel>.Empty, helpers.ToImmutable(), helperName, helperName == null ? null : sourceType, helperName == null ? null : targetType, body);
        }

        private static string? RenderCollectionBody(IMethodSymbol method, CollectionShape sourceShape, CollectionShape targetShape, ITypeSymbol sourceType, ITypeSymbol targetType, string parameterName, Compilation compilation, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options, Location? location)
        {
            if (sourceShape.IsDictionary != targetShape.IsDictionary)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedCollectionShape, location));
                return null;
            }

            var builder = new StringBuilder();
            var sourceExpression = EscapeIdentifier(parameterName);
            var targetLocal = CreateUniqueIdentifier(method, "target");
            var itemLocal = CreateUniqueIdentifier(method, "item", targetLocal);
            var indexLocal = CreateUniqueIdentifier(method, "index", targetLocal, itemLocal);
            var nullTarget = RenderEmptyCollection(targetShape, targetType, compilation);
            builder.Append("        if (");
            builder.Append(sourceExpression);
            builder.AppendLine(" == null)");
            builder.AppendLine("        {");
            if ((options.NullCollections == NullCollectionStrategyPreserve ||
                options.NullCollections == NullCollectionStrategyError && !options.NullCollectionsExplicit) &&
                IsMaybeNull(targetType))
            {
                builder.AppendLine("            return null;");
            }
            else if (options.NullCollections == NullCollectionStrategyEmpty)
            {
                builder.Append("            return ");
                builder.Append(nullTarget);
                builder.AppendLine(";");
            }
            else
            {
                builder.Append("            throw new global::System.ArgumentNullException(nameof(");
                builder.Append(sourceExpression);
                builder.AppendLine("));");
            }

            builder.AppendLine("        }");

            if (targetShape.IsDictionary)
            {
                var keyConversion = ResolveElementExpression(method, sourceShape.KeyType!, targetShape.KeyType!, itemLocal + ".Key", null, compilation, diagnostics, helpers, helperNames, options, location);
                var valueConversion = ResolveElementExpression(method, sourceShape.ElementType, targetShape.ElementType, itemLocal + ".Value", null, compilation, diagnostics, helpers, helperNames, options, location);
                if (keyConversion == null || valueConversion == null)
                {
                    return null;
                }

                builder.Append("        var " + targetLocal + " = new ");
                builder.Append(ConcreteCollectionType(targetShape, targetType));
                builder.Append("(");
                builder.Append(SourceCountExpression(sourceType, sourceShape, sourceExpression) ?? "0");
                if (CanPreserveComparer(sourceShape, targetShape, sourceType, targetType))
                {
                    builder.Append(", ");
                    builder.Append(sourceExpression);
                    builder.Append(".Comparer");
                }

                builder.AppendLine(");");
                builder.Append("        foreach (var " + itemLocal + " in ");
                builder.Append(sourceExpression);
                builder.AppendLine(")");
                builder.AppendLine("        {");
                builder.Append("            " + targetLocal + ".Add(");
                builder.Append(keyConversion);
                builder.Append(", ");
                builder.Append(valueConversion);
                builder.AppendLine(");");
                builder.AppendLine("        }");
                builder.AppendLine("        return " + targetLocal + ";");
                return builder.ToString();
            }

            if (targetShape.Kind == CollectionKind.Array)
            {
                var countExpression = SourceCountExpression(sourceType, sourceShape, sourceExpression);
                if (countExpression == null)
                {
                    builder.Append("        var " + targetLocal + " = new global::System.Collections.Generic.List<");
                    builder.Append(DisplayType(targetShape.ElementType));
                    builder.AppendLine(">();");
                }
                else
                {
                    builder.Append("        var " + targetLocal + " = new ");
                    var arrayType = (ArrayTypeSyntax)SyntaxFactory.ParseTypeName(DisplayType(targetShape.ElementType) + "[]");
                    var sizedRank = SyntaxFactory.ArrayRankSpecifier(SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.ParseExpression(countExpression!)));
                    builder.Append(arrayType.WithRankSpecifiers(arrayType.RankSpecifiers.Replace(arrayType.RankSpecifiers[0], sizedRank)).ToString());
                    builder.AppendLine(";");
                    builder.AppendLine("        var " + indexLocal + " = 0;");
                }
            }
            else
            {
                builder.Append("        var " + targetLocal + " = new ");
                builder.Append(ConcreteCollectionType(targetShape, targetType));
                var countExpression = SourceCountExpression(sourceType, sourceShape, sourceExpression);
                var preserveComparer = CanPreserveComparer(sourceShape, targetShape, sourceType, targetType);
                builder.Append('(');
                if (countExpression != null && !(preserveComparer && targetShape.Kind == CollectionKind.Set))
                {
                    builder.Append(countExpression);
                }

                if (preserveComparer)
                {
                    if (countExpression != null && targetShape.Kind == CollectionKind.Dictionary)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(sourceExpression);
                    builder.Append(".Comparer");
                }

                builder.AppendLine(");");
            }

            var elementConversion = ResolveElementExpression(method, sourceShape.ElementType, targetShape.ElementType, itemLocal, null, compilation, diagnostics, helpers, helperNames, options, location);
            if (elementConversion == null)
            {
                return null;
            }

            builder.Append("        foreach (var " + itemLocal + " in ");
            builder.Append(sourceExpression);
            builder.AppendLine(")");
            builder.AppendLine("        {");
            if (targetShape.Kind == CollectionKind.Array)
            {
                if (SourceCountExpression(sourceType, sourceShape, sourceExpression) == null)
                {
                    builder.Append("            " + targetLocal + ".Add(");
                    builder.Append(elementConversion);
                    builder.AppendLine(");");
                }
                else
                {
                    builder.Append("            " + targetLocal + "[" + indexLocal + "] = ");
                    builder.Append(elementConversion);
                    builder.AppendLine(";");
                    builder.AppendLine("            " + indexLocal + "++;");
                }
            }
            else
            {
                builder.Append("            " + targetLocal + ".Add(");
                builder.Append(elementConversion);
                builder.AppendLine(");");
            }

            builder.AppendLine("        }");
            builder.Append("        return ");
            builder.Append(targetShape.Kind == CollectionKind.Array && SourceCountExpression(sourceType, sourceShape, sourceExpression) == null ? targetLocal + ".ToArray()" : targetLocal);
            builder.AppendLine(";");
            return builder.ToString();
        }

        private static string? ResolveElementExpression(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options, Location? location)
        {
            if (ReportDuplicateVisibleDefaults(method, sourceType, targetType, compilation, diagnostics, "item", location))
            {
                return null;
            }
            var diagnosticCount = diagnostics.Count;
            var localMarked = ResolveConverterSet(method, method.ContainingType, static m => HasAttribute(m, MappingConverterAttributeName), sourceType, targetType, expression, sourceMember, compilation, diagnostics, "item", location);
            if (localMarked != null || diagnostics.Count != diagnosticCount)
            {
                return localMarked?.Expression;
            }

            var visible = ResolveVisibleMapping(method, method.ContainingType, sourceType, targetType, expression, sourceMember, compilation, diagnostics, "item", location);
            if (visible != null || diagnostics.Count != diagnosticCount)
            {
                return visible?.Expression;
            }

            var externalTypes = GetRegisteredMapperTypes(method.ContainingType, compilation);
            var externalConverter = ResolveExternalConversion(method, externalTypes, sourceType, targetType, expression, sourceMember, compilation, diagnostics, "item", location, mappingStage: false);
            if (externalConverter != null || diagnostics.Count != diagnosticCount)
            {
                return externalConverter?.Expression;
            }

            var externalMapping = ResolveExternalConversion(method, externalTypes, sourceType, targetType, expression, sourceMember, compilation, diagnostics, "item", location, mappingStage: true);
            if (externalMapping != null || diagnostics.Count != diagnosticCount)
            {
                return externalMapping?.Expression;
            }

            var languageDiagnosticCount = diagnostics.Count;
            var languageConversion = RequiresCollectionCopy(sourceType, targetType, compilation) ? null :
                ResolveLanguageConversion(sourceType, targetType, expression, compilation, diagnostics, location, options, isElement: true);
            if (languageConversion != null)
            {
                return languageConversion;
            }

            if (diagnostics.Count != languageDiagnosticCount)
            {
                return null;
            }

            var enumMapping = ResolveEnumMapping(method, sourceType, targetType, expression, sourceMember, "item", diagnostics, location, options, isElement: true);
            if (enumMapping != null || diagnostics.Count != languageDiagnosticCount)
            {
                return enumMapping?.Expression;
            }

            var nestedSourceShape = GetCollectionShape(sourceType, compilation);
            var nestedTargetShape = GetCollectionShape(targetType, compilation);
            if (nestedSourceShape != null || nestedTargetShape != null)
            {
                if (nestedSourceShape == null || nestedTargetShape == null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedCollectionShape, location));
                    return null;
                }

                var helperName = CreateHelperName(
                    nestedSourceShape.IsDictionary || nestedTargetShape.IsDictionary ? "MapDictionary_" : "MapCollection_",
                    sourceType,
                    targetType);
                if (helperNames.Add(helperName))
                {
                    var helper = CreateCollectionMappingModel(method, sourceType, targetType, method.Parameters[0].Name, null, compilation, diagnostics, helpers, helperNames, options, helperName);
                    if (helper == null)
                    {
                        return null;
                    }

                    helpers.Add(helper);
                }

                return helperName + "(" + expression + ")";
            }

            if (sourceType is INamedTypeSymbol namedSource && targetType is INamedTypeSymbol namedTarget && IsStructuralObjectType(namedSource) && IsStructuralObjectType(namedTarget))
            {
                var helperName = CreateHelperName("MapNested_", namedSource, namedTarget);
                if (helperNames.Add(helperName))
                {
                    var helper = CreateNestedMappingModel(method, namedSource, namedTarget, helperName, compilation, diagnostics, helpers, helperNames, options);
                    if (helper == null)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.StructuralNestedMappingFailed, location, namedSource.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), namedTarget.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                        return null;
                    }

                    helpers.Add(helper);
                }

                return helperName + "(" + expression + ")";
            }

            diagnostics.Add(Diagnostic.Create(Diagnostics.ConversionNotFound, location, sourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return null;
        }

        private static MappingModel? CreateNestedMappingModel(IMethodSymbol rootMethod, INamedTypeSymbol sourceType, INamedTypeSymbol targetType, string helperName, Compilation compilation, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder sharedHelpers, HashSet<string> sharedHelperNames, EffectiveMappingOptions options)
        {
            var sourceMembers = GetSourceMembers(sourceType, diagnostics).ToArray();
            var construction = SelectConstruction(targetType, rootMethod, sourceMembers, compilation, diagnostics, options, sharedHelpers, sharedHelperNames, ImmutableArray<ExplicitMemberConfiguration>.Empty);
            if (construction == null)
            {
                return null;
            }

            var assignments = ImmutableArray.CreateBuilder<AssignmentModel>();
            var preconditions = ImmutableArray.CreateBuilder<PreconditionModel>();
            var helpers = sharedHelpers;
            var helperNames = sharedHelperNames;
            var externalTypes = GetRegisteredMapperTypes(rootMethod.ContainingType, compilation).ToArray();

            foreach (var targetMember in GetTargetMembers(targetType, diagnostics, includeConstructorOnly: true))
            {
                if (construction.BoundTargetMembers.Any(m => SymbolEqualityComparer.Default.Equals(m, targetMember)))
                {
                    continue;
                }

                if (!CanAssignInInitializer(targetMember))
                {
                    if (IsRequired(targetMember) || TargetIsNonNullable(targetMember))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.RequiredTargetMemberNotMapped, targetMember.Locations.FirstOrDefault() ?? rootMethod.Locations.FirstOrDefault(), targetMember.Name));
                    }
                    else
                    {
                        ReportUnmappedTarget(targetMember, options.UnmappedTargetMembers, diagnostics);
                    }
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

            return new MappingModel(rootMethod, sourceNullable: false, returnNullable: false, options.NullableMismatch, options.ReferenceHandling, guardNonNullSource: false, construction, preconditions.ToImmutable(), assignments.ToImmutable(), ImmutableArray<MappingModel>.Empty, helperName, sourceType, targetType, customBody: null);
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

        private static string EscapeIdentifier(string value)
        {
            return SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ||
                SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
                ? "@" + value
                : value;
        }

        private static string EscapeMemberPath(string value)
        {
            return string.Join(".", value.Split('.').Select(EscapeIdentifier));
        }

        private static bool IsStructuralObjectType(INamedTypeSymbol type)
        {
            return type.SpecialType == SpecialType.None &&
                type.TypeKind != TypeKind.Enum &&
                (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct);
        }

        private static bool IsTupleBoundary(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            var sourceIsTuple = sourceType is INamedTypeSymbol sourceNamed && sourceNamed.IsTupleType;
            var targetIsTuple = targetType is INamedTypeSymbol targetNamed && targetNamed.IsTupleType;
            return sourceIsTuple != targetIsTuple;
        }

        private static CollectionShape? GetCollectionShape(ITypeSymbol type, Compilation? compilation, bool forDestinationConstruction = false)
        {
            if (type.SpecialType == SpecialType.System_String)
            {
                return null;
            }

            if (type is IArrayTypeSymbol array)
            {
                return array.Rank == 1
                    ? new CollectionShape(CollectionKind.Array, array.ElementType, null, isDictionary: false, isRectangularArray: false, unsupported: false, custom: false)
                    : new CollectionShape(CollectionKind.Array, array.ElementType, null, isDictionary: false, isRectangularArray: true, unsupported: true, custom: false);
            }

            if (!(type is INamedTypeSymbol named))
            {
                return null;
            }

            var display = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (display == "System.Collections.Generic.Queue<T>" || display == "System.Collections.Generic.Stack<T>" || display.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal) || display == "System.Collections.Generic.IAsyncEnumerable<T>")
            {
                return new CollectionShape(CollectionKind.Unsupported, named.TypeArguments.Length == 0 ? type : named.TypeArguments[0], null, false, false, true, false);
            }

            if (display == "System.Collections.Generic.Dictionary<TKey, TValue>" || display == "System.Collections.Generic.IDictionary<TKey, TValue>" || display == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                return new CollectionShape(CollectionKind.Dictionary, named.TypeArguments[1], named.TypeArguments[0], isDictionary: true, isRectangularArray: false, unsupported: false, custom: false);
            }

            if (display == "System.Collections.Generic.List<T>" || display == "System.Collections.Generic.IList<T>" || display == "System.Collections.Generic.ICollection<T>" || display == "System.Collections.Generic.IEnumerable<T>" || display == "System.Collections.Generic.IReadOnlyCollection<T>" || display == "System.Collections.Generic.IReadOnlyList<T>")
            {
                var kind = forDestinationConstruction &&
                    (display == "System.Collections.Generic.IEnumerable<T>" || display == "System.Collections.Generic.IReadOnlyCollection<T>" || display == "System.Collections.Generic.IReadOnlyList<T>")
                    ? CollectionKind.Array
                    : CollectionKind.List;
                return new CollectionShape(kind, named.TypeArguments[0], null, isDictionary: false, isRectangularArray: false, unsupported: false, custom: false);
            }

            if (display == "System.Collections.Generic.HashSet<T>" || display == "System.Collections.Generic.ISet<T>" || display == "System.Collections.Generic.IReadOnlySet<T>")
            {
                return new CollectionShape(CollectionKind.Set, named.TypeArguments[0], null, isDictionary: false, isRectangularArray: false, unsupported: false, custom: false);
            }

            var enumerable = named.AllInterfaces.FirstOrDefault(i => i.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.Collections.Generic.IEnumerable<T>");
            if (enumerable != null)
            {
                return new CollectionShape(CollectionKind.List, enumerable.TypeArguments[0], null, false, false, unsupported: false, custom: true);
            }

            return null;
        }

        private static bool IsCollectionType(ITypeSymbol type, Compilation? compilation)
        {
            return GetCollectionShape(type, compilation) != null;
        }

        private static string ConcreteCollectionType(CollectionShape shape, ITypeSymbol targetType)
        {
            var element = DisplayType(shape.ElementType);
            if (shape.IsDictionary)
            {
                var key = DisplayType(shape.KeyType!);
                return "global::System.Collections.Generic.Dictionary<" + key + ", " + element + ">";
            }

            if (shape.Kind == CollectionKind.Set)
            {
                return "global::System.Collections.Generic.HashSet<" + element + ">";
            }

            return "global::System.Collections.Generic.List<" + element + ">";
        }

        private static string RenderEmptyCollection(CollectionShape shape, ITypeSymbol targetType, Compilation compilation)
        {
            if (shape.Kind == CollectionKind.Array)
            {
                var elementType = DisplayType(shape.ElementType);
                return HasArrayEmpty(compilation)
                    ? "global::System.Array.Empty<" + elementType + ">()"
                    : "new " + elementType + "[0]";
            }

            return "new " + ConcreteCollectionType(shape, targetType) + "()";
        }

        private static bool HasArrayEmpty(Compilation compilation)
        {
            var arrayType = compilation.GetSpecialType(SpecialType.System_Array);
            return arrayType.GetMembers("Empty").OfType<IMethodSymbol>().Any(static method =>
                method.IsStatic &&
                method.TypeParameters.Length == 1 &&
                method.Parameters.Length == 0 &&
                method.ReturnType is IArrayTypeSymbol array &&
                SymbolEqualityComparer.Default.Equals(array.ElementType, method.TypeParameters[0]));
        }

        private static bool HasAggressiveInlining(Compilation compilation)
        {
            var attributeType = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.MethodImplAttribute");
            var optionsType = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.MethodImplOptions");
            var systemAttributeType = compilation.GetTypeByMetadataName("System.Attribute");
            if (attributeType == null ||
                optionsType == null ||
                systemAttributeType == null ||
                attributeType.DeclaredAccessibility != Accessibility.Public ||
                attributeType.TypeKind != TypeKind.Class ||
                attributeType.IsAbstract ||
                !SymbolEqualityComparer.Default.Equals(attributeType.BaseType, systemAttributeType) ||
                optionsType.DeclaredAccessibility != Accessibility.Public ||
                optionsType.TypeKind != TypeKind.Enum)
            {
                return false;
            }

            var hasConstructor = attributeType.InstanceConstructors.Any(constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, optionsType));
            var hasOption = optionsType.GetMembers("AggressiveInlining").OfType<IFieldSymbol>().Any(field =>
                field.DeclaredAccessibility == Accessibility.Public &&
                field.IsStatic &&
                field.HasConstantValue &&
                SymbolEqualityComparer.Default.Equals(field.Type, optionsType));
            return hasConstructor && hasOption;
        }

        private static bool CanPreserveComparer(CollectionShape sourceShape, CollectionShape targetShape, ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            if (sourceShape.Kind != targetShape.Kind || sourceShape.Kind != CollectionKind.Set && sourceShape.Kind != CollectionKind.Dictionary)
            {
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(sourceShape.ElementType, targetShape.ElementType))
            {
                return false;
            }

            if (sourceShape.IsDictionary && !SymbolEqualityComparer.Default.Equals(sourceShape.KeyType, targetShape.KeyType))
            {
                return false;
            }

            return sourceType is INamedTypeSymbol sourceNamed &&
                (sourceNamed.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.Collections.Generic.HashSet<T>" ||
                 sourceNamed.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.Collections.Generic.Dictionary<TKey, TValue>");
        }

        private static string? SourceCountExpression(ITypeSymbol sourceType, CollectionShape sourceShape, string sourceExpression)
        {
            if (sourceShape.Custom)
            {
                return null;
            }

            if (sourceType is IArrayTypeSymbol)
            {
                return sourceExpression + ".Length";
            }

            if (sourceType is INamedTypeSymbol named)
            {
                var display = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (display != "System.Collections.Generic.IEnumerable<T>")
                {
                    return sourceExpression + ".Count";
                }
            }

            return null;
        }

        private static string ShapeName(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return ShapeName(array.ElementType) + "_Array";
            }

            if (type is INamedTypeSymbol named && named.TypeArguments.Length != 0)
            {
                return SanitizeIdentifier(named.Name) + "_" + string.Join("_", named.TypeArguments.Select(ShapeName));
            }

            return SanitizeIdentifier(type.Name);
        }

        private static string CreateHelperName(string prefix, ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            var identity = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "->" +
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return prefix + ShapeName(sourceType) + "_To_" + ShapeName(targetType) + "_" + StableHash(identity);
        }

        private static string CreateExpressionCaptureName(IMethodSymbol method, string targetName, string expression)
        {
            return CreateUniqueIdentifier(method, "__sourcePath_" + SanitizeIdentifier(targetName) + "_" + StableHash(expression));
        }

        private static string CreateUniqueIdentifier(IMethodSymbol method, string preferredName, params string[] additionalReservedNames)
        {
            var name = preferredName;
            while (method.Parameters.Any(parameter => parameter.Name == name) ||
                additionalReservedNames.Contains(name, StringComparer.Ordinal))
            {
                name = "_" + name;
            }

            return name;
        }

        private static ConversionModel? ResolveExternalConversion(IMethodSymbol mappingMethod, INamedTypeSymbol[] externalTypes, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location, bool mappingStage, string? selectedName = null, bool sourceExpressionMayBeNull = false, string? sourceMemberPath = null)
        {
            var options = EffectiveOptions(mappingMethod);
            var allowNonNullInputFallback = sourceExpressionMayBeNull ||
                options.IgnoreNullSourceMembers && sourceMember != null && SourceMayBeNull(sourceMember);
            if (mappingStage && selectedName == null &&
                ReportUnusableDefault(externalTypes.SelectMany(static t => t.GetMembers().OfType<IMethodSymbol>()), mappingMethod, sourceType, targetType, compilation, diagnostics, targetName, location))
            {
                return null;
            }

            var classRegistrations = mappingMethod.ContainingType.GetAttributes()
                .Where(static a => IsAttribute(a, UseMapperAttributeName))
                .Select(static a => a.ConstructorArguments[0].Value as INamedTypeSymbol)
                .ToArray();
            var candidates = externalTypes.SelectMany(static t => t.GetMembers().OfType<IMethodSymbol>())
                .Where(m => !m.IsImplicitlyDeclared &&
                    (selectedName != null ? m.Name == selectedName : HasAttribute(m, MappingConverterAttributeName) != mappingStage) &&
                    GetConverterInputRank(m, m.ContainingType, sourceType, targetType, compilation, options, allowNonNullInputFallback) != int.MaxValue &&
                    compilation.IsSymbolAccessibleWithin(m, mappingMethod.ContainingType))
                .Select(m => new
                {
                    Method = m,
                    Rank = GetConverterInputRank(m, m.ContainingType, sourceType, targetType, compilation, options, allowNonNullInputFallback),
                    Scope = classRegistrations.Any(t => SymbolEqualityComparer.Default.Equals(t, m.ContainingType)) ? 0 : 1,
                })
                .OrderBy(static c => c.Rank)
                .ThenBy(static c => c.Scope)
                .ThenBy(static c => c.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }

            var best = candidates.Where(c => c.Rank == candidates[0].Rank && c.Scope == candidates[0].Scope).ToArray();
            if (mappingStage && selectedName == null)
            {
                var defaults = best.Where(static c => HasAttribute(c.Method, DefaultMappingAttributeName)).ToArray();
                if (defaults.Length > 1)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateDefaultMapping, location, targetName));
                    return null;
                }

                if (defaults.Length == 1)
                {
                    best = defaults;
                }
            }

            if (best.Length != 1)
            {
                diagnostics.Add(Diagnostic.Create(mappingStage && selectedName == null ? Diagnostics.AmbiguousMapping : Diagnostics.AmbiguousConverter, location, targetName));
                return null;
            }

            var selected = best[0].Method;
            var converterInput = ApplyConverterInputNullability(mappingMethod, selected, expression, sourceExpressionMayBeNull, sourceMemberPath ?? targetName, diagnostics, location);
            return converterInput == null ? null : CreateConverterResult(mappingMethod, selected, targetType, DisplayType(selected.ContainingType) + "." + EscapeIdentifier(selected.Name) + "(" + converterInput + ")", sourceMember, compilation, diagnostics, targetName, location);
        }

        private static ConversionModel? ResolveNamedConverter(IMethodSymbol mappingMethod, INamedTypeSymbol? converterType, INamedTypeSymbol[] externalTypes, string name, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, Location? location, string targetName, bool sourceExpressionMayBeNull = false, string? sourceMemberPath = null)
        {
            if (converterType != null)
            {
                return ResolveConverterSet(mappingMethod, converterType, m => m.Name == name, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetName, location, qualifyWithType: true, invalidWhenNone: true, sourceExpressionMayBeNull: sourceExpressionMayBeNull, sourceMemberPath: sourceMemberPath);
            }

            var diagnosticCount = diagnostics.Count;
            var declaration = mappingMethod.DeclaringSyntaxReferences.First().GetSyntax();
            var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
            var localMethods = semanticModel.LookupSymbols(declaration.SpanStart, mappingMethod.ContainingType, name).OfType<IMethodSymbol>().ToArray();
            if (localMethods.Any(m => !SymbolEqualityComparer.Default.Equals(m.ContainingType, mappingMethod.ContainingType)))
            {
                // A derived applicable overload hides base overloads even when a base parameter is a closer match.
                var invocation = SyntaxFactory.ParseExpression(EscapeIdentifier(name) + "(default(" + DisplayType(sourceType) + "))");
                var binding = semanticModel.GetSpeculativeSymbolInfo(declaration.SpanStart, invocation, SpeculativeBindingOption.BindAsExpression);
                if (binding.CandidateReason == CandidateReason.Ambiguous)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousConverter, location, name));
                    return null;
                }

                localMethods = binding.Symbol is IMethodSymbol bound ? new[] { bound } : Array.Empty<IMethodSymbol>();
            }
            var local = ResolveConverterSet(mappingMethod, mappingMethod.ContainingType, m => m.Name == name, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetName, location, candidateMethods: localMethods, sourceExpressionMayBeNull: sourceExpressionMayBeNull, sourceMemberPath: sourceMemberPath);
            if (local != null || diagnostics.Count != diagnosticCount)
            {
                return local;
            }

            var external = ResolveExternalConversion(mappingMethod, externalTypes, sourceType, targetType, expression, sourceMember, compilation, diagnostics, targetName, location, mappingStage: false, selectedName: name, sourceExpressionMayBeNull: sourceExpressionMayBeNull, sourceMemberPath: sourceMemberPath);
            if (external != null || diagnostics.Count != diagnosticCount)
            {
                return external;
            }

            diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidConverterSignature, location, name));
            return null;
        }

        private static ConversionModel? ResolveConverterSet(IMethodSymbol mappingMethod, INamedTypeSymbol type, Func<IMethodSymbol, bool> predicate, ITypeSymbol sourceType, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location, bool qualifyWithType = false, bool invalidWhenNone = false, IEnumerable<IMethodSymbol>? candidateMethods = null, bool sourceExpressionMayBeNull = false, string? sourceMemberPath = null)
        {
            var options = EffectiveOptions(mappingMethod);
            var allowNonNullInputFallback = sourceExpressionMayBeNull ||
                options.IgnoreNullSourceMembers && sourceMember != null && SourceMayBeNull(sourceMember);
            var methods = (candidateMethods ?? type.GetMembers().OfType<IMethodSymbol>())
                .Where(m => (!mappingMethod.IsStatic || m.IsStatic) && (!qualifyWithType || m.IsStatic) &&
                    compilation.IsSymbolAccessibleWithin(m, mappingMethod.ContainingType)).ToArray();
            var candidates = methods
                .Where(m => !m.IsImplicitlyDeclared && predicate(m) &&
                    GetConverterInputRank(m, type, sourceType, targetType, compilation, options, allowNonNullInputFallback) != int.MaxValue)
                .Select(m => new { Method = m, Rank = GetConverterInputRank(m, type, sourceType, targetType, compilation, options, allowNonNullInputFallback) })
                .OrderBy(static c => c.Rank)
                .ThenBy(static c => c.Method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                var asynchronous = methods.Any(m => predicate(m) &&
                    (IsAsyncMethod(m) || IsTaskLike(m.ReturnType)) && m.Parameters.Length == 1 &&
                    compilation.ClassifyConversion(sourceType, m.Parameters[0].Type).IsImplicit);
                if (asynchronous)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.AsyncMappingNotSupported, location, targetName));
                }
                else if (invalidWhenNone)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidConverterSignature, location, targetName));
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
            var receiver = qualifyWithType ? DisplayType(method.ContainingType) + "." : string.Empty;
            var converterInput = ApplyConverterInputNullability(mappingMethod, method, expression, sourceExpressionMayBeNull, sourceMemberPath ?? targetName, diagnostics, location);
            return converterInput == null ? null : CreateConverterResult(mappingMethod, method, targetType, receiver + EscapeIdentifier(method.Name) + "(" + converterInput + ")", sourceMember, compilation, diagnostics, targetName, location);
        }

        private static string? ApplyConverterInputNullability(IMethodSymbol mappingMethod, IMethodSymbol converter, string expression, bool sourceExpressionMayBeNull, string sourceMemberPath, ICollection<Diagnostic> diagnostics, Location? location)
        {
            if (!sourceExpressionMayBeNull || IsMaybeNull(converter.Parameters[0].Type))
            {
                return expression;
            }

            if (EffectiveOptions(mappingMethod).NullableMismatch == NullableMismatchPolicyError)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, location, sourceMemberPath));
                return null;
            }

            return "(" + expression + " ?? throw new global::System.InvalidOperationException(\"Source member path '" +
                sourceMemberPath + "' was null.\"))";
        }

        private static ConversionModel? CreateConverterResult(IMethodSymbol mappingMethod, IMethodSymbol converter, ITypeSymbol targetType, string expression, ISymbol? sourceMember, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location)
        {
            var options = EffectiveOptions(mappingMethod);
            var resultType = converter.ReturnType;
            if (IsMaybeNull(resultType) && !IsMaybeNull(targetType) &&
                (targetType.IsValueType || targetType.NullableAnnotation == NullableAnnotation.NotAnnotated))
            {
                if (options.NullableMismatch == NullableMismatchPolicyError)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.ConverterNullabilityMismatch, location, targetName));
                    return null;
                }

                expression = "(" + expression + " ?? throw new global::System.InvalidOperationException(\"Converter result for target '" + targetName + "' was null.\"))";
                resultType = resultType is INamedTypeSymbol nullableResult && nullableResult.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                    ? nullableResult.TypeArguments[0]
                    : resultType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            }

            var convertedExpression = ResolveLanguageConversion(resultType, targetType, expression, compilation, diagnostics, location, options, memberPath: targetName);
            return convertedExpression == null ? null : new ConversionModel(convertedExpression, sourceMember, potentiallyNull: false, targetName,
                nullCheckExpression: null, requiresNonNullSourceExpression: !IsMaybeNull(converter.Parameters[0].Type));
        }

        private static bool ReportUnusableDefault(IEnumerable<IMethodSymbol> methods, IMethodSymbol mappingMethod, ITypeSymbol sourceType, ITypeSymbol targetType, Compilation compilation, ICollection<Diagnostic> diagnostics, string targetName, Location? location)
        {
            var unusable = methods.Any(m => HasAttribute(m, DefaultMappingAttributeName) && m.Parameters.Length == 1 &&
                compilation.ClassifyConversion(sourceType, m.Parameters[0].Type).IsImplicit &&
                IsCompatibleConverterResult(m.ReturnType, targetType, compilation, EffectiveOptions(mappingMethod)) &&
                (!IsUsableConverter(m, m.ContainingType, sourceType, targetType, compilation, EffectiveOptions(mappingMethod)) || !compilation.IsSymbolAccessibleWithin(m, mappingMethod.ContainingType)));
            if (unusable)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.DefaultMappingNotUsable, location, targetName));
            }

            return unusable;
        }

        private static bool IsUsableConverter(IMethodSymbol method, INamedTypeSymbol lookupType, ITypeSymbol sourceType, ITypeSymbol targetType, Compilation compilation, EffectiveMappingOptions options)
        {
            if (IsAsyncMethod(method) || IsTaskLike(method.ReturnType) || method.IsGenericMethod || method.Parameters.Length != 1 || method.ReturnsVoid || method.Parameters[0].RefKind != RefKind.None)
            {
                return false;
            }

            if (lookupType.IsStatic && !method.IsStatic)
            {
                return false;
            }

            return compilation.ClassifyConversion(sourceType, method.Parameters[0].Type).IsImplicit &&
                IsCompatibleConverterResult(method.ReturnType, targetType, compilation, options);
        }

        private static int GetConverterInputRank(IMethodSymbol method, INamedTypeSymbol lookupType, ITypeSymbol sourceType, ITypeSymbol targetType,
            Compilation compilation, EffectiveMappingOptions options, bool sourceExpressionMayBeNull)
        {
            if (IsUsableConverter(method, lookupType, sourceType, targetType, compilation, options))
            {
                return compilation.ClassifyConversion(sourceType, method.Parameters[0].Type).IsIdentity ? 0 : 1;
            }

            if (!sourceExpressionMayBeNull)
            {
                return int.MaxValue;
            }

            var nonNullSourceType = RemoveNullableAnnotation(sourceType);
            if (!IsUsableConverter(method, lookupType, nonNullSourceType, targetType, compilation, options))
            {
                return int.MaxValue;
            }

            return compilation.ClassifyConversion(nonNullSourceType, method.Parameters[0].Type).IsIdentity ? 2 : 3;
        }

        private static bool IsCompatibleConverterResult(ITypeSymbol resultType, ITypeSymbol targetType, Compilation compilation, EffectiveMappingOptions options)
        {
            var conversion = compilation.ClassifyConversion(resultType, targetType);
            if (conversion.IsImplicit)
            {
                return true;
            }

            if (resultType is INamedTypeSymbol nullableResult && nullableResult.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                resultType = nullableResult.TypeArguments[0];
                conversion = compilation.ClassifyConversion(resultType, targetType);
            }

            return conversion.IsImplicit || conversion.IsUserDefined && options.AllowExplicitOperators ||
                conversion.Exists && IsNumericType(resultType) && IsNumericType(targetType) && options.NumericConversion != "ImplicitOnly";
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
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidSourcePath, location, sourcePath));
                return null;
            }

            var segments = sourcePath.Split('.');
            if (segments.Length == 0 || segments.Any(static s => string.IsNullOrWhiteSpace(s)))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidSourcePath, location, sourcePath));
                return null;
            }

            var roots = rootMembers.Where(m => m.Name == segments[0]).ToArray();
            if (roots.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMemberMatch, location, segments[0]));
                return null;
            }
            ISymbol? first = roots.FirstOrDefault();
            if (first == null)
            {
                diagnostics.Add(Diagnostic.Create(HasIndexer(sourceType, segments[0]) ? Diagnostics.IndexerNotSupported : Diagnostics.InvalidSourcePath, location, segments[0]));
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
                var members = GetReadableDirectMembers(currentType, diagnostics).Where(m => m.Name == segments[i]).ToArray();
                if (members.Length > 1)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMemberMatch, location, segments[i]));
                    return null;
                }
                var member = members.FirstOrDefault();
                if (member == null)
                {
                    diagnostics.Add(Diagnostic.Create(HasIndexer(currentType, segments[i]) ? Diagnostics.IndexerNotSupported : Diagnostics.InvalidSourcePath, location, segments[i]));
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

            var escapedParameterName = EscapeIdentifier(parameterName);
            return string.Join(" || ", sourcePath.NullCheckExpressions.Select(p => escapedParameterName + "." + EscapeMemberPath(p) + " == null"));
        }

        private static string BuildNullSafeSourcePathExpression(string parameterName, SourcePathModel sourcePath)
        {
            var nullablePrefixes = new HashSet<string>(sourcePath.NullCheckExpressions, StringComparer.Ordinal);
            var segments = sourcePath.Expression.Split('.');
            var builder = new StringBuilder(EscapeIdentifier(parameterName));
            var currentPath = string.Empty;
            for (var index = 0; index < segments.Length; index++)
            {
                builder.Append(nullablePrefixes.Contains(currentPath) ? "?." : ".");
                builder.Append(EscapeIdentifier(segments[index]));
                currentPath = index == 0 ? segments[index] : currentPath + "." + segments[index];
            }

            return builder.ToString();
        }

        private static string? BuildPatchGuard(string parameterName, ExplicitMemberConfiguration? explicitConfiguration, ISymbol sourceMember)
        {
            var sourcePath = explicitConfiguration == null ? null : explicitConfiguration.SelectedSource;
            if (sourcePath != null)
            {
                if (!SourcePathMayBeNull(sourcePath))
                {
                    return null;
                }

                var escapedParameterName = EscapeIdentifier(parameterName);
                return string.Join(" && ", sourcePath.NullCheckExpressions.Select(p => escapedParameterName + "." + EscapeMemberPath(p) + " != null"));
            }

            return SourceMayBeNull(sourceMember)
                ? EscapeIdentifier(parameterName) + "." + EscapeIdentifier(sourceMember.Name) + " != null"
                : null;
        }

        private static IEnumerable<ISymbol> GetReadableDirectMembers(ITypeSymbol type, ICollection<Diagnostic> diagnostics)
        {
            return GetSourceMembers(type, diagnostics);
        }

        private static bool HasIndexer(ITypeSymbol type, string memberName)
        {
            foreach (var current in GetMemberHierarchy(type))
            {
                if (current.GetMembers().OfType<IPropertySymbol>().Any(p => p.IsIndexer &&
                    (p.Name == memberName || p.MetadataName == memberName)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDirectMember(ITypeSymbol type, string memberName)
        {
            return GetMemberHierarchy(type).SelectMany(static t => t.GetMembers()).Any(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Name == memberName && (m is IFieldSymbol field && !field.IsConst || m is IPropertySymbol property && property.Parameters.Length == 0));
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
            var nullCollections = ReadNullCollectionOption(mapping) ?? ReadNullCollectionOption(mapper) ?? ReadNullCollectionOption(assemblyDefaults);
            return new EffectiveMappingOptions(
                ReadEnumOption(mapping, "NameMatching") ?? ReadEnumOption(mapper, "NameMatching") ?? ReadEnumOption(assemblyDefaults, "NameMatching") ?? "ExactThenIgnoreCase",
                ReadEnumOption(mapping, "UnmappedTargetMembers") ?? ReadEnumOption(mapper, "UnmappedTargetMembers") ?? ReadEnumOption(assemblyDefaults, "UnmappedTargetMembers") ?? UnmappedMemberPolicyWarning,
                ReadEnumOption(mapping, "UnmappedSourceMembers") ?? ReadEnumOption(mapper, "UnmappedSourceMembers") ?? ReadEnumOption(assemblyDefaults, "UnmappedSourceMembers") ?? UnmappedMemberPolicyIgnore,
                ReadEnumOption(mapping, "NullableMismatch") ?? ReadEnumOption(mapper, "NullableMismatch") ?? ReadEnumOption(assemblyDefaults, "NullableMismatch") ?? NullableMismatchPolicyError,
                nullCollections ?? NullCollectionStrategyError,
                nullCollections != null,
                ReadOptionState(mapping, "GuardNonNullSource") ?? ReadBoolOption(mapper, "GuardNonNullSource"),
                ReadOptionState(mapping, "IgnoreNullSourceMembers") ?? ReadBoolOption(mapper, "IgnoreNullSourceMembers"),
                ReadEnumOption(mapping, "EnumMapping") ?? ReadEnumOption(mapper, "EnumMapping") ?? EnumMappingStrategyByName,
                ReadEnumOption(mapping, "EnumNumericConversion") ?? ReadEnumOption(mapper, "EnumNumericConversion") ?? EnumNumericConversionChecked,
                ReadEnumOption(mapping, "UnmatchedEnumValues") ?? ReadEnumOption(mapper, "UnmatchedEnumValues") ?? UnmatchedEnumValuePolicyError,
                ReadEnumOption(mapping, "ReferenceHandling") ?? ReadEnumOption(mapper, "ReferenceHandling") ?? ReferenceHandlingNone,
                ReadEnumOption(mapping, "NumericConversion") ?? ReadEnumOption(mapper, "NumericConversion") ?? "ImplicitOnly",
                ReadOptionState(mapping, "AllowExplicitOperators") ?? ReadBoolOption(mapper, "AllowExplicitOperators"));
        }

        private static bool ReadBoolOption(AttributeData? attribute, string name)
        {
            return attribute != null && attribute.NamedArguments.Any(pair => pair.Key == name && pair.Value.Value is bool value && value);
        }

        private static bool? ReadOptionState(AttributeData? attribute, string name)
        {
            if (attribute == null)
            {
                return null;
            }

            foreach (var pair in attribute.NamedArguments)
            {
                if (pair.Key == name && pair.Value.Value is int raw && raw != 0)
                {
                    return raw == 2;
                }
            }

            return null;
        }

        private static string? ReadNullCollectionOption(AttributeData? attribute)
        {
            if (attribute == null)
            {
                return null;
            }

            foreach (var pair in attribute.NamedArguments)
            {
                if (pair.Key == "NullCollections" && pair.Value.Value is int raw && raw != 0)
                {
                    return raw == 2 ? NullCollectionStrategyPreserve : raw == 3 ? NullCollectionStrategyEmpty : NullCollectionStrategyError;
                }

                if (pair.Key == "NullCollections" && pair.Value.Value != null)
                {
                    var value = pair.Value.Value.ToString();
                    return value == "2" ? NullCollectionStrategyPreserve : value == "3" ? NullCollectionStrategyEmpty : NullCollectionStrategyError;
                }
            }

            return null;
        }

        private static string? ReadEnumOption(AttributeData? attribute, string name)
        {
            if (attribute == null)
            {
                return null;
            }

            foreach (var pair in attribute.NamedArguments)
            {
                if (pair.Key == name && TryReadNonZeroInt32(pair.Value.Value, out var raw))
                {
                    if (name == "NameMatching")
                    {
                        return raw == 1 ? NameMatchingExact : raw == 3 ? NameMatchingIgnoreCase : "ExactThenIgnoreCase";
                    }

                    if (name == "NullableMismatch")
                    {
                        return raw == 2 ? NullableMismatchPolicyThrow : NullableMismatchPolicyError;
                    }

                    if (name == "EnumMapping")
                    {
                        return raw == 2 ? EnumMappingStrategyByValue : EnumMappingStrategyByName;
                    }

                    if (name == "EnumNumericConversion")
                    {
                        return raw == 2 ? EnumNumericConversionUnchecked : EnumNumericConversionChecked;
                    }

                    if (name == "NumericConversion")
                    {
                        return raw == 2 ? "Checked" : raw == 3 ? "Unchecked" : "ImplicitOnly";
                    }

                    if (name == "UnmatchedEnumValues")
                    {
                        return raw == 2 ? UnmatchedEnumValuePolicyThrow : raw == 3 ? UnmatchedEnumValuePolicyByValue : UnmatchedEnumValuePolicyError;
                    }

                    if (name == "ReferenceHandling")
                    {
                        return raw == 2 ? ReferenceHandlingThrowOnCycle : ReferenceHandlingNone;
                    }

                    return raw == 1 ? UnmappedMemberPolicyIgnore : raw == 2 ? UnmappedMemberPolicyInfo : raw == 3 ? UnmappedMemberPolicyWarning : raw == 4 ? UnmappedMemberPolicyError : null;
                }
            }

            return null;
        }

        private static bool TryReadNonZeroInt32(object? value, out int raw)
        {
            if (value is IConvertible convertible)
            {
                raw = convertible.ToInt32(CultureInfo.InvariantCulture);
                return raw != 0;
            }

            raw = 0;
            return false;
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

        private static IEnumerable<INamedTypeSymbol> GetMemberHierarchy(ITypeSymbol type)
        {
            for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                yield return current;
                if (current.TypeKind == TypeKind.Interface)
                {
                    foreach (var inherited in current.AllInterfaces
                        .OrderByDescending(static i => i.AllInterfaces.Length)
                        .ThenBy(static i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
                    {
                        yield return inherited;
                    }
                }
            }
        }

        private static IEnumerable<ISymbol> GetVisibleMembers(ITypeSymbol type, ICollection<Diagnostic> diagnostics, Func<ISymbol, bool> predicate)
        {
            var selected = new Dictionary<string, List<ISymbol>>(StringComparer.Ordinal);
            foreach (var current in GetMemberHierarchy(type))
            {
                foreach (var member in current.GetMembers()
                    .Where(static m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public)
                    .Where(predicate)
                    .OrderBy(static m => m is IPropertySymbol ? 0 : 1)
                    .ThenBy(static m => m.Name, StringComparer.Ordinal))
                {
                    if (!selected.TryGetValue(member.Name, out var sameName))
                    {
                        sameName = new List<ISymbol>();
                        selected.Add(member.Name, sameName);
                    }

                    var moreDerived = sameName.FirstOrDefault(previous => current.TypeKind != TypeKind.Interface ||
                        SymbolEqualityComparer.Default.Equals(previous.ContainingType, current) ||
                        previous.ContainingType.AllInterfaces.Contains(current, SymbolEqualityComparer.Default));
                    if (moreDerived != null)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.HiddenMemberSelected, moreDerived.Locations.FirstOrDefault(), moreDerived.Name));
                        continue;
                    }

                    sameName.Add(member);
                }
            }

            return selected.Values.SelectMany(static members => members)
                .OrderBy(static m => m.Name, StringComparer.Ordinal)
                .ThenBy(static m => m.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal);
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

        private static ConstructionModel? SelectConstruction(INamedTypeSymbol targetType, IMethodSymbol method, ISymbol[] sourceMembers, Compilation compilation, ICollection<Diagnostic> diagnostics, EffectiveMappingOptions options, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, ImmutableArray<ExplicitMemberConfiguration> configurations, ISet<string>? targetDefaults = null)
        {
            var location = method.Locations.FirstOrDefault();
            var constructors = targetType.Constructors
                .Where(c => IsAccessibleConstructor(c, method.ContainingType, compilation) && !IsRecordCopyConstructor(c, targetType))
                .OrderBy(static c => c.Parameters.Length)
                .ToArray();
            var marked = constructors.Where(c => HasAttribute(c, MappingConstructorAttributeName)).ToArray();
            if (marked.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.MultipleMappingConstructors, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            var candidates = new List<(IMethodSymbol Constructor, ConstructionModel? Plan, List<Diagnostic> Diagnostics, ImmutableArray<MappingModel>.Builder Helpers, HashSet<string> Names)>();
            foreach (var constructor in marked.Length == 1 ? marked : constructors.Where(static c => c.Parameters.Length > 0))
            {
                var candidateDiagnostics = new List<Diagnostic>();
                var candidateHelpers = ImmutableArray.CreateBuilder<MappingModel>();
                var candidateNames = new HashSet<string>(helperNames, StringComparer.Ordinal);
                var plan = TryCreateConstruction(constructor, targetType, method, sourceMembers, compilation, targetDefaults, configurations, candidateDiagnostics, candidateHelpers, candidateNames, options);
                candidates.Add((constructor, plan, candidateDiagnostics, candidateHelpers, candidateNames));
            }

            var satisfiable = candidates.Where(static c => c.Plan != null && !c.Diagnostics.Any(IsFatalDiagnostic)).ToArray();
            if (satisfiable.Length > 0)
            {
                var max = satisfiable.Max(static c => c.Plan!.ParameterCount);
                var best = satisfiable.Where(c => c.Plan!.ParameterCount == max).ToArray();
                if (best.Length == 1)
                {
                    helpers.AddRange(best[0].Helpers);
                    helperNames.UnionWith(best[0].Names);
                    foreach (var diagnostic in best[0].Diagnostics) diagnostics.Add(diagnostic);
                    return best[0].Plan;
                }

                diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousConstructor, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            if (marked.Length == 1)
            {
                foreach (var diagnostic in candidates[0].Diagnostics) diagnostics.Add(diagnostic);
                diagnostics.Add(Diagnostic.Create(Diagnostics.MappingConstructorNotSatisfiable, location, targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
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

            foreach (var diagnostic in candidates.SelectMany(static c => c.Diagnostics)) diagnostics.Add(diagnostic);

            return null;
        }

        private static ConstructionModel? TryCreateConstruction(IMethodSymbol constructor, INamedTypeSymbol targetType, IMethodSymbol method, ISymbol[] sourceMembers, Compilation compilation, ISet<string>? targetDefaults, ImmutableArray<ExplicitMemberConfiguration> configurations, ICollection<Diagnostic> diagnostics, ImmutableArray<MappingModel>.Builder helpers, HashSet<string> helperNames, EffectiveMappingOptions options)
        {
            var nameMatching = options.NameMatching;
            var arguments = ImmutableArray.CreateBuilder<ConstructorArgumentModel>();
            var boundMembers = ImmutableArray.CreateBuilder<ISymbol>();
            var targetMembers = GetTargetMembers(targetType, new List<Diagnostic>(), includeConstructorOnly: true).ToArray();

            foreach (var parameter in constructor.Parameters)
            {
                var defaultTargetMatch = MatchTargetForParameter(parameter, targetMembers, nameMatching);
                if (defaultTargetMatch.Ambiguous)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMemberMatch, parameter.Locations.FirstOrDefault(), parameter.Name));
                    return null;
                }
                if (defaultTargetMatch.Member != null && !defaultTargetMatch.Ambiguous && targetDefaults != null && targetDefaults.Contains(defaultTargetMatch.Member.Name))
                {
                    if (!parameter.HasExplicitDefaultValue)
                    {
                        return null;
                    }

                    boundMembers.Add(defaultTargetMatch.Member);
                    continue;
                }

                var configuration = defaultTargetMatch.Member == null ? null : configurations.FirstOrDefault(c => c.TargetName == defaultTargetMatch.Member.Name);
                var match = configuration?.SelectedSource != null ? new MatchResult(configuration.SelectedSource.SourceMember, false) : MatchParameter(parameter, sourceMembers, nameMatching);
                if (match.Ambiguous)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.AmbiguousMemberMatch, parameter.Locations.FirstOrDefault(), parameter.Name));
                    return null;
                }

                if (match.Member == null && string.IsNullOrWhiteSpace(configuration?.Use))
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

                var conversionTarget = defaultTargetMatch.Member != null && SymbolEqualityComparer.IncludeNullability.Equals(GetMemberType(defaultTargetMatch.Member), parameter.Type) ? defaultTargetMatch.Member : parameter;
                var conversion = ResolveConversion(method, configuration, match.Member ?? method.Parameters[0], conversionTarget, compilation, GetRegisteredMapperTypes(method.ContainingType, compilation).ToArray(), diagnostics, helpers, helperNames, options);
                if (conversion == null)
                {
                    return null;
                }

                if (conversion.PotentiallyNull && !parameter.Type.IsValueType && !IsMaybeNull(parameter.Type))
                {
                    if (options.NullableMismatch == NullableMismatchPolicyError)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.NullableToNonNullable, parameter.Locations.FirstOrDefault(), parameter.Name));
                        return null;
                    }

                    conversion = new ConversionModel("(" + conversion.Expression + " ?? throw new global::System.InvalidOperationException(\"Source member '" + conversion.MemberPath + "' was null.\"))", conversion.SourceMember, false, conversion.MemberPath, null);
                }

                arguments.Add(new ConstructorArgumentModel(parameter.Name, conversion.Expression, match.Member));
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
            if (nameMatching == NameMatchingExact || nameMatching != NameMatchingIgnoreCase && exact.Length == 1)
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
            if (nameMatching == NameMatchingExact || nameMatching != NameMatchingIgnoreCase && exact.Length == 1)
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

        private static bool IsAccessibleConstructor(IMethodSymbol constructor, INamedTypeSymbol mapperType, Compilation compilation)
        {
            return compilation.IsSymbolAccessibleWithin(constructor, mapperType);
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
            return targetType.IsRecord && constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, targetType);
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

        private static bool CanAssignAfterConstruction(ISymbol member)
        {
            return member is IFieldSymbol field && !field.IsReadOnly && !field.IsConst ||
                member is IPropertySymbol property && property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public && !property.SetMethod.IsInitOnly;
        }

        private static bool IsMaybeNull(IParameterSymbol parameter)
        {
            return IsMaybeNull(parameter.Type);
        }

        private static bool IsMaybeNull(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
                !type.IsValueType && type.NullableAnnotation == NullableAnnotation.Annotated;
        }

        private static ITypeSymbol RemoveNullableAnnotation(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? named.TypeArguments[0]
                : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
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
            if (symbol is IParameterSymbol parameter)
            {
                return parameter.Type;
            }
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

            diagnostics.Add(Diagnostic.Create(policy == UnmappedMemberPolicyError ? Diagnostics.UnmappedTargetMemberError : policy == UnmappedMemberPolicyInfo ? Diagnostics.UnmappedTargetMemberInfo : Diagnostics.UnmappedTargetMember, member.Locations.FirstOrDefault(), member.Name));
        }

        private static void ReportUnmappedSource(ISymbol member, string policy, ICollection<Diagnostic> diagnostics)
        {
            if (policy == UnmappedMemberPolicyIgnore)
            {
                return;
            }

            diagnostics.Add(Diagnostic.Create(policy == UnmappedMemberPolicyError ? Diagnostics.UnmappedSourceMemberError : policy == UnmappedMemberPolicyWarning ? Diagnostics.UnmappedSourceMemberWarning : Diagnostics.UnmappedSourceMember, member.Locations.FirstOrDefault(), member.Name));
        }

        private static void ValidateMappingMethod(IMethodSymbol method, List<Diagnostic> diagnostics, bool handwritten = false)
        {
            var location = method.Locations.FirstOrDefault();
            var unsupported = method.Parameters.Select(static p => p.Type).Concat(new[] { method.ReturnType })
                .FirstOrDefault(type => ContainsUnsupportedSignatureType(type, handwritten) || !handwritten && method.Parameters.Length == 2 && type.IsRefLikeType);
            if (unsupported != null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedGeneratedType, location, unsupported.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return;
            }

            if (IsAsyncMethod(method) || IsTaskLike(method.ReturnType))
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

            if (method.ContainingType.IsStatic && !method.IsStatic)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMappingMethodSignature, location, method.Name));
                return;
            }

            if (method.IsGenericMethod)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMappingMethodSignature, location, method.Name));
                return;
            }

            if (method.Parameters.Length == 1)
            {
                if (method.ReturnsVoid || method.Parameters[0].RefKind != RefKind.None)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMappingMethodSignature, location, method.Name));
                }
            }
            else if (method.Parameters.Length == 2)
            {
                var destination = method.Parameters[1];
                if (method.Parameters[0].RefKind != RefKind.None ||
                    destination.RefKind == RefKind.Out ||
                    method.ReturnsVoid == false && !SymbolEqualityComparer.Default.Equals(method.ReturnType, destination.Type))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUpdateSignature, location, method.Name));
                }
                else if (destination.Type.IsValueType && destination.RefKind != RefKind.Ref)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.StructUpdateRequiresRef, location, method.Name));
                }
                else if (!method.ReturnsVoid && destination.RefKind == RefKind.Ref)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUpdateSignature, location, method.Name));
                }
                else if (method.ReturnsVoid && IsMaybeNull(destination))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.NullableVoidDestinationNotSupported, location, method.Name));
                }
            }
            else
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

        private static bool ContainsUnsupportedSignatureType(ITypeSymbol type, bool handwritten = false)
        {
            return type.TypeKind == TypeKind.Dynamic || !handwritten && (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer) ||
                type is IArrayTypeSymbol array && ContainsUnsupportedSignatureType(array.ElementType, handwritten) ||
                type is INamedTypeSymbol named && named.TypeArguments.Any(argument => ContainsUnsupportedSignatureType(argument, handwritten));
        }

        private static bool IsAsyncMethod(IMethodSymbol method)
        {
            // Some compiler hosts omit IsAsync on invalid bodyless partial declarations.
            return method.IsAsync || method.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is MethodDeclarationSyntax syntax && syntax.Modifiers.Any(SyntaxKind.AsyncKeyword));
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

        private static void ValidateUseMapper(AttributeData attribute, ICollection<Diagnostic> diagnostics, Compilation? compilation = null, INamedTypeSymbol? mapper = null)
        {
            var mapperType = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as ITypeSymbol : null;
            var location = attribute.ApplicationSyntaxReference == null ? Location.None : attribute.ApplicationSyntaxReference.GetSyntax().GetLocation();
            if (!(mapperType is INamedTypeSymbol namedMapperType) || mapperType.TypeKind == TypeKind.Error)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUseMapperType, location, "UseMapper"));
            }
            else if (!namedMapperType.IsStatic)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ExternalMapperMustBeStatic, location, namedMapperType.Name));
            }
            else if (namedMapperType.IsGenericType || ContainingTypes(namedMapperType).Any(static t => t.IsGenericType) ||
                !namedMapperType.GetMembers().OfType<IMethodSymbol>().Any(method =>
                    IsUsableRegisteredMethod(method) &&
                    (compilation == null || mapper == null || compilation.IsSymbolAccessibleWithin(method, mapper))))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidUseMapperType, location, namedMapperType.Name));
            }
        }

        private static bool IsUsableRegisteredMethod(IMethodSymbol method)
        {
            if (method.IsImplicitlyDeclared || method.MethodKind != MethodKind.Ordinary || !method.IsStatic ||
                method.Parameters.Length == 2 && (method.Parameters[1].Type is IArrayTypeSymbol ||
                    method.Parameters[1].Type.IsReferenceType && method.Parameters[1].RefKind != RefKind.None))
            {
                return false;
            }

            var diagnostics = new List<Diagnostic>();
            ValidateMappingMethod(method, diagnostics, handwritten: !IsPartialDeclaration(method));
            return diagnostics.Count == 0;
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
                builder.Append(containingType.IsRecord
                    ? containingType.TypeKind == TypeKind.Struct ? "record struct " : "record "
                    : containingType.TypeKind == TypeKind.Interface ? "interface " : containingType.TypeKind == TypeKind.Struct ? "struct " : "class ");
                builder.AppendLine(EscapeIdentifier(containingType.Name));
                builder.AppendLine("{");
            }

            builder.Append(symbol.IsStatic ? "static partial class " : "partial class ");
            builder.AppendLine(EscapeIdentifier(symbol.Name));
            builder.AppendLine("{");
            var needsCycleTracker = false;
            var orderedMappings = mappings.OrderBy(static m => m.Method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal).ToArray();
            var recursiveDeclaredNames = FindRecursiveDeclaredMappingNames(orderedMappings);
            foreach (var mapping in orderedMappings)
            {
                var recursiveHelperNames = mapping.ReferenceHandling == ReferenceHandlingThrowOnCycle ? FindRecursiveHelperNames(mapping) : new HashSet<string>(StringComparer.Ordinal);
                var declaredRecursive = mapping.ReferenceHandling == ReferenceHandlingThrowOnCycle && recursiveDeclaredNames.Contains(mapping.Method.Name);
                if (declaredRecursive)
                {
                    recursiveHelperNames.UnionWith(recursiveDeclaredNames);
                    IncludeTrackerDependentHelpers(mapping, recursiveHelperNames);
                }
                needsCycleTracker = needsCycleTracker || recursiveHelperNames.Count != 0;
                if (declaredRecursive)
                {
                    AppendDeclaredTrackingWrapper(builder, mapping);
                    var core = new MappingModel(mapping.Method, mapping.SourceNullable, mapping.ReturnNullable, mapping.NullableMismatch,
                        mapping.ReferenceHandling, mapping.GuardNonNullSource, mapping.Construction, mapping.Preconditions,
                        mapping.Assignments, mapping.Helpers, mapping.Method.Name, mapping.Method.Parameters[0].Type,
                        mapping.IsUpdate && mapping.DestinationParameter != null ? mapping.DestinationParameter.Type : mapping.Method.ReturnType,
                        mapping.CustomBody, mapping.IsUpdate, mapping.DestinationParameter, mapping.DestinationNullable, isDeclaredCore: true);
                    AppendMapping(builder, core, recursiveHelperNames);
                }
                else
                {
                    AppendMapping(builder, mapping, recursiveHelperNames);
                }
                foreach (var helper in FlattenHelpers(mapping).OrderBy(static h => h.HelperName, StringComparer.Ordinal))
                {
                    AppendMapping(builder, helper, recursiveHelperNames);
                }
            }
            if (needsCycleTracker)
            {
                AppendCycleTracker(builder);
            }
            builder.AppendLine("}");

            foreach (var _ in ContainingTypes(symbol))
            {
                builder.AppendLine("}");
            }

            AppendNamespaceEnd(builder, symbol);
            return builder.ToString();
        }

        private static void AppendDeclaredTrackingWrapper(StringBuilder builder, MappingModel mapping)
        {
            var method = mapping.Method;
            var trackerLocal = CreateUniqueIdentifier(method, "__tracker");
            builder.Append("    ");
            builder.Append(ToAccessibility(method.DeclaredAccessibility));
            builder.Append(' ');
            if (method.IsStatic)
            {
                builder.Append("static ");
            }
            builder.Append("partial ");
            builder.Append(method.ReturnsVoid ? "void" : DisplayType(method.ReturnType));
            builder.Append(' ');
            builder.Append(EscapeIdentifier(method.Name));
            builder.Append('(');
            for (var index = 0; index < method.Parameters.Length; index++)
            {
                if (index != 0)
                {
                    builder.Append(", ");
                }
                var parameter = method.Parameters[index];
                if (parameter.RefKind == RefKind.Ref)
                {
                    builder.Append("ref ");
                }
                builder.Append(DisplayType(parameter.Type));
                builder.Append(' ');
                builder.Append(EscapeIdentifier(parameter.Name));
            }
            builder.AppendLine(")");
            builder.AppendLine("    {");
            builder.AppendLine("        var " + trackerLocal + " = new __LiteMapperCycleTracker();");
            builder.Append("        ");
            if (!method.ReturnsVoid)
            {
                builder.Append("return ");
            }
            builder.Append(EscapeIdentifier(method.Name));
            builder.Append('(');
            for (var index = 0; index < method.Parameters.Length; index++)
            {
                if (index != 0)
                {
                    builder.Append(", ");
                }
                if (method.Parameters[index].RefKind == RefKind.Ref)
                {
                    builder.Append("ref ");
                }
                builder.Append(EscapeIdentifier(method.Parameters[index].Name));
            }
            if (method.Parameters.Length != 0)
            {
                builder.Append(", ");
            }
            builder.AppendLine(trackerLocal + ", string.Empty);");
            builder.AppendLine("    }");
        }

        private static HashSet<string> FindRecursiveDeclaredMappingNames(IEnumerable<MappingModel> mappings)
        {
            var models = mappings.ToArray();
            var names = models.Select(static mapping => mapping.Method.Name).Distinct(StringComparer.Ordinal).ToArray();
            var edges = models.GroupBy(static mapping => mapping.Method.Name, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key,
                    group => new HashSet<string>(group.SelectMany(mapping => CalledMappings(mapping, names)), StringComparer.Ordinal),
                    StringComparer.Ordinal);
            var recursive = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (CanReach(name, name, edges, new HashSet<string>(StringComparer.Ordinal)))
                {
                    recursive.Add(name);
                }
            }
            return recursive;
        }

        private static IEnumerable<string> CalledMappings(MappingModel mapping, string[] names)
        {
            var called = CalledHelpers(mapping, names);
            foreach (var helper in FlattenHelpers(mapping))
            {
                called.UnionWith(CalledHelpers(helper, names));
            }
            return called;
        }

        private static void IncludeTrackerDependentHelpers(MappingModel mapping, HashSet<string> trackedNames)
        {
            var helpers = FlattenHelpers(mapping).Where(static helper => helper.HelperName != null).ToArray();
            var allNames = helpers.Select(static helper => helper.HelperName!).Concat(trackedNames).Distinct(StringComparer.Ordinal).ToArray();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var helper in helpers)
                {
                    if (!trackedNames.Contains(helper.HelperName!) && CalledHelpers(helper, allNames).Overlaps(trackedNames))
                    {
                        trackedNames.Add(helper.HelperName!);
                        changed = true;
                    }
                }
            }
        }

        private static void AppendMapping(StringBuilder builder, MappingModel mapping, HashSet<string> recursiveHelperNames)
        {
            var method = mapping.Method;
            var parameter = method.Parameters[0];
            var destination = mapping.DestinationParameter;
            var trackerLocal = CreateUniqueIdentifier(method, "__tracker");
            var memberPathLocal = CreateUniqueIdentifier(method, "__memberPath", trackerLocal);
            var destinationCreatedLocal = CreateUniqueIdentifier(method, "__destinationCreated", trackerLocal, memberPathLocal);
            var targetLocal = CreateUniqueIdentifier(method, "target", trackerLocal, memberPathLocal, destinationCreatedLocal);
            var trackedHelper = mapping.HelperName != null && recursiveHelperNames.Contains(mapping.HelperName);
            var trackedPublicEntry = mapping.HelperName == null && mapping.ReferenceHandling == ReferenceHandlingThrowOnCycle && recursiveHelperNames.Count != 0;
            if (mapping.EmitAggressiveInlining)
            {
                builder.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            }

            builder.Append("    ");
            builder.Append(mapping.HelperName == null ? ToAccessibility(method.DeclaredAccessibility) : "private");
            builder.Append(' ');
            if (method.IsStatic || mapping.HelperName != null && !mapping.IsDeclaredCore)
            {
                builder.Append("static ");
            }

            if (mapping.HelperName == null)
            {
                builder.Append("partial ");
            }

            builder.Append(mapping.IsUpdate && method.ReturnsVoid ? "void" : DisplayType(mapping.HelperTargetType ?? method.ReturnType));
            builder.Append(' ');
            builder.Append(EscapeIdentifier(mapping.HelperName ?? method.Name));
            builder.Append('(');
            builder.Append(mapping.HelperSourceType == null ? DisplayType(parameter.Type) : DisplayType(mapping.HelperSourceType).TrimEnd('?'));
            builder.Append(' ');
            builder.Append(EscapeIdentifier(parameter.Name));
            if (mapping.IsUpdate && destination != null)
            {
                builder.Append(", ");
                if (destination.RefKind == RefKind.Ref)
                {
                    builder.Append("ref ");
                }

                builder.Append(DisplayType(destination.Type));
                builder.Append(' ');
                builder.Append(EscapeIdentifier(destination.Name));
            }
            if (trackedHelper)
            {
                builder.Append(", __LiteMapperCycleTracker " + trackerLocal + ", string " + memberPathLocal);
            }

            builder.AppendLine(")");
            builder.AppendLine("    {");

            if (mapping.SourceNullable)
            {
                builder.Append("        if (");
                builder.Append(EscapeIdentifier(parameter.Name));
                builder.AppendLine(" == null)");
                builder.AppendLine("        {");
                if (mapping.ReturnNullable)
                {
                    builder.AppendLine("            return null;");
                }
                else
                {
                    builder.Append("            throw new global::System.ArgumentNullException(nameof(");
                    builder.Append(EscapeIdentifier(parameter.Name));
                    builder.AppendLine("));");
                }

                builder.AppendLine("        }");
            }
            else if (mapping.GuardNonNullSource && !parameter.Type.IsValueType)
            {
                builder.Append("        if (");
                builder.Append(EscapeIdentifier(parameter.Name));
                builder.AppendLine(" == null)");
                builder.AppendLine("        {");
                builder.Append("            throw new global::System.ArgumentNullException(nameof(");
                builder.Append(EscapeIdentifier(parameter.Name));
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

            if (trackedPublicEntry)
            {
                builder.AppendLine("        var " + trackerLocal + " = new __LiteMapperCycleTracker();");
                if (!parameter.Type.IsValueType)
                {
                builder.AppendLine("        " + trackerLocal + ".Enter(" + EscapeIdentifier(parameter.Name) + "!, typeof(" + DisplayType(parameter.Type).TrimEnd('?') + "), typeof(" + DisplayType(method.ReturnType).TrimEnd('?') + "), \"" + method.Name + "\", string.Empty);");
                }
            }

            if (trackedHelper && mapping.HelperSourceType != null && !mapping.HelperSourceType.IsValueType)
            {
                builder.AppendLine("        " + trackerLocal + ".Enter(" + EscapeIdentifier(parameter.Name) + "!, typeof(" + DisplayType(mapping.HelperSourceType).TrimEnd('?') + "), typeof(" + DisplayType(mapping.HelperTargetType!).TrimEnd('?') + "), \"" + method.Name + "\", " + memberPathLocal + ");");
                builder.AppendLine("        try");
                builder.AppendLine("        {");
            }

            if (mapping.CustomBody != null)
            {
                builder.Append(RewriteTrackedCalls(mapping.CustomBody, recursiveHelperNames, memberPathLocal, trackerLocal));
                if (trackedHelper && mapping.HelperSourceType != null && !mapping.HelperSourceType.IsValueType)
                {
                    builder.AppendLine("        }");
                    builder.AppendLine("        finally");
                    builder.AppendLine("        {");
                    builder.AppendLine("            " + trackerLocal + ".Exit(" + EscapeIdentifier(parameter.Name) + "!);");
                    builder.AppendLine("        }");
                }
                builder.AppendLine("    }");
                return;
            }

            if (mapping.IsUpdate && destination != null)
            {
                var creationAssignments = mapping.Assignments.Where(static assignment => assignment.InitializeDuringConstruction).ToArray();
                var boundNames = new HashSet<string>(mapping.Construction?.BoundTargetMembers.Select(static member => member.Name) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
                var needsCreationFlag = mapping.Assignments.Any(assignment => boundNames.Contains(assignment.TargetName) || assignment.InitializeDuringConstruction);
                if (needsCreationFlag)
                {
                    builder.AppendLine("        var " + destinationCreatedLocal + " = false;");
                }

                if (!destination.Type.IsValueType)
                {
                    builder.Append("        if (");
                    builder.Append(EscapeIdentifier(destination.Name));
                    builder.AppendLine(" == null)");
                    builder.AppendLine("        {");
                    if (!method.ReturnsVoid && mapping.DestinationNullable)
                    {
                        builder.Append("            ");
                        builder.Append(EscapeIdentifier(destination.Name));
                        builder.Append(" = new ");
                        builder.Append(DisplayType(destination.Type).TrimEnd('?'));
                        builder.Append('(');
                        if (mapping.Construction != null)
                        {
                            builder.Append(string.Join(", ", mapping.Construction.Arguments.Select(argument => EscapeIdentifier(argument.ParameterName) + ": " + RewriteTrackedCalls(argument.Expression, recursiveHelperNames, PathExpression(mapping, argument.ParameterName, memberPathLocal), trackerLocal))));
                        }
                        builder.Append(')');
                        if (creationAssignments.Length > 0)
                        {
                            builder.Append(" { ");
                            builder.Append(string.Join(", ", creationAssignments.Select(assignment => EscapeIdentifier(assignment.TargetName) + " = " + RewriteTrackedCalls(assignment.InitializationExpression, recursiveHelperNames, PathExpression(mapping, assignment.TargetName, memberPathLocal), trackerLocal))));
                            builder.Append(" }");
                        }
                        builder.AppendLine(";");
                        if (needsCreationFlag)
                        {
                            builder.AppendLine("            " + destinationCreatedLocal + " = true;");
                        }
                    }
                    else
                    {
                        builder.Append("            throw new global::System.ArgumentNullException(nameof(");
                        builder.Append(EscapeIdentifier(destination.Name));
                        builder.AppendLine("));");
                    }

                    builder.AppendLine("        }");
                }

                foreach (var assignment in mapping.Assignments)
                {
                    var guard = assignment.Guard;
                    if (boundNames.Contains(assignment.TargetName) || assignment.InitializeDuringConstruction)
                    {
                        guard = guard == null ? "!" + destinationCreatedLocal : "!" + destinationCreatedLocal + " && (" + guard + ")";
                    }
                    if (guard != null)
                    {
                        builder.Append("        if (");
                        builder.Append(guard);
                        builder.AppendLine(")");
                        builder.AppendLine("        {");
                        builder.Append("            ");
                    }
                    else
                    {
                        builder.Append("        ");
                    }

                    if (!assignment.IsStatement)
                    {
                        builder.Append(EscapeIdentifier(destination.Name));
                        builder.Append('.');
                        builder.Append(EscapeIdentifier(assignment.TargetName));
                        builder.Append(" = ");
                    }
                    builder.Append(RewriteTrackedCalls(assignment.Expression, recursiveHelperNames, PathExpression(mapping, assignment.TargetName, memberPathLocal), trackerLocal));
                    builder.AppendLine(";");
                    if (guard != null)
                    {
                        builder.AppendLine("        }");
                    }
                }

                if (!method.ReturnsVoid)
                {
                    builder.Append("        return ");
                    builder.Append(EscapeIdentifier(destination.Name));
                    builder.AppendLine(";");
                }

                builder.AppendLine("    }");
                return;
            }

            builder.Append("        var " + targetLocal + " = new ");
            builder.Append(DisplayType(mapping.HelperTargetType ?? method.ReturnType).TrimEnd('?'));
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
                    builder.Append(EscapeIdentifier(argument.ParameterName));
                    builder.Append(": ");
                    builder.Append(RewriteTrackedCalls(argument.Expression, recursiveHelperNames, PathExpression(mapping, argument.ParameterName, memberPathLocal), trackerLocal));
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
                    builder.Append(EscapeIdentifier(assignment.TargetName));
                    builder.Append(" = ");
                    builder.Append(RewriteTrackedCalls(assignment.Expression, recursiveHelperNames, PathExpression(mapping, assignment.TargetName, memberPathLocal), trackerLocal));
                    builder.AppendLine(i == mapping.Assignments.Length - 1 ? string.Empty : ",");
                }

                builder.AppendLine("        };");
            }

            builder.AppendLine("        return " + targetLocal + ";");
            if (trackedHelper && mapping.HelperSourceType != null && !mapping.HelperSourceType.IsValueType)
            {
                builder.AppendLine("        }");
                builder.AppendLine("        finally");
                builder.AppendLine("        {");
                builder.AppendLine("            " + trackerLocal + ".Exit(" + EscapeIdentifier(parameter.Name) + "!);");
                builder.AppendLine("        }");
            }
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

        private static HashSet<string> FindRecursiveHelperNames(MappingModel mapping)
        {
            var helpers = FlattenHelpers(mapping).Where(static h => h.HelperName != null).ToArray();
            var names = helpers.Select(static h => h.HelperName!).ToArray();
            var edges = helpers.ToDictionary(static h => h.HelperName!, h => CalledHelpers(h, names), StringComparer.Ordinal);
            var recursive = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (CanReach(name, name, edges, new HashSet<string>(StringComparer.Ordinal)))
                {
                    recursive.Add(name);
                }
            }

            return recursive;
        }

        private static HashSet<string> CalledHelpers(MappingModel mapping, string[] helperNames)
        {
            var text = new StringBuilder();
            if (mapping.CustomBody != null)
            {
                text.Append(mapping.CustomBody);
            }

            foreach (var assignment in mapping.Assignments)
            {
                text.Append(assignment.Expression);
            }

            if (mapping.Construction != null)
            {
                foreach (var argument in mapping.Construction.Arguments)
                {
                    text.Append(argument.Expression);
                }
            }

            var value = text.ToString();
            return new HashSet<string>(helperNames.Where(name => value.IndexOf(name + "(", StringComparison.Ordinal) >= 0), StringComparer.Ordinal);
        }

        private static bool CanReach(string current, string target, Dictionary<string, HashSet<string>> edges, HashSet<string> visited)
        {
            if (!edges.TryGetValue(current, out var next))
            {
                return false;
            }

            foreach (var item in next)
            {
                if (item == target)
                {
                    return true;
                }

                if (visited.Add(item) && CanReach(item, target, edges, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static string PathExpression(MappingModel mapping, string memberName, string memberPathName)
        {
            if (mapping.HelperName == null)
            {
                return "\"" + memberName + "\"";
            }
            return mapping.IsDeclaredCore
                ? "(" + memberPathName + ".Length == 0 ? \"" + memberName + "\" : " + memberPathName + " + \"." + memberName + "\")"
                : memberPathName + " + \"." + memberName + "\"";
        }

        private static string RewriteTrackedCalls(string expression, HashSet<string> recursiveHelperNames, string pathExpression, string trackerName)
        {
            foreach (var helperName in recursiveHelperNames.OrderByDescending(static n => n.Length))
            {
                expression = RewriteTrackedCall(expression, helperName, pathExpression, trackerName);
            }

            return expression;
        }

        private static string RewriteTrackedCall(string expression, string helperName, string pathExpression, string trackerName)
        {
            var search = helperName + "(";
            var index = expression.IndexOf(search, StringComparison.Ordinal);
            while (index >= 0)
            {
                var argumentStart = index + search.Length;
                var depth = 1;
                var position = argumentStart;
                while (position < expression.Length && depth != 0)
                {
                    if (expression[position] == '(')
                    {
                        depth++;
                    }
                    else if (expression[position] == ')')
                    {
                        depth--;
                    }

                    position++;
                }

                if (depth == 0)
                {
                    expression = expression.Insert(position - 1, ", " + trackerName + ", " + pathExpression);
                    index = expression.IndexOf(search, position + pathExpression.Length, StringComparison.Ordinal);
                }
                else
                {
                    break;
                }
            }

            return expression;
        }

        private static void AppendCycleTracker(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("    private sealed class __LiteMapperCycleTracker");
            builder.AppendLine("    {");
            builder.AppendLine("        private readonly global::System.Collections.Generic.HashSet<object> _active = new global::System.Collections.Generic.HashSet<object>(__ReferenceIdentityComparer.Instance);");
            builder.AppendLine();
            builder.AppendLine("        public void Enter(object source, global::System.Type sourceType, global::System.Type destinationType, string mappingMethod, string memberPath)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (!_active.Add(source))");
            builder.AppendLine("            {");
            builder.AppendLine("                throw new global::Mammoth.LiteMapper.LiteMapperCycleException(sourceType, destinationType, mappingMethod, memberPath);");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        public void Exit(object source)");
            builder.AppendLine("        {");
            builder.AppendLine("            _active.Remove(source);");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private sealed class __ReferenceIdentityComparer : global::System.Collections.Generic.IEqualityComparer<object>");
            builder.AppendLine("    {");
            builder.AppendLine("        public static readonly __ReferenceIdentityComparer Instance = new __ReferenceIdentityComparer();");
            builder.AppendLine("        public new bool Equals(object? x, object? y) => object.ReferenceEquals(x, y);");
            builder.AppendLine("        public int GetHashCode(object obj) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);");
            builder.AppendLine("    }");
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

        private static string DisplayType(ITypeSymbol type)
        {
            var format = IsCollectionType(type, null) || type.ContainingType != null || type.ContainingNamespace?.IsGlobalNamespace == false
                ? SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)
                : SymbolDisplayFormat.MinimallyQualifiedFormat;
            return type.ToDisplayString(format);
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
            public MapperModel(string displayName, Location location, string hintName, string source, ImmutableArray<Diagnostic> diagnostics, bool canGenerate)
            {
                DisplayName = displayName;
                Location = location;
                HintName = hintName;
                Source = source;
                Diagnostics = diagnostics;
                CanGenerate = canGenerate;
            }

            public string DisplayName { get; }

            public Location Location { get; }

            public string HintName { get; }

            public string Source { get; }

            public ImmutableArray<Diagnostic> Diagnostics { get; }

            public bool CanGenerate { get; }
        }

        private readonly struct GeneratorOptions : IEquatable<GeneratorOptions>
        {
            private const string EmitDebugMetadataKey = "build_property.LiteMapper_EmitDebugMetadata";
            private const string IncludeGeneratedSourceCommentsKey = "build_property.LiteMapper_IncludeGeneratedSourceComments";
            private const string TreatInternalGeneratorErrorsAsExceptionsKey = "build_property.LiteMapper_TreatInternalGeneratorErrorsAsExceptions";

            public GeneratorOptions(bool emitDebugMetadata, bool includeGeneratedSourceComments, bool treatInternalGeneratorErrorsAsExceptions)
            {
                EmitDebugMetadata = emitDebugMetadata;
                IncludeGeneratedSourceComments = includeGeneratedSourceComments;
                TreatInternalGeneratorErrorsAsExceptions = treatInternalGeneratorErrorsAsExceptions;
            }

            public bool EmitDebugMetadata { get; }

            public bool IncludeGeneratedSourceComments { get; }

            public bool TreatInternalGeneratorErrorsAsExceptions { get; }

            public static GeneratorOptions From(AnalyzerConfigOptions options)
            {
                return new GeneratorOptions(
                    IsEnabled(options, EmitDebugMetadataKey),
                    IsEnabled(options, IncludeGeneratedSourceCommentsKey),
                    IsEnabled(options, TreatInternalGeneratorErrorsAsExceptionsKey));
            }

            public bool Equals(GeneratorOptions other)
            {
                return EmitDebugMetadata == other.EmitDebugMetadata &&
                    IncludeGeneratedSourceComments == other.IncludeGeneratedSourceComments &&
                    TreatInternalGeneratorErrorsAsExceptions == other.TreatInternalGeneratorErrorsAsExceptions;
            }

            public override bool Equals(object? obj)
            {
                return obj is GeneratorOptions other && Equals(other);
            }

            public override int GetHashCode()
            {
                var hash = EmitDebugMetadata ? 1 : 0;
                hash = hash * 397 ^ (IncludeGeneratedSourceComments ? 1 : 0);
                hash = hash * 397 ^ (TreatInternalGeneratorErrorsAsExceptions ? 1 : 0);
                return hash;
            }

            private static bool IsEnabled(AnalyzerConfigOptions options, string key)
            {
                return options.TryGetValue(key, out var value) &&
                    (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(value, "1", StringComparison.Ordinal));
            }
        }

        private sealed class MappingModel
        {
            public MappingModel(IMethodSymbol method, bool sourceNullable, bool returnNullable, string nullableMismatch, string referenceHandling, bool guardNonNullSource, ConstructionModel? construction, ImmutableArray<PreconditionModel> preconditions, ImmutableArray<AssignmentModel> assignments, ImmutableArray<MappingModel> helpers, string? helperName, ITypeSymbol? helperSourceType, ITypeSymbol? helperTargetType, string? customBody, bool isUpdate = false, IParameterSymbol? destinationParameter = null, bool destinationNullable = false, bool isDeclaredCore = false, bool emitAggressiveInlining = false)
            {
                Method = method;
                SourceNullable = sourceNullable;
                ReturnNullable = returnNullable;
                NullableMismatch = nullableMismatch;
                ReferenceHandling = referenceHandling;
                GuardNonNullSource = guardNonNullSource;
                Construction = construction;
                Preconditions = preconditions;
                Assignments = assignments;
                Helpers = helpers;
                HelperName = helperName;
                HelperSourceType = helperSourceType;
                HelperTargetType = helperTargetType;
                CustomBody = customBody;
                IsUpdate = isUpdate;
                DestinationParameter = destinationParameter;
                DestinationNullable = destinationNullable;
                IsDeclaredCore = isDeclaredCore;
                EmitAggressiveInlining = emitAggressiveInlining;
            }

            public IMethodSymbol Method { get; }

            public bool SourceNullable { get; }

            public bool ReturnNullable { get; }

            public string NullableMismatch { get; }

            public string ReferenceHandling { get; }

            public bool GuardNonNullSource { get; }

            public ConstructionModel? Construction { get; }

            public ImmutableArray<PreconditionModel> Preconditions { get; }

            public ImmutableArray<AssignmentModel> Assignments { get; }

            public ImmutableArray<MappingModel> Helpers { get; }

            public string? HelperName { get; }

            public ITypeSymbol? HelperSourceType { get; }

            public ITypeSymbol? HelperTargetType { get; }

            public string? CustomBody { get; }

            public bool IsUpdate { get; }

            public IParameterSymbol? DestinationParameter { get; }

            public bool DestinationNullable { get; }

            public bool IsDeclaredCore { get; }

            public bool EmitAggressiveInlining { get; }
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
            public ConstructorArgumentModel(string parameterName, string expression, ISymbol? sourceMember)
            {
                ParameterName = parameterName;
                Expression = expression;
                SourceMember = sourceMember;
            }

            public string ParameterName { get; }

            public string Expression { get; }

            public ISymbol? SourceMember { get; }
        }

        private sealed class AssignmentModel
        {
            public AssignmentModel(string targetName, string expression, string? guard = null, bool initializeDuringConstruction = false, bool isStatement = false, string? initializationExpression = null, bool isDirectMemberCopy = false)
            {
                TargetName = targetName;
                Expression = expression;
                Guard = guard;
                InitializeDuringConstruction = initializeDuringConstruction;
                IsStatement = isStatement;
                InitializationExpression = initializationExpression ?? expression;
                IsDirectMemberCopy = isDirectMemberCopy;
            }

            public string TargetName { get; }

            public string Expression { get; }

            public string? Guard { get; }

            public bool InitializeDuringConstruction { get; }

            public bool IsStatement { get; }

            public string InitializationExpression { get; }

            public bool IsDirectMemberCopy { get; }
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
            public ConversionModel(string expression, ISymbol? sourceMember, bool potentiallyNull, string memberPath, string? nullCheckExpression, bool requiresNonNullSourceExpression = false)
            {
                Expression = expression;
                SourceMember = sourceMember;
                PotentiallyNull = potentiallyNull;
                MemberPath = memberPath;
                NullCheckExpression = nullCheckExpression;
                RequiresNonNullSourceExpression = requiresNonNullSourceExpression;
            }

            public string Expression { get; }

            public ISymbol? SourceMember { get; }

            public bool PotentiallyNull { get; }

            public string MemberPath { get; }

            public string? NullCheckExpression { get; }

            public bool RequiresNonNullSourceExpression { get; }
        }

        private sealed class EffectiveMappingOptions
        {
            public EffectiveMappingOptions(string nameMatching, string unmappedTargetMembers, string unmappedSourceMembers, string nullableMismatch, string nullCollections, bool nullCollectionsExplicit, bool guardNonNullSource, bool ignoreNullSourceMembers, string enumMapping, string enumNumericConversion, string unmatchedEnumValues, string referenceHandling, string numericConversion, bool allowExplicitOperators)
            {
                NameMatching = nameMatching;
                UnmappedTargetMembers = unmappedTargetMembers;
                UnmappedSourceMembers = unmappedSourceMembers;
                NullableMismatch = nullableMismatch;
                NullCollections = nullCollections;
                NullCollectionsExplicit = nullCollectionsExplicit;
                GuardNonNullSource = guardNonNullSource;
                IgnoreNullSourceMembers = ignoreNullSourceMembers;
                EnumMapping = enumMapping;
                EnumNumericConversion = enumNumericConversion;
                UnmatchedEnumValues = unmatchedEnumValues;
                ReferenceHandling = referenceHandling;
                NumericConversion = numericConversion;
                AllowExplicitOperators = allowExplicitOperators;
            }

            public string NameMatching { get; }

            public string UnmappedTargetMembers { get; }

            public string UnmappedSourceMembers { get; }

            public string NullableMismatch { get; }

            public string NullCollections { get; }

            public bool NullCollectionsExplicit { get; }

            public bool GuardNonNullSource { get; }

            public bool IgnoreNullSourceMembers { get; }

            public string EnumMapping { get; }

            public string EnumNumericConversion { get; }

            public string UnmatchedEnumValues { get; }

            public string ReferenceHandling { get; }

            public string NumericConversion { get; }

            public bool AllowExplicitOperators { get; }
        }

        private sealed class EnumMemberModel
        {
            public EnumMemberModel(string sourceName, ulong value, string targetName, ulong targetValue, bool targetIsExpression = false)
            {
                SourceName = sourceName;
                Value = value;
                TargetName = targetName;
                TargetValue = targetValue;
                TargetIsExpression = targetIsExpression;
            }

            public string SourceName { get; }

            public ulong Value { get; }

            public string TargetName { get; }

            public ulong TargetValue { get; }

            public bool TargetIsExpression { get; }
        }

        private enum CollectionKind
        {
            Unsupported,
            Array,
            List,
            Set,
            Dictionary
        }

        private sealed class CollectionShape
        {
            public CollectionShape(CollectionKind kind, ITypeSymbol elementType, ITypeSymbol? keyType, bool isDictionary, bool isRectangularArray, bool unsupported, bool custom)
            {
                Kind = kind;
                ElementType = elementType;
                KeyType = keyType;
                IsDictionary = isDictionary;
                IsRectangularArray = isRectangularArray;
                Unsupported = unsupported;
                Custom = custom;
            }

            public CollectionKind Kind { get; }

            public ITypeSymbol ElementType { get; }

            public ITypeSymbol? KeyType { get; }

            public bool IsDictionary { get; }

            public bool IsRectangularArray { get; }

            public bool Unsupported { get; }

            public bool Custom { get; }
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
