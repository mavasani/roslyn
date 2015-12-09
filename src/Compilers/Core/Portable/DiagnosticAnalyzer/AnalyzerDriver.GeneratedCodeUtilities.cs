﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal abstract partial class AnalyzerDriver : IDisposable
    {
        private static class GeneratedCodeUtilities
        {
            internal static bool HasGeneratedCodeAttribute(ISymbol symbol, INamedTypeSymbol generatedCodeAttribute)
            {
                Debug.Assert(symbol != null);
                Debug.Assert(generatedCodeAttribute != null);

                if (symbol.GetAttributes().Any(a => a.AttributeClass == generatedCodeAttribute))
                {
                    return true;
                }

                return symbol.ContainingSymbol != null && HasGeneratedCodeAttribute(symbol.ContainingSymbol, generatedCodeAttribute);
            }

            internal static bool IsGeneratedCode(SyntaxTree tree, Func<SyntaxTrivia, bool> isSingleLineComment, CancellationToken cancellationToken)
            {
                if (IsGeneratedCodeFile(tree.FilePath))
                {
                    return true;
                }

                if (BeginsWithAutoGeneratedComment(tree, isSingleLineComment, cancellationToken))
                {
                    return true;
                }

                return false;
            }

            private static bool IsGeneratedCodeFile(string filePath)
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return false;
                }

                var fileName = PathUtilities.GetFileName(filePath);
                if (fileName.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var extension = PathUtilities.GetExtension(fileName);
                if (string.IsNullOrEmpty(extension))
                {
                    return false;
                }

                var fileNameWithoutExtension = PathUtilities.GetFileName(filePath, includeExtension: false);
                if (fileNameWithoutExtension.EndsWith(".designer", StringComparison.OrdinalIgnoreCase) ||
                    fileNameWithoutExtension.EndsWith(".generated", StringComparison.OrdinalIgnoreCase) ||
                    fileNameWithoutExtension.EndsWith(".g", StringComparison.OrdinalIgnoreCase) ||
                    fileNameWithoutExtension.EndsWith(".g.i", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            private static bool BeginsWithAutoGeneratedComment(SyntaxTree tree, Func<SyntaxTrivia, bool> isSingleLineComment, CancellationToken cancellationToken)
            {
                var root = tree.GetRoot(cancellationToken);
                if (root.HasLeadingTrivia)
                {
                    var leadingTrivia = root.GetLeadingTrivia();

                    foreach (var trivia in leadingTrivia)
                    {
                        if (!isSingleLineComment(trivia))
                        {
                            continue;
                        }

                        var text = trivia.ToString();

                        // Scan past whitespace and comment begin tokens.
                        int index = 0;
                        while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == '/' || text[index] == '\''))
                        {
                            index++;
                        }

                        // Check to see if the text of the comment starts with "<auto-generated>".
                        const string AutoGenerated = "<auto-generated>";

                        if (string.Compare(text, index, AutoGenerated, 0, AutoGenerated.Length, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            
        }
    }
}
