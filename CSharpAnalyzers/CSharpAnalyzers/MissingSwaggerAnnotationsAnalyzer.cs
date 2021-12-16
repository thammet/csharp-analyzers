using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CSharpAnalyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MissingSwaggerAnnotationsAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "MissingSwaggerAnnotations";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.SwaggerAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.SwaggerAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.SwaggerAnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Swagger";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
            // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Method);
        }

        private static void AnalyzeSymbol(SymbolAnalysisContext context)
        {
            var methodSymbol = (IMethodSymbol)context.Symbol;

            if (MethodHasVoidReturnType(context, methodSymbol) || !MethodIsAController(context, methodSymbol) ||!MethodHasInvalidSwaggerAnnotation(context, methodSymbol))
            {
                return;
            }

            var diagnostic = Diagnostic.Create(
                    descriptor: Rule,
                    location: methodSymbol.Locations[0],
                    messageArgs: methodSymbol.Name);

            context.ReportDiagnostic(diagnostic);
        }

        private static bool MethodIsAController(SymbolAnalysisContext context, IMethodSymbol methodSymbol)
        {
            return MethodHasControllerAttributes(context, methodSymbol) && InheritsFromController(context, methodSymbol);
        }

        private static bool MethodHasControllerAttributes(SymbolAnalysisContext context, IMethodSymbol methodSymbol)
        {
            var httpMethodAttributeSymbol = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute");
            return methodSymbol.GetAttributes().Any(a => InheritsFromSymbol(a.AttributeClass, httpMethodAttributeSymbol));
        }

        private static bool InheritsFromController(SymbolAnalysisContext context, IMethodSymbol methodSymbol)
        {
            var controllerSymbol = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Controller");
            return InheritsFromSymbol(methodSymbol.ContainingType, controllerSymbol);
        }

        private static bool InheritsFromSymbol(INamedTypeSymbol current, INamedTypeSymbol inheritedSymbol)
        {
            if(current == null || inheritedSymbol == null)
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(current, inheritedSymbol) || InheritsFromSymbol(current.BaseType, inheritedSymbol);
        }

        private static bool MethodHasVoidReturnType(SymbolAnalysisContext context, IMethodSymbol methodSymbol)
        {
            var taskSymbol = context.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            return methodSymbol.ReturnsVoid || SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, taskSymbol);
        }

        private static bool MethodHasInvalidSwaggerAnnotation(SymbolAnalysisContext context, IMethodSymbol methodSymbol)
        {
            var producesResponseTypeAttributeSymbol = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute");

            var attributes = methodSymbol
                .GetAttributes()
                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, producesResponseTypeAttributeSymbol))
                .ToList();

            if (!attributes.Any())
            {
                return true;
            }

            return attributes.All(attribute => ConvertTypeNameToSwaggerTypeName(methodSymbol.ReturnType.ToDisplayString()) != GetSwaggerType(attribute));
        }

        private static string GetSwaggerType(AttributeData attribute)
        {
            if (attribute.ConstructorArguments.Any())
            {
                foreach (var arg in attribute.ConstructorArguments)
                {
                    if (arg.Kind == TypedConstantKind.Type)
                    {
                        return arg.Value.ToString();
                    }
                }
            }

            return attribute.NamedArguments.First(arg => arg.Key == "Type").Value.Value.ToString();
        }

        public static string ConvertTypeNameToSwaggerTypeName(string typeName)
        {
            // I hate this!!! It works for most cases but is a bad pattern.
            return RemoveGenericFromTypeName(RemoveGenericFromTypeName(typeName, "Task"), "System.Threading.Tasks.Task");
        }

        private static string RemoveGenericFromTypeName(string typeName, string genericName)
        {
            var name = $"{genericName}<";
            return typeName.StartsWith(name) ? typeName.Substring(name.Length, typeName.Length - 1 - name.Length) : typeName;
        }
    }
}
