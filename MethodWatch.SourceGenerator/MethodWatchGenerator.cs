﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MethodWatch.SourceGenerator
{
    [Generator]
    public class MethodWatchGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new MethodWatchSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // Add a debug file to verify the generator is running
            context.AddSource("MethodWatchGenerator.debug", SourceText.From(@"
// This file is generated by MethodWatchGenerator
// If you can see this, the source generator is working
", Encoding.UTF8));

            // Get the syntax receiver
            if (!(context.SyntaxReceiver is MethodWatchSyntaxReceiver receiver))
                return;

            // Get the compilation
            var compilation = context.Compilation;

            // Get the MethodWatch attribute symbol
            var methodWatchAttributeSymbol = compilation.GetTypeByMetadataName("MethodWatch.MethodWatchAttribute");
            if (methodWatchAttributeSymbol == null)
                return;

            // Process each method with the MethodWatch attribute
            foreach (var methodDeclaration in receiver.MethodsWithAttribute)
            {
                var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
                if (methodSymbol == null) continue;

                // Check if the method has the MethodWatch attribute
                var hasMethodWatchAttribute = methodSymbol.GetAttributes()
                    .Any(attr => attr.AttributeClass?.Equals(methodWatchAttributeSymbol, SymbolEqualityComparer.Default) ?? false);

                if (!hasMethodWatchAttribute) continue;

                // Get the containing class
                var classDeclaration = methodDeclaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classDeclaration == null) continue;

                // Get the class name
                var className = classDeclaration.Identifier.Text;

                // Get the method name
                var methodName = methodDeclaration.Identifier.Text;

                // Get the return type
                var returnType = methodDeclaration.ReturnType.ToString();

                // Get the parameters
                var parameters = methodDeclaration.ParameterList.Parameters;
                var parameterList = string.Join(", ", parameters.Select(p => $"{p.Type} {p.Identifier}"));
                var argumentList = string.Join(", ", parameters.Select(p => p.Identifier.Text));

                // Get the attribute arguments
                var attribute = methodSymbol.GetAttributes()
                    .First(attr => attr.AttributeClass?.Equals(methodWatchAttributeSymbol, SymbolEqualityComparer.Default) ?? false);
                
                var thresholdMilliseconds = 0L;

                if (attribute.ConstructorArguments.Length > 0)
                {
                    thresholdMilliseconds = (long)attribute.ConstructorArguments[0].Value!;
                }

                // Generate the wrapper method
                var sourceBuilder = new StringBuilder(1024); // Pre-allocate buffer
                sourceBuilder.AppendLine("using System;");
                sourceBuilder.AppendLine("using System.Diagnostics;");
                sourceBuilder.AppendLine("using System.Text.Json;");
                sourceBuilder.AppendLine("using Microsoft.AspNetCore.Mvc;");
                sourceBuilder.AppendLine("using System.Threading.Tasks;");
                sourceBuilder.AppendLine("using Microsoft.Extensions.Logging;");
                sourceBuilder.AppendLine();
                sourceBuilder.AppendLine($"namespace {methodSymbol.ContainingNamespace}");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AppendLine($"    partial class {className}");
                sourceBuilder.AppendLine("    {");
                sourceBuilder.AppendLine($"        public {returnType} {methodName}_Watched({parameterList})");
                sourceBuilder.AppendLine("        {");
                sourceBuilder.AppendLine("            var sw = Stopwatch.StartNew();");
                sourceBuilder.AppendLine("            try");
                sourceBuilder.AppendLine("            {");
                sourceBuilder.Append("                _logger.LogInformation(\"MethodWatch: ").Append(methodName).AppendLine("_Watched called\");");

                // Add the method call
                if (returnType == "void")
                {
                    sourceBuilder.Append("                ").Append(methodName).Append("(").Append(argumentList).AppendLine(");");
                    sourceBuilder.AppendLine("                sw.Stop();");
                    sourceBuilder.AppendLine($"                var elapsedMs = sw.Elapsed.TotalMilliseconds;");
                    sourceBuilder.AppendLine($"                MethodWatch.RecordExecution(\"{methodName}\", (long)elapsedMs, {thresholdMilliseconds}, true);");
                }
                else
                {
                    sourceBuilder.Append("                var result = ").Append(methodName).Append("(").Append(argumentList).AppendLine(");");
                    sourceBuilder.AppendLine("                sw.Stop();");
                    sourceBuilder.AppendLine($"                var elapsedMs = sw.Elapsed.TotalMilliseconds;");
                    sourceBuilder.AppendLine($"                MethodWatch.RecordExecution(\"{methodName}\", (long)elapsedMs, {thresholdMilliseconds}, true);");
                    sourceBuilder.AppendLine("                return result;");
                }

                sourceBuilder.AppendLine("            }");
                sourceBuilder.AppendLine("            catch (Exception ex)");
                sourceBuilder.AppendLine("            {");
                sourceBuilder.AppendLine("                sw.Stop();");
                sourceBuilder.Append("                    _logger.LogError(ex, \"MethodWatch: ").Append(methodName).AppendLine("_Watched failed after {sw.Elapsed.TotalMilliseconds:F2} ms\");");
                sourceBuilder.AppendLine("                throw;");
                sourceBuilder.AppendLine("            }");
                sourceBuilder.AppendLine("        }");
                sourceBuilder.AppendLine("    }");
                sourceBuilder.AppendLine("}");

                // Add the source file
                context.AddSource($"{className}_{methodName}_MethodWatch.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
            }
        }
    }

    public class MethodWatchSyntaxReceiver : ISyntaxReceiver
    {
        public List<MethodDeclarationSyntax> MethodsWithAttribute { get; } = new List<MethodDeclarationSyntax>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is MethodDeclarationSyntax methodDeclaration)
            {
                if (methodDeclaration.AttributeLists.Any())
                {
                    MethodsWithAttribute.Add(methodDeclaration);
                }
            }
        }
    }
}
