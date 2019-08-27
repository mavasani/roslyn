// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.Shared.Utilities
{
    internal readonly struct UnusedMembersCandidateChecker
    {
        private readonly SymbolVisibility? _candidateVisibilityOpt;
        private readonly Accessibility? _candidateAccessibiltyOpt;
        private readonly INamedTypeSymbol _taskType, _genericTaskType, _structLayoutAttributeType;
        private readonly INamedTypeSymbol _eventArgsType;
        private readonly DeserializationConstructorCheck _deserializationConstructorCheck;
        private readonly ImmutableHashSet<INamedTypeSymbol> _attributeSetForMethodsToIgnore;

        public UnusedMembersCandidateChecker(Compilation compilation, SymbolVisibility candidateVisibility)
            : this(compilation, candidateVisibility, candidateAccessibiltyOpt: null)
        {
        }

        public UnusedMembersCandidateChecker(Compilation compilation, Accessibility candidateAccessibilty)
            : this(compilation, candidateVisibilityOpt: null, candidateAccessibilty)
        {
        }

        private UnusedMembersCandidateChecker(Compilation compilation, SymbolVisibility? candidateVisibilityOpt, Accessibility? candidateAccessibiltyOpt)
        {
            _candidateVisibilityOpt = candidateVisibilityOpt;
            _candidateAccessibiltyOpt = candidateAccessibiltyOpt;
            _taskType = compilation.TaskType();
            _genericTaskType = compilation.TaskOfTType();
            _structLayoutAttributeType = compilation.StructLayoutAttributeType();
            _eventArgsType = compilation.EventArgsType();
            _deserializationConstructorCheck = new DeserializationConstructorCheck(compilation);
            _attributeSetForMethodsToIgnore = ImmutableHashSet.CreateRange(GetAttributesForMethodsToIgnore(compilation));
        }

        private static IEnumerable<INamedTypeSymbol> GetAttributesForMethodsToIgnore(Compilation compilation)
        {
            // Ignore methods with special serialization attributes, which are invoked by the runtime
            // for deserialization.
            var onDeserializingAttribute = compilation.OnDeserializingAttribute();
            if (onDeserializingAttribute != null)
            {
                yield return onDeserializingAttribute;
            }

            var onDeserializedAttribute = compilation.OnDeserializedAttribute();
            if (onDeserializedAttribute != null)
            {
                yield return onDeserializedAttribute;
            }

            var onSerializingAttribute = compilation.OnSerializingAttribute();
            if (onSerializingAttribute != null)
            {
                yield return onSerializingAttribute;
            }

            var onSerializedAttribute = compilation.OnSerializedAttribute();
            if (onSerializedAttribute != null)
            {
                yield return onSerializedAttribute;
            }

            var comRegisterFunctionAttribute = compilation.ComRegisterFunctionAttribute();
            if (comRegisterFunctionAttribute != null)
            {
                yield return comRegisterFunctionAttribute;
            }

            var comUnregisterFunctionAttribute = compilation.ComUnregisterFunctionAttribute();
            if (comUnregisterFunctionAttribute != null)
            {
                yield return comUnregisterFunctionAttribute;
            }
        }

        /// <summary>
        /// Returns true if the given symbol meets the following criteria to be
        /// a candidate for dead code analysis:
        ///     1. It has the required <see cref="_candidateVisibilityOpt"/> or <see cref="_candidateAccessibiltyOpt"/>.
        ///     2. It is not an implicitly declared symbol.
        ///     3. It is not a member of a type with StructLayoutAttribute as the ordering of the members is critical
        ///        and removal of unused members might break semantics.
        ///     4. It is either a method, field, property or an event.
        ///     5. If method, then it is a constructor OR a method with <see cref="MethodKind.Ordinary"/>,
        ///        such that is meets a few criteria (see implementation details below).
        ///     6. If field, then it must not be a backing field for an auto property.
        ///        Backing fields have a non-null <see cref="IFieldSymbol.AssociatedSymbol"/>.
        ///     7. If property, then it must not be an explicit interface property implementation.
        ///     8. If event, then it must not be an explicit interface event implementation.
        /// </summary>
        public bool IsCandidateSymbol(ISymbol memberSymbol)
        {
            Debug.Assert(memberSymbol == memberSymbol.OriginalDefinition);

            if (memberSymbol.IsImplicitlyDeclared)
            {
                return false;
            }

            if (_candidateAccessibiltyOpt.HasValue &&
                memberSymbol.DeclaredAccessibility != _candidateAccessibiltyOpt.Value)
            {
                return false;
            }

            if (_candidateVisibilityOpt.HasValue &&
                memberSymbol.GetResultantVisibility() != _candidateVisibilityOpt.Value)
            {
                return false;
            }

            switch (memberSymbol.Kind)
            {
                case SymbolKind.Method:
                    var methodSymbol = (IMethodSymbol)memberSymbol;

                    // Do not track accessors, as we will track/flag the associated symbol.
                    if (methodSymbol.AssociatedSymbol != null)
                    {
                        return false;
                    }

                    switch (methodSymbol.MethodKind)
                    {
                        case MethodKind.Constructor:
                            // It is fine to have an unused private constructor
                            // without parameters.
                            // This is commonly used for static holder types
                            // that want to block instantiation of the type.
                            if (methodSymbol.Parameters.Length == 0 &&
                                _candidateAccessibiltyOpt == Accessibility.Private)
                            {
                                return false;
                            }

                            // ISerializable constructor is invoked by the runtime for deserialization
                            // and it is a common pattern to have a private serialization constructor
                            // that is not explicitly referenced in code.
                            if (_deserializationConstructorCheck.IsDeserializationConstructor(methodSymbol) &&
                                _candidateAccessibiltyOpt == Accessibility.Private)
                            {
                                return false;
                            }

                            break;

                        case MethodKind.Ordinary:
                            // Do not flag unused entry point (Main) method.
                            if (IsEntryPoint(methodSymbol))
                            {
                                return false;
                            }

                            // It is fine to have unused virtual/abstract/overrides/extern
                            // methods as they might be used in another type in the containing
                            // type's type hierarchy.
                            if (methodSymbol.IsAbstract ||
                                methodSymbol.IsVirtual ||
                                methodSymbol.IsOverride ||
                                methodSymbol.IsExtern)
                            {
                                return false;
                            }

                            // Explicit interface implementations are not referenced explicitly,
                            // but are still used.
                            if (!methodSymbol.ExplicitInterfaceImplementations.IsEmpty)
                            {
                                return false;
                            }

                            // Ignore methods with special attributes that indicate special/reflection
                            // based access.
                            if (IsMethodWithSpecialAttribute(methodSymbol))
                            {
                                return false;
                            }

                            // ShouldSerializeXXX and ResetXXX are ok if there is a matching
                            // property XXX as they are used by the windows designer property grid
                            if (IsShouldSerializeOrResetPropertyMethod(methodSymbol))
                            {
                                return false;
                            }

                            // Ignore methods with event handler signature
                            // as lot of ASP.NET types have many special event handlers
                            // that are invoked with reflection (e.g. Application_XXX, Page_XXX,
                            // OnTransactionXXX, etc).
                            if (methodSymbol.HasEventHandlerSignature(_eventArgsType))
                            {
                                return false;
                            }

                            break;
                    }

                    break;

                case SymbolKind.Field:
                    if (((IFieldSymbol)memberSymbol).AssociatedSymbol != null)
                    {
                        return false;
                    }

                    break;

                case SymbolKind.Property:
                    if (!((IPropertySymbol)memberSymbol).ExplicitInterfaceImplementations.IsEmpty)
                    {
                        return false;
                    }

                    break;

                case SymbolKind.Event:
                    if (!((IEventSymbol)memberSymbol).ExplicitInterfaceImplementations.IsEmpty)
                    {
                        return false;
                    }

                    break;

                default:
                    return false;
            }

            // Bail out for types with 'StructLayoutAttribute' as the ordering of the members is critical,
            // and removal of unused members might break semantics.
            return !memberSymbol.ContainingType.GetAttributes().Any(IsStructLayoutAttribute);
        }

        private bool IsEntryPoint(IMethodSymbol methodSymbol)
            => methodSymbol.Name == WellKnownMemberNames.EntryPointMethodName &&
               methodSymbol.IsStatic &&
               (methodSymbol.ReturnsVoid ||
                methodSymbol.ReturnType.SpecialType == SpecialType.System_Int32 ||
                methodSymbol.ReturnType.OriginalDefinition.Equals(_taskType) ||
                methodSymbol.ReturnType.OriginalDefinition.Equals(_genericTaskType));

        private bool IsMethodWithSpecialAttribute(IMethodSymbol methodSymbol)
            => methodSymbol.GetAttributes().Any(IsSpecialAttribute);

        private bool IsSpecialAttribute(AttributeData attribute)
            => _attributeSetForMethodsToIgnore.Contains(attribute.AttributeClass);

        private bool IsStructLayoutAttribute(AttributeData attribute)
            => _structLayoutAttributeType != null &&
            _structLayoutAttributeType.Equals(attribute.AttributeClass);

        private bool IsShouldSerializeOrResetPropertyMethod(IMethodSymbol methodSymbol)
        {
            // ShouldSerializeXXX and ResetXXX are ok if there is a matching
            // property XXX as they are used by the windows designer property grid
            // Note that we do a case sensitive compare for compatibility with legacy FxCop
            // implementation of this rule.

            return methodSymbol.ReturnType.SpecialType == SpecialType.System_Boolean &&
                methodSymbol.Parameters.IsEmpty &&
                (IsSpecialMethodWithMatchingProperty("ShouldSerialize") ||
                 IsSpecialMethodWithMatchingProperty("Reset"));

            // Local functions.
            bool IsSpecialMethodWithMatchingProperty(string prefix)
            {
                if (methodSymbol.Name.StartsWith(prefix))
                {
                    var suffix = methodSymbol.Name.Substring(prefix.Length);
                    return suffix.Length > 0 &&
                        methodSymbol.ContainingType.GetMembers(suffix).Any(m => m is IPropertySymbol);
                }

                return false;
            }
        }
    }
}
