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
        private const string MappingOptionsAttributeName = "Mammoth.LiteMapper.MappingOptionsAttribute";
        private const string UseMapperAttributeName = "Mammoth.LiteMapper.UseMapperAttribute";

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

            foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>().Where(static m => m.PartialDefinitionPart == null && IsPartialDeclaration(m)))
            {
                ValidateMappingMethod(member, diagnostics);
            }

            var source = RenderDeclaration(symbol);
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

        private static string RenderDeclaration(INamedTypeSymbol symbol)
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

            builder.Append("partial ");
            builder.Append(symbol.IsStatic ? "static class " : "class ");
            builder.AppendLine(symbol.Name);
            builder.AppendLine("{");
            builder.AppendLine("}");

            foreach (var _ in ContainingTypes(symbol))
            {
                builder.AppendLine("}");
            }

            AppendNamespaceEnd(builder, symbol);
            return builder.ToString();
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
    }
}
