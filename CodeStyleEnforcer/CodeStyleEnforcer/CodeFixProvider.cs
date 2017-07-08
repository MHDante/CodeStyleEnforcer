using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace CodeStyleEnforcer
{

	using static AnalyzerScaffolding<CodeStyleEnforcerAnalyzer, CodeStyleEnforcerCodeFixProvider>;
	using static CodeStyleEnforcerAnalyzer;

	[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CodeStyleEnforcerCodeFixProvider)), Shared]
    public class CodeStyleEnforcerCodeFixProvider : CodeFixProvider
    {
	    public sealed override ImmutableArray<string> FixableDiagnosticIds => FixableDescriptors;
        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

	    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
	    {
		    RegisterFixes(context, context.Diagnostics);
	    }
		
		[CodeFix(nameof(ClosingBraceMustHaveComment))]
	    public static async Task<Document> AddEndCommentAsync(Document document, Diagnostic diagnostic,
		    CancellationToken cancellationToken)
	    {
		    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
			var token = root.FindToken(diagnostic.Location.SourceSpan.End);
		    var comment = SyntaxFactory.Comment(diagnostic.Properties["TargetComment"]);
		    SyntaxNode newRoot;
		    var triviaToAdd = new[] {comment, SyntaxFactory.CarriageReturnLineFeed};

			if (token.HasTrailingTrivia)
		    {
			    newRoot = root.InsertTriviaBefore(token.TrailingTrivia.First(), triviaToAdd);
		    }
		    else
		    {
			    var newNode = token.Parent.WithTrailingTrivia(triviaToAdd);
			    newRoot = root.ReplaceNode(token.Parent, newNode);
		    }

		    return document.WithSyntaxRoot(newRoot);
	    }
		
	    [CodeFix(nameof(FileMustEndInNewLine))]
	    public static async Task<Document> AddNewAtLineEndOfFile(Document document, Diagnostic diagnostic,
		    CancellationToken cancellationToken)
	    {
		    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		    var newline = new[] {SyntaxFactory.CarriageReturnLineFeed};

		    SyntaxNode newRoot = root.HasTrailingTrivia 
			    ? root.InsertTriviaAfter(root.GetTrailingTrivia().Last(), newline) 
			    : root.WithTrailingTrivia(newline);

		    return document.WithSyntaxRoot(newRoot);
	    }

		[CodeFix(nameof(EnumsMustEndInS))]
		public static async Task<Solution> RenameEnumAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
		{
		    var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		
			// Find the type declaration identified by the diagnostic.
			var diagnosticSpan = diagnostic.Location.SourceSpan;
			var token = root.FindToken(diagnosticSpan.Start);
		
		    var typeDecl = token.Parent.AncestorsAndSelf().OfType<EnumDeclarationSyntax>().First();
			// Compute new plural name.
			var identifierToken = typeDecl.Identifier;
		    var newName = identifierToken.Text+'s';
		
		    // Get the symbol representing the type to be renamed.
		    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
		    var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
		
		    // Produce a new solution that has all references to that type renamed, including the declaration.
		    var originalSolution = document.Project.Solution;
		    var optionSet = originalSolution.Workspace.Options;
		    var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);
		
		    // Return the new solution with the now-uppercase type name.
		    return newSolution;
		}
	}
}