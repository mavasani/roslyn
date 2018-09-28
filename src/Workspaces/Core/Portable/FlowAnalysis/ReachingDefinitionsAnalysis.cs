// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FlowAnalysis.ReachingDefinitions
{
    internal static partial class ReachingDefinitionsAnalysis
    {
        public static DefinitionUsageResult Run(ControlFlowGraph cfg, ISymbol owningSymbol, CancellationToken cancellationToken)
            => DataFlowAnalyzer.RunAnalysis(cfg, owningSymbol, cancellationToken);

        public static DefinitionUsageResult Run(IOperation rootOperation, ISymbol owningSymbol, CancellationToken cancellationToken)
        {
            AnalysisData analysisData = null;
            using (analysisData = OperationTreeAnalysisData.Create(owningSymbol, AnalyzeLocalFunction))
            {
                var operations = SpecializedCollections.SingletonEnumerable(rootOperation);
                Walker.AnalyzeOperationsAndUpdateData(operations, analysisData, cancellationToken);
                return analysisData.ToResult();
            }

            // Local functions.
            BasicBlockAnalysisData AnalyzeLocalFunction(IMethodSymbol localFunction)
            {
                var localFunctionOperation = rootOperation.GetLocalFunctionOperation(localFunction);
                if (localFunctionOperation == null)
                {
                    Debug.Fail($"Failed to find ILocalFunctionOperation for '{localFunction.ToDisplayString()}'");
                    return null;
                }

                var operations = SpecializedCollections.SingletonEnumerable(localFunctionOperation);
                Walker.AnalyzeOperationsAndUpdateData(operations, analysisData, cancellationToken);
                return analysisData.CurrentBlockAnalysisData;
            }
        }
    }
}
