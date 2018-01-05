// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitBlock(IBlockOperation operation, object argument)
        {
            var savedCurrentBlock = _currentBlock;
            _currentBlock = operation;
            _rewrittenOperationsToHoist.Add(_currentBlock, new List<IOperation>());
            _tempLocalsForOperationsToHoist.Add(_currentBlock, new List<ILocalSymbol>());

            var statementBuilder = ArrayBuilder<IOperation>.GetInstance();
            ArrayBuilder<ILocalSymbol> localsBuilder = null;
            foreach (var statement in operation.Operations)
            {
                var rewrittenStatement = Visit(statement);

                Debug.Assert((_tempLocalsForOperationsToHoist[_currentBlock].Count == 0) == (_rewrittenOperationsToHoist[_currentBlock].Count == 0));
                Debug.Assert(_conditionalAccessInstanceReplacements.Count == 0);

                if (_tempLocalsForOperationsToHoist[_currentBlock].Count > 0)
                {
                    localsBuilder = localsBuilder ?? ArrayBuilder<ILocalSymbol>.GetInstance();
                    localsBuilder.AddRange(_tempLocalsForOperationsToHoist[_currentBlock]);
                    statementBuilder.AddRange(_rewrittenOperationsToHoist[_currentBlock]);
                    _tempLocalsForOperationsToHoist[_currentBlock].Clear();
                    _rewrittenOperationsToHoist[_currentBlock].Clear();
                }

                statementBuilder.Add(rewrittenStatement);
            }

            _rewrittenOperationsToHoist.Remove(_currentBlock);
            _tempLocalsForOperationsToHoist.Remove(_currentBlock);
            _currentBlock = savedCurrentBlock;

            var locals = localsBuilder != null ? operation.Locals.AddRange(localsBuilder.ToArrayAndFree()) : operation.Locals;
            return new BlockStatement(statementBuilder.ToImmutableAndFree(), locals, SemanticModel,
                operation.Syntax, operation.Type, operation.ConstantValue, operation.IsImplicit);
        }
    }
}
