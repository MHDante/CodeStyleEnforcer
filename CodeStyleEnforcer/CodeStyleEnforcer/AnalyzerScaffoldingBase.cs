using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeStyleEnforcer
{
	public enum Categories
	{
		Style,
	}

	[AttributeUsage(AttributeTargets.Method)]
	public class DiagnosticAttribute : Attribute
	{
		public String Id { get; private set; }
		public string Title { get; private set; }
		public bool IsEnabledByDefault { get; private set; }
		public string Description { get; private set; }
		public string HelpLinkUri { get; private set; }
		public string MessageFormat { get; private set; }
		public Categories Category { get; private set; }
		public DiagnosticSeverity DefaultSeverity { get; private set; }
		public string[] CustomTags { get; private set; }
		public SyntaxKind[] SyntaxKinds { get; private set; }
		public DiagnosticDescriptor Descriptor { get; private set; }

		public DiagnosticAttribute(params SyntaxKind[] syntaxKinds) : this(kinds: syntaxKinds) { }
		public DiagnosticAttribute(string title = null, bool isEnabledByDefault = true, string description = null, string helpLinkUri = null, string messageFormat = null, Categories category = Categories.Style, DiagnosticSeverity defaultSeverity = CodeStyleEnforcerAnalyzer.DefaultSeverity, string[] customTags = null, params SyntaxKind[] kinds)
		{
			SyntaxKinds = kinds;
			Title = title;
			IsEnabledByDefault = isEnabledByDefault;
			Description = description;
			HelpLinkUri = helpLinkUri;
			MessageFormat = messageFormat;
			Category = category;
			DefaultSeverity = defaultSeverity;
			CustomTags = customTags?? new string[0];
		}

		public DiagnosticAttribute Initialize(MethodInfo method)
		{
			Id = method.Name;
			Descriptor = new DiagnosticDescriptor(
					   Id,
				       Title ?? method.Name,
				       MessageFormat ?? method.Name,
				       Enum.GetName(typeof(Categories), Category),
				       DefaultSeverity,
				       IsEnabledByDefault,
				       Description,
				       HelpLinkUri,
				       CustomTags
			       );
			return this;
		}
	}

	public delegate Task<Solution> SolutionFixDelegate(Document document, Diagnostic diagnostic, CancellationToken cToken);
	public delegate Task<Document> DocumentFixDelegate(Document document, Diagnostic diagnostic, CancellationToken cToken);


	[AttributeUsage(AttributeTargets.Method)]
	class CodeFixAttribute : Attribute
	{
		public string Title;
		public Delegate Delegate;
		public string Id;
		public string DiagnosticName;
		
		public CodeFixAttribute(string diagnosticName, string title = null)
		{
			DiagnosticName = diagnosticName;
			Title = title;
		}

		public CodeFixAttribute Initialize(MethodInfo mi)
		{
			Title = Title ?? mi.Name;
			if(mi.ReturnType == typeof(Task<Solution>)) Delegate = mi.CreateDelegate(typeof(SolutionFixDelegate));
			else if(mi.ReturnType == typeof(Task<Document>)) Delegate = mi.CreateDelegate(typeof(DocumentFixDelegate));
			else throw new Exception("Template must return Solution or Document");
			Id = mi.Name;
			return this;
		}
	}
	
	public abstract class AnalyzerScaffolding<TAnalyzer, TFixProvider> where TAnalyzer : DiagnosticAnalyzer where TFixProvider : CodeFixProvider
	{
		static AnalyzerScaffolding()
		{
			_DiagnosticAttributesById = typeof(TAnalyzer).GetTypeInfo().DeclaredMethods
				.Select(mi => mi.GetCustomAttribute<DiagnosticAttribute>()?.Initialize(mi))
				.Where(a => a != null)
				.ToDictionary(a => a.Id);

			var descriptors = _DiagnosticAttributesById.Values
				.Select(a=> a.Descriptor)
				.ToArray();
			

			_supportedDiagnostics = ImmutableArray.Create(descriptors);

			_fixAttributesById = typeof(TFixProvider).GetTypeInfo().DeclaredMethods
				.Select(mi => mi.GetCustomAttribute<CodeFixAttribute>()?.Initialize(mi))
				.Where(a => a != null).ToDictionary(a=>a.Id);
			
			_diagnosticIdToFixIds = _DiagnosticAttributesById.Keys
				.ToDictionary(
				a=>a,
				a => _fixAttributesById.Where(kvp=>kvp.Value.DiagnosticName == a).Select(kvp=>kvp.Key).ToArray()
				);

			FixableDescriptors = ImmutableArray.Create(_diagnosticIdToFixIds.Keys.ToArray());
		}


		public static ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics;
		public static readonly Dictionary<string, DiagnosticAttribute> _DiagnosticAttributesById = new Dictionary<string, DiagnosticAttribute>();
		public static ImmutableArray<string> FixableDescriptors;
		private static Dictionary<string, string[]> _diagnosticIdToFixIds;
		private static Dictionary<string, CodeFixAttribute> _fixAttributesById = new Dictionary<string, CodeFixAttribute>();

		public static DiagnosticDescriptor GetDescriptorByMethodName(string methodName) 
			=> _DiagnosticAttributesById[methodName].Descriptor;

		public static void RegisterFixes(CodeFixContext context, ImmutableArray<Diagnostic> diagnostics)
		{
			foreach (var diagnostic in diagnostics)
			{

				var diagId = diagnostic.Id;
				if (!_diagnosticIdToFixIds.ContainsKey(diagId))
					throw new InvalidDataException("Missing fix for error: " + diagId);
				foreach (var fixId in _diagnosticIdToFixIds[diagId])
				{
					var fixAttr = _fixAttributesById[fixId];
					SolutionFixDelegate sDelegate = fixAttr.Delegate as SolutionFixDelegate;
					DocumentFixDelegate dDelegate = fixAttr.Delegate as DocumentFixDelegate;


					if (sDelegate != null)
					{
						context.RegisterCodeFix(
							CodeAction.Create(
								fixAttr.Title,
								c => sDelegate(context.Document, diagnostic, c),
								fixAttr.Id),
							diagnostic);
					}
					else if (dDelegate != null)
					{
						context.RegisterCodeFix(
							CodeAction.Create(
								fixAttr.Title,
								c => dDelegate(context.Document, diagnostic, c),
								fixAttr.Id),
							diagnostic);
					}
				}
			}
		}
	}
}