// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.MakeFieldReadonly
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    internal sealed class MakeFieldReadonlyDiagnosticAnalyzer
        : AbstractCodeStyleDiagnosticAnalyzer
    {
        public MakeFieldReadonlyDiagnosticAnalyzer()
            : base(
                IDEDiagnosticIds.MakeFieldReadonlyDiagnosticId,
                new LocalizableResourceString(nameof(FeaturesResources.Add_readonly_modifier), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
                new LocalizableResourceString(nameof(FeaturesResources.Make_field_readonly), WorkspacesResources.ResourceManager, typeof(WorkspacesResources)))
        {
        }

        public override bool OpenFileOnly(Workspace workspace) => false;

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.ProjectAnalysis;

        protected override void InitializeWorker(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(compilationStartContext =>
            {
                // Stores the state for fields:
                //  'isCandidate' : Indicates whether the field is a candidate to be made readonly based on it's declaration and options.
                //  'written'     : Indicates if there are any writes to the field outside the constructor and field initializer.
                var fieldStateMap = new ConcurrentDictionary<IFieldSymbol, (bool isCandidate, bool written)>();

                compilationStartContext.RegisterSymbolAction(symbolContext =>
                {
                    tryGetOrComputeIsCandidateField((IFieldSymbol)symbolContext.Symbol, symbolContext.Options, symbolContext.CancellationToken);
                }, SymbolKind.Field);

                compilationStartContext.RegisterOperationBlockStartAction(operationBlockStartContext =>
                {
                    var isConstructor = operationBlockStartContext.OwningSymbol.IsConstructor();
                    var isStaticConstructor = operationBlockStartContext.OwningSymbol.IsStaticConstructor();

                    operationBlockStartContext.RegisterOperationAction(operationContext =>
                    {
                        var fieldReference = (IFieldReferenceOperation)operationContext.Operation;
                        var field = fieldReference.Field;

                        (bool isCandidate, bool written) = tryGetOrComputeIsCandidateField(field, operationContext.Options, operationContext.CancellationToken);

                        // Ignore fields that are not candidates or have already been analyzed to have been written outside the constructor/field initializer.
                        if (!isCandidate || written)
                        {
                            return;
                        }

                        // Field writes: assignment, increment/decrement or field passed by ref.
                        var isFieldAssignemnt = fieldReference.Parent is IAssignmentOperation assignmentOperation &&
                            assignmentOperation.Target == fieldReference;
                        if (isFieldAssignemnt ||
                            fieldReference.Parent is IIncrementOrDecrementOperation ||
                            fieldReference.Parent is IArgumentOperation argumentOperation && argumentOperation.Parameter.RefKind != RefKind.None)
                        {
                            // Writes to fields inside constructor are ignored, except for the below cases:
                            //  1. Instance reference of an instance field being written is not the instance being initialized by the constructor.
                            //  2. Field is being written inside a lambda or local function.

                            // Check if we are in the constructor of the containing type of the written field.
                            if ((isConstructor || isStaticConstructor) &&
                                field.ContainingType == operationBlockStartContext.OwningSymbol.ContainingType)
                            {
                                // For instance fields, ensure that the instance reference is being initialized by the constructor.
                                var instanceFieldWrittenInCtor = isConstructor &&
                                    fieldReference.Instance?.Kind == OperationKind.InstanceReference &&
                                    (!isFieldAssignemnt || fieldReference.Parent.Parent?.Kind != OperationKind.ObjectOrCollectionInitializer);
                                
                                // For static fields, ensure that we are in the static constructor.
                                var staticFieldWrittenInStaticCtor = isStaticConstructor && field.IsStatic;

                                if (instanceFieldWrittenInCtor || staticFieldWrittenInStaticCtor)
                                {
                                    // Finally, ensure that the write is not inside a lambda or local function.
                                    if (!IsInAnonymousFunctionOrLocalFunction(fieldReference))
                                    {
                                        // It is safe to ignore this write.
                                        return;
                                    }
                                }
                            }

                            onFieldWrite(field);
                        }
                    }, OperationKind.FieldReference);
                });

                compilationStartContext.RegisterCompilationEndAction(compilationEndContext =>
                {
                    foreach (var kvp in fieldStateMap)
                    {
                        IFieldSymbol field = kvp.Key;
                        (bool isCandidate, bool written) fieldState = kvp.Value;
                        if (fieldState.isCandidate && !fieldState.written)
                        {
                            var option = GetCodeStyleOption(field, compilationEndContext.Options, compilationEndContext.CancellationToken);
                            var diagnostic = Diagnostic.Create(
                                GetDescriptorWithSeverity(option.Notification.Value),
                                field.Locations[0]);
                            compilationEndContext.ReportDiagnostic(diagnostic);
                        }
                    }
                });

                return;

                // Local functions.
                bool isCandidateField(IFieldSymbol symbol) =>
                        symbol.DeclaredAccessibility == Accessibility.Private &&
                        !symbol.IsReadOnly &&
                        !symbol.IsConst &&
                        !symbol.IsImplicitlyDeclared &&
                        symbol.Locations.Length == 1 &&
                        !IsMutableValueType(symbol.Type);

                void onFieldWrite(IFieldSymbol field)
                {
                    Debug.Assert(isCandidateField(field));
                    Debug.Assert(fieldStateMap.ContainsKey(field));

                    fieldStateMap[field] = (isCandidate: true, written: true);
                }

                (bool isCandidate, bool written) tryGetOrComputeIsCandidateField(IFieldSymbol fieldSymbol, AnalyzerOptions options, CancellationToken cancellationToken)
                {
                    return fieldStateMap.GetOrAdd(fieldSymbol, valueFactory: ComputeIsCandidate);

                    (bool isCandidate, bool written) ComputeIsCandidate(IFieldSymbol field)
                    {
                        if (!isCandidateField(field))
                        {
                            return default;
                        }

                        var option = GetCodeStyleOption(field, options, cancellationToken);
                        if (option == null || !option.Value)
                        {
                            return default;
                        }

                        return (isCandidate: true, written: false);
                    }
                }
            });
        }

        private static CodeStyleOption<bool> GetCodeStyleOption(IFieldSymbol field, AnalyzerOptions options, CancellationToken cancellationToken)
        {
            var optionSet = options.GetDocumentOptionSetAsync(field.Locations[0].SourceTree, cancellationToken).GetAwaiter().GetResult();
            if (optionSet == null)
            {
                return null;
            }

            return optionSet.GetOption(CodeStyleOptions.PreferReadonly, field.Language);
        }

        private static bool IsInAnonymousFunctionOrLocalFunction(IOperation operation)
        {
            operation = operation.Parent;
            while (operation != null)
            {
                switch (operation.Kind)
                {
                    case OperationKind.AnonymousFunction:
                    case OperationKind.LocalFunction:
                        return true;
                }

                operation = operation.Parent;
            }

            return false;
        }

        private static bool IsMutableValueType(ITypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol fieldSymbol &&
                    !(fieldSymbol.IsConst || fieldSymbol.IsReadOnly || fieldSymbol.IsStatic))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
