// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Roslyn.Utilities;
using static Microsoft.CodeAnalysis.Diagnostics.Telemetry.AnalyzerTelemetry;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal abstract partial class AnalyzerDriver : IDisposable
    {
        protected static readonly ConditionalWeakTable<Compilation, CompilationData> s_compilationDataCache = new ConditionalWeakTable<Compilation, CompilationData>();

        internal class CompilationData
        {
            private readonly object _gate = new object();

            /// <summary>
            /// Cached semantic model for the compilation trees.
            /// PERF: This cache enables us to re-use semantic model's bound node cache across analyzer execution and diagnostic queries.
            /// </summary>
            private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModelsMap;

            public CompilationData(Compilation comp)
            {
                _semanticModelsMap = new Dictionary<SyntaxTree, SemanticModel>();
                this.SuppressMessageAttributeState = new SuppressMessageAttributeState(comp);
                this.DeclarationAnalysisDataMap = new Dictionary<SyntaxReference, DeclarationAnalysisData>();
            }

            public SuppressMessageAttributeState SuppressMessageAttributeState { get; }
            public Dictionary<SyntaxReference, DeclarationAnalysisData> DeclarationAnalysisDataMap { get; }

            public SemanticModel GetOrCreateCachedSemanticModel(SyntaxTree tree, Compilation compilation, CancellationToken cancellationToken)
            {
                SemanticModel model;
                lock (_gate)
                {
                    if (_semanticModelsMap.TryGetValue(tree, out model))
                    {
                        return model;
                    }
                }


                model = compilation.GetSemanticModel(tree);

                // Invoke GetDiagnostics to populate the compilation's CompilationEvent queue.
                model.GetDiagnostics(null, cancellationToken);

                lock (_gate)
                {
                    _semanticModelsMap[tree] = model;
                }

                return model;
            }

            public bool RemoveCachedSemanticModel(SyntaxTree tree)
            {
                lock (_gate)
                {
                    return _semanticModelsMap.Remove(tree);
                }
            }
        }

        internal class DeclarationAnalysisData
        {
            /// <summary>
            /// GetSyntax() for the given SyntaxReference.
            /// </summary>
            public SyntaxNode DeclaringReferenceSyntax { get; set; }

            /// <summary>
            /// Topmost declaration node for analysis.
            /// </summary>
            public SyntaxNode TopmostNodeForAnalysis { get; set; }

            /// <summary>
            /// All member declarations within the declaration.
            /// </summary>
            public ImmutableArray<DeclarationInfo> DeclarationsInNode { get; set; }

            /// <summary>
            /// All descendant nodes for syntax node actions.
            /// </summary>
            public ImmutableArray<SyntaxNode> DescendantNodesToAnalyze { get; set; }

            /// <summary>
            /// Flag indicating if this is a partial analysis.
            /// </summary>
            public bool IsPartialAnalysis { get; set; }
        }

        internal static CompilationData GetCachedCompilationData(Compilation compilation)
        {
            return s_compilationDataCache.GetValue(compilation, c => new CompilationData(c));
        }

        internal static bool RemoveCachedCompilationData(Compilation compilation)
        {
            return s_compilationDataCache.Remove(compilation);
        }

        public static SemanticModel GetOrCreateCachedSemanticModel(SyntaxTree tree, Compilation compilation, CancellationToken cancellationToken)
        {
            var compilationData = GetCachedCompilationData(compilation);
            return compilationData.GetOrCreateCachedSemanticModel(tree, compilation, cancellationToken);
        }

        public static bool RemoveCachedSemanticModel(SyntaxTree tree, Compilation compilation)
        {
            CompilationData compilationData;
            return s_compilationDataCache.TryGetValue(compilation, out compilationData) &&
                compilationData.RemoveCachedSemanticModel(tree);
        }
    }
}
