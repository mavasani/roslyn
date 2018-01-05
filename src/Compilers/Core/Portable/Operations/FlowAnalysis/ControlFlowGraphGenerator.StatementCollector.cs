// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal partial class ControlFlowGraphGenerator
    {
        private sealed class StatementCollector : OperationWalker
        {
            private readonly List<IOperation> _statements;

            public StatementCollector()
            {
                _statements = new List<IOperation>();
            }

            public IEnumerable<IOperation> Statements
            {
                get => _statements;
            }

            public override void Visit(IOperation operation)
            {
                if (operation != null)
                {
                    var isStatement = operation.Type == null && !operation.ConstantValue.HasValue &&
                        operation.Kind != OperationKind.VariableDeclarator; // operation.IsStatement();
                    var isBlockStatement = operation.Kind == OperationKind.Block;

                    if (isStatement && !isBlockStatement)
                    {
                        _statements.Add(operation);
                    }
                }

                base.Visit(operation);
            }

            public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
            {
                // Include lamdas but don't include statements inside them,
                // because they don't belong to the lambda's containing method.
            }

            public override void VisitLocalFunction(ILocalFunctionOperation operation)
            {
                // Include local functions but don't include statements inside them,
                // because they don't belong to the local function's containing method.
            }
        }
    }
}
