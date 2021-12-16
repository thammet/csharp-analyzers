using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpAnalyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingSwaggerAnnotationAnalyzerCodeFixProvider)), Shared]
    public class MissingSwaggerAnnotationAnalyzerCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(MissingSwaggerAnnotationsAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.CodeFixTitle,
                    createChangedDocument: c => AddSwaggerAnnotation(context.Document, declaration, root, c),
                    equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                diagnostic);
        }

        private Task<Document> AddSwaggerAnnotation(Document document, MethodDeclarationSyntax methodDeclaration, SyntaxNode root, CancellationToken cancellationToken)
        {
            var attributeListSyntaxes = SyntaxFactory.AttributeList();

            attributeListSyntaxes = attributeListSyntaxes.AddAttributes(
                SyntaxFactory.Attribute(
                    SyntaxFactory.ParseName("ProducesResponseType"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SeparatedList<AttributeArgumentSyntax>()
                            .Add(SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression("Microsoft.AspNetCore.Http.StatusCodes.Status200OK")))
                            .Add(SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression($"Type = typeof({MissingSwaggerAnnotationsAnalyzer.ConvertTypeNameToSwaggerTypeName(methodDeclaration.ReturnType.ToString())})")))
                    )
                )
            );

            var newMethodDeclaration = SyntaxFactory.MethodDeclaration(
                methodDeclaration.AttributeLists.Add(attributeListSyntaxes),
                methodDeclaration.Modifiers,
                methodDeclaration.ReturnType,
                methodDeclaration.ExplicitInterfaceSpecifier,
                methodDeclaration.Identifier,
                methodDeclaration.TypeParameterList,
                methodDeclaration.ParameterList,
                methodDeclaration.ConstraintClauses,
                methodDeclaration.Body,
                methodDeclaration.ExpressionBody,
                methodDeclaration.SemicolonToken
           );

            var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);

            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }
    }
}
