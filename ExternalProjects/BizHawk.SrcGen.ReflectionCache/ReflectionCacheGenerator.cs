namespace BizHawk.SrcGen.ReflectionCache;

using System.Collections.Generic;
using System.Linq;
using System.Text;

using BizHawk.Analyzers;

[Generator]
public sealed class ReflectionCacheGenerator : IIncrementalGenerator
{
	private sealed class NamespaceInferrer
	{
		/// <remarks>
		/// I may have just added RNG to the build process...
		/// Increase this sample size to decrease chance of random failure.
		/// Alternatively, if you can come up with a better way of getting the project name in <see cref="ISourceGenerator.Execute"/> (I tried like 5 different things), do that instead.
		/// --yoshi
		/// </remarks>
		private const int SAMPLE_SIZE = 20;

		private string? _namespace;

		private readonly List<string> _namespaces = new();

		public string Namespace => _namespace ??= CalcNamespace();

		private string CalcNamespace()
		{
			if (_namespaces.Count is 0) return string.Empty; // this can happen if the Analyzer is applied to a project with no source files
			// black magic wizardry to find common prefix https://stackoverflow.com/a/35081977
			var ns = new string(_namespaces[0]
				.Substring(0, _namespaces.Min(s => s.Length))
				.TakeWhile((c, i) => _namespaces.TrueForAll(s => s[i] == c))
				.ToArray());
			return ns[ns.Length - 1] == '.' ? ns.Substring(0, ns.Length - 1) : ns; // trim trailing '.' (can't use BizHawk.Common from Source Generators)
		}

		public void AddSample(NamespaceDeclarationSyntax syn)
		{
			if (_namespace is not null) return;
			var newNS = syn.Name.ToMetadataNameStr();
			if (!newNS.StartsWith("BizHawk.", StringComparison.Ordinal)) return;
			_namespaces.Add(newNS);
			if (_namespaces.Count == SAMPLE_SIZE) _namespace = CalcNamespace();
		}
	}

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var nSpace = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (syntaxNode, _) => syntaxNode is NamespaceDeclarationSyntax,
				transform: static (ctx, _) => (NamespaceDeclarationSyntax) ctx.Node)
			.Collect()
			.Select((nsSyns, _) =>
			{
				NamespaceInferrer shim = new();
				foreach (var syn in nsSyns) shim.AddSample(syn);
				return shim.Namespace;
			});
		context.RegisterSourceOutput(nSpace, Execute);
	}

	public void Execute(SourceProductionContext context, string nSpace)
	{
		if (string.IsNullOrWhiteSpace(nSpace)) return; // see note in `CalcNamespace`
		var src = $@"#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
{(nSpace == "BizHawk.Common" ? string.Empty : "\nusing BizHawk.Common;")}
using BizHawk.Common.StringExtensions;

namespace {nSpace}
{{
	public static class ReflectionCache
	{{
		private const string EMBED_PREFIX = ""{nSpace}."";

		private static Type[]? _types = null;

		private static readonly Assembly Asm = typeof({nSpace}.ReflectionCache).Assembly;

		public static readonly Version AsmVersion = Asm.GetName().Version!;

		public static Type[] Types => _types ??= Asm.GetTypesWithoutLoadErrors().ToArray();

		public static IEnumerable<string> EmbeddedResourceList(string extraPrefix)
		{{
			var fullPrefix = EMBED_PREFIX + extraPrefix;
			return Asm.GetManifestResourceNames().Where(s => s.StartsWithOrdinal(fullPrefix)) // seems redundant with `RemovePrefix`, but we only want these in the final list
				.Select(s => s.RemovePrefix(fullPrefix));
		}}

		public static IEnumerable<string> EmbeddedResourceList()
			=> EmbeddedResourceList(string.Empty); // can't be simplified to `Asm.GetManifestResourceNames` call

		/// <exception cref=""ArgumentException"">not found</exception>
		public static Stream EmbeddedResourceStream(string embedPath)
		{{
			var fullPath = EMBED_PREFIX + embedPath;
			return Asm.GetManifestResourceStream(fullPath)
				?? throw new ArgumentException(paramName: nameof(embedPath), message: $""resource at {{fullPath}} not found"");
		}}
	}}
}}
";
		context.AddSource("ReflectionCache.cs", SourceText.From(src, Encoding.UTF8));
	}
}
