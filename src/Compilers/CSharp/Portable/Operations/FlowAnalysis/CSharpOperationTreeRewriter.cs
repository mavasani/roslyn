// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.Operations.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp.Operations.FlowAnalysis
{
    internal class CSharpOperationTreeRewriter : OperationTreeRewriter
    {
        private int _tempLocalCount = 0;

        public CSharpOperationTreeRewriter(SemanticModel semanticModel, ISymbol containerSymbol) : base(semanticModel, containerSymbol)
        {
        }
        public override IOperation VisitArgument(IArgumentOperation operation, object argument)
        {
            return new CSharpArgument(operation.ArgumentKind, operation.Parameter, Visit(operation.Value), ((Operation)operation).SemanticModel, operation.Syntax, operation.ConstantValue, operation.IsImplicit);
        }

        public override IOperation VisitConversion(IConversionOperation operation, object argument)
        {
            return new CSharpConversionExpression(Visit(operation.Operand), operation.GetConversion(), operation.IsTryCast, operation.IsChecked, ((Operation)operation).SemanticModel, operation.Syntax, operation.Type, operation.ConstantValue, operation.IsImplicit);
        }

        public override IOperation VisitCompoundAssignment(ICompoundAssignmentOperation operation, object argument)
        {
            var compoundAssignment = (BaseCSharpCompoundAssignmentOperation)operation;
            return new CSharpCompoundAssignmentOperation(Visit(operation.Target), Visit(operation.Value), compoundAssignment.InConversionInternal, compoundAssignment.OutConversionInternal, operation.OperatorKind, operation.IsLifted, operation.IsChecked, operation.OperatorMethod, ((Operation)operation).SemanticModel, operation.Syntax, operation.Type, operation.ConstantValue, operation.IsImplicit);
        }

        protected override ILabelSymbol CreateGeneratedLabelSymbol(string label) => new GeneratedLabelSymbol(label);

        protected override ILocalSymbol CreateTemporaryLocalSymbol(ITypeSymbol type, SyntaxNode syntax) =>
            new SynthesizedLocal(ContainerSymbol as MethodSymbol, (TypeSymbol)type, SynthesizedLocalKind.LoweringTemp, syntax, name: $"temp_{++_tempLocalCount}");

        protected override bool IsNullableType(ITypeSymbol type) => ((TypeSymbol)type).IsNullableType();

        protected override IOperation MakeConversionIfNeeded(IOperation source, ITypeSymbol convertedType)
        {
            var conversion = SemanticModel.Compilation.ClassifyConversion(source.Type, convertedType);
            if (conversion.IsImplicit && conversion.IsIdentity)
            {
                return source;
            }

            return new CSharpConversionExpression(source, conversion, isTryCast: false, isChecked: false, SemanticModel, source.Syntax, convertedType, constantValue: default, isImplicit: true);
        }
    }
}
