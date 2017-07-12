using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeStyleEnforcer
{
	using static AnalyzerScaffolding<CodeStyleEnforcerAnalyzer, CodeStyleEnforcerCodeFixProvider>;
	using static CodeStyleEnforcerCodeFixProvider;

	[DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CodeStyleEnforcerAnalyzer : DiagnosticAnalyzer
    {
		public const DiagnosticSeverity DefaultSeverity = DiagnosticSeverity.Error;
	    private const string SupportedNamespace = "VusrCore";
	    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => _supportedDiagnostics;
		public override void Initialize(AnalysisContext context)
        {
	        context.RegisterSyntaxNodeAction(FileMustEndInNewLine, SyntaxKind.CompilationUnit);
	        context.RegisterSyntaxNodeAction(ClosingBraceMustHaveComment, SyntaxKind.NamespaceDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.InterfaceDeclaration);
	        context.RegisterSyntaxNodeAction(MembersMustBePrecededByEmptyLine, SyntaxKind.InterfaceDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.NamespaceDeclaration);
	        context.RegisterSymbolAction(EnumsMustEndInS, SymbolKind.NamedType);
		}

		///Todo: register only on namespace
	    public static bool CheckSupport(SyntaxNode node)
	    {
		    bool foundSupportedNameSpace = false;
			var nsDeclarations = node.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>();
			foreach (var nsd in nsDeclarations)
			{
				foundSupportedNameSpace = nsd.Name.ToFullString().ToLowerInvariant().Contains(SupportedNamespace.ToLowerInvariant());
				if(foundSupportedNameSpace) break;
			}
		    return foundSupportedNameSpace;
	    }

	    [Diagnostic]
	    public static void EnumsMustEndInS(SymbolAnalysisContext obj)
	    {

		    if (!obj.Symbol.IsDefinition) return;

		    var symbol = obj.Symbol as INamedTypeSymbol;
		    if (symbol?.TypeKind != TypeKind.Enum) return;

		    if (symbol.Name.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return;

			var declSymbol = obj.Symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(obj.CancellationToken);
			if(!CheckSupport(declSymbol))return;

		    DiagnosticDescriptor descriptor = GetDescriptorByMethodName(nameof(EnumsMustEndInS));


		    foreach (var location in symbol.Locations)
			{
				var diagnostic = Diagnostic.Create(descriptor, location);
				obj.ReportDiagnostic(diagnostic);
			}
		}

	    [Diagnostic]
	    public static void FileMustEndInNewLine(SyntaxNodeAnalysisContext obj)
	    {
		    var nameSpaceNodes = obj.Node.ChildNodes().OfType<NamespaceDeclarationSyntax>();
			if (!nameSpaceNodes.Any(CheckSupport)) return;
			var members = obj.Node.ChildNodesAndTokens();
			var lastMember = members.LastOrDefault();
			if (lastMember==null || lastMember.Kind() != SyntaxKind.EndOfFileToken) return;
			
		    var penultimateToken = members[members.Count-2];
		    bool endsInNewline = penultimateToken.HasTrailingTrivia 
				&& penultimateToken.GetTrailingTrivia().Last().Kind() == SyntaxKind.EndOfLineTrivia;

		    if (endsInNewline) return;
			Location location = lastMember.GetLocation();
		    DiagnosticDescriptor descriptor = GetDescriptorByMethodName(nameof(FileMustEndInNewLine));
		    var diagnostic = Diagnostic.Create(descriptor, location);
		    obj.ReportDiagnostic(diagnostic);
	    }

	    [Diagnostic]
	    public static void MembersMustBePrecededByEmptyLine(SyntaxNodeAnalysisContext obj)
	    {

		    if (!CheckSupport(obj.Node)) return;

			var affected = new List<SyntaxKind>
		    {
			    SyntaxKind.InterfaceDeclaration,
			    SyntaxKind.ClassDeclaration,
			    SyntaxKind.EnumDeclaration,
			    SyntaxKind.NamespaceDeclaration,
			    SyntaxKind.MethodDeclaration
		    };

		    bool foundFirstBrace = false;
		    bool foundFirstMember = false;
		    foreach (var member in obj.Node.ChildNodesAndTokens())
		    {
			    if (!foundFirstBrace)
			    {
				    foundFirstBrace = member.Kind() == SyntaxKind.OpenBraceToken;
				    continue;
			    }
				if (member.IsToken) continue;
			    if (!foundFirstMember)
			    {
				    foundFirstMember = true;
					continue;
			    }
				if (!affected.Contains(member.Kind())) continue;
			    var hasline = member.HasLeadingTrivia;
			    var leadingTrivia = member.GetLeadingTrivia();
				hasline = hasline && member.GetLeadingTrivia().First().Kind() == SyntaxKind.EndOfLineTrivia;
			    if (!hasline && leadingTrivia.Count>1)
			    {
				    for (int i = -1; i < leadingTrivia.Count-1; i++)
				    {
					    if (i>=0 && !leadingTrivia[i].IsKind(SyntaxKind.EndOfLineTrivia)) continue;
					    for (int j = i+1; j < leadingTrivia.Count; j++)
					    {
						    if (leadingTrivia[j].IsKind(SyntaxKind.EndOfLineTrivia))
						    {
							    hasline = true;
							    break;
						    }
						    if (!leadingTrivia[j].IsKind(SyntaxKind.WhitespaceTrivia))
							    break;
					    }
					    if(hasline) break;
				    }
			    }

			    if (hasline) continue;
			    Location location = member.AsNode().GetFirstToken(true, true, true, true).Parent.GetLocation();
			    DiagnosticDescriptor descriptor = GetDescriptorByMethodName(nameof(MembersMustBePrecededByEmptyLine));
			    var diagnostic = Diagnostic.Create(descriptor, location);
			    obj.ReportDiagnostic(diagnostic);
			}
			

		    
	    }

		[Diagnostic]
		public static void ClosingBraceMustHaveComment(SyntaxNodeAnalysisContext obj)
		{

			if (!CheckSupport(obj.Node)) return;
			var node = obj.Node;
			var lastToken = node.GetLastToken();

			if (lastToken.Kind() != SyntaxKind.CloseBraceToken) return;
			
			string keyword = null;
			string name = null;
			var dec = obj.Node as TypeDeclarationSyntax;
			if (dec != null)
			{
				name = dec.Identifier.ToString();
				keyword = dec.Keyword.ToString();
			}
			var dec2 = obj.Node as NamespaceDeclarationSyntax;
			if (dec2 != null)
			{
				name = dec2.Name.ToString();
				keyword = dec2.NamespaceKeyword.ToString();
			}

			var targetComment = "// End "+ name + " " + keyword;



			if (lastToken.HasTrailingTrivia)
			{
				var trailingTrivia = lastToken.TrailingTrivia.First();
				if (trailingTrivia.Kind() == SyntaxKind.SingleLineCommentTrivia)
				{
					var fullComment = trailingTrivia.ToFullString();
					if (fullComment.Equals(targetComment, StringComparison.OrdinalIgnoreCase))
						return;
				}
			}
			Location location = node.GetLastToken().GetLocation();
			var props = ImmutableDictionary.CreateBuilder<string, string>();
			props.Add("TargetComment", targetComment);
			DiagnosticDescriptor descriptor = GetDescriptorByMethodName(nameof(ClosingBraceMustHaveComment));
			var diagnostic = Diagnostic.Create(descriptor, location, props.ToImmutable());
			obj.ReportDiagnostic(diagnostic);
		}
		
    }
	
}//End Class
