// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal static partial class ReachingDefinitionsAnalysis
    {
        private sealed class OperationTreeAnalysisData : AnalysisData
        {
            private OperationTreeAnalysisData(
                PooledDictionary<(ISymbol symbol, IOperation operation), bool> definitionUsageMap,
                PooledHashSet<ISymbol> symbolsRead,
                Func<IMethodSymbol, BasicBlockAnalysisData> analyzeLocalFunction,
                PooledDictionary<IOperation, PooledHashSet<IOperation>> reachingDelegateCreationTargets)
                : base(definitionUsageMap, symbolsRead,
                       analyzeLocalFunction,
                       analyzeLambda: null,     // Lambda target needs flow analysis, not support in operation tree analysis.
                       reachingDelegateCreationTargets)
            {
            }

            public static OperationTreeAnalysisData Create(
                ISymbol owningSymbol,
                Func<IMethodSymbol, BasicBlockAnalysisData> analyzeLocalFunction)
            {
                return new OperationTreeAnalysisData(
                    definitionUsageMap: CreateDefinitionsUsageMap(owningSymbol.GetParameters()),
                    symbolsRead: PooledHashSet<ISymbol>.GetInstance(),
                    analyzeLocalFunction,
                    reachingDelegateCreationTargets: PooledDictionary<IOperation, PooledHashSet<IOperation>>.GetInstance());
            }

            public override bool IsLValueFlowCapture(CaptureId captureId)
                => throw ExceptionUtilities.Unreachable;
            public override void OnLValueCaptureFound(ISymbol symbol, IOperation operation, CaptureId captureId)
                => throw ExceptionUtilities.Unreachable;
            public override void OnLValueDereferenceFound(CaptureId captureId)
                => throw ExceptionUtilities.Unreachable;
        }
    }
}
