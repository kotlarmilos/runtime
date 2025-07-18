// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

using Internal.Text;
using Internal.TypeSystem;
using Internal.NativeFormat;

using Debug = System.Diagnostics.Debug;
using InvokeTableFlags = Internal.Runtime.InvokeTableFlags;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents a map between reflection metadata and generated method bodies.
    /// </summary>
    internal sealed class ReflectionInvokeMapNode : ObjectNode, ISymbolDefinitionNode, INodeWithSize
    {
        private int? _size;
        private ExternalReferencesTableNode _externalReferences;

        public ReflectionInvokeMapNode(ExternalReferencesTableNode externalReferences)
        {
            _externalReferences = externalReferences;
        }

        int INodeWithSize.Size => _size.Value;

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix).Append("__method_to_entrypoint_map"u8);
        }
        public int Offset => 0;
        public override bool IsShareable => false;

        public override ObjectNodeSection GetSection(NodeFactory factory) => _externalReferences.GetSection(factory);

        public override bool StaticDependenciesAreComputed => true;

        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);

        public static void AddDependenciesDueToReflectability(ref DependencyList dependencies, NodeFactory factory, MethodDesc method)
        {
            Debug.Assert(factory.MetadataManager.IsReflectionInvokable(method));
            Debug.Assert(method.GetCanonMethodTarget(CanonicalFormKind.Specific) == method);

            dependencies ??= new DependencyList();

            dependencies.Add(factory.MaximallyConstructableType(method.OwningType), "Reflection invoke");

            if (factory.MetadataManager.HasReflectionInvokeStubForInvokableMethod(method))
            {
                MethodDesc invokeStub = factory.MetadataManager.GetReflectionInvokeStub(method);
                dependencies.Add(factory.MethodEntrypoint(invokeStub), "Reflection invoke");

                var signature = method.Signature;
                AddSignatureDependency(ref dependencies, factory, method, signature.ReturnType, "Reflection invoke", isOut: true);
                foreach (var parameterType in signature)
                    AddSignatureDependency(ref dependencies, factory, method, parameterType, "Reflection invoke", isOut: false);
            }

            if (method.OwningType.IsValueType && !method.Signature.IsStatic)
                dependencies.Add(factory.MethodEntrypoint(method, unboxingStub: true), "Reflection unboxing stub");

            if (!method.IsAbstract)
            {
                dependencies.Add(factory.AddressTakenMethodEntrypoint(method), "Body of a reflectable method");
            }

            if (method.HasInstantiation)
            {
                foreach (var instArg in method.Instantiation)
                {
                    dependencies.Add(factory.NecessaryTypeSymbol(instArg), "Reflectable generic method inst arg");
                }
            }

            ReflectionVirtualInvokeMapNode.GetVirtualInvokeMapDependencies(ref dependencies, factory, method);
        }

        internal static void AddSignatureDependency(ref DependencyList dependencies, NodeFactory factory, TypeSystemEntity referent, TypeDesc type, string reason, bool isOut)
        {
            if (type.IsByRef)
            {
                type = ((ParameterizedType)type).ParameterType;
                isOut = true;
            }

            // Pointer runtime type handles can be created at runtime if necessary
            while (type.IsPointer)
                type = ((ParameterizedType)type).ParameterType;

            // Skip tracking dependencies for primitive types. Assume that they are always present.
            if (type.IsPrimitive || type.IsVoid)
                return;

            try
            {
                factory.TypeSystemContext.DetectGenericCycles(type, referent);

                // If the user can legitimately get to a System.Type instance by inspecting the signature of the method,
                // the user then might use these System.Type instances with APIs such as:
                //
                // * MethodInfo.CreateDelegate
                // * RuntimeHelpers.Box
                // * Array.CreateInstanceForArrayType
                //
                // All of these APIs are trim/AOT safe and allow creating new instances of the supplied Type.
                // We need this to work.
                // NOTE: We don't have to worry about RuntimeHelpers.GetUninitializedObject because that one is annotated
                // as trim-unsafe.
                bool isMaximallyConstructibleByPolicy = type.IsDelegate || type.IsValueType || type.IsSzArray;

                // Reflection might need to create boxed instances of valuetypes as part of reflection invocation.
                // Non-valuetypes are only needed for the purposes of casting/type checks.
                // If this is a non-exact type, we need the type loader template to get the type handle.
                if (type.IsCanonicalSubtype(CanonicalFormKind.Any))
                    GenericTypesTemplateMap.GetTemplateTypeDependencies(ref dependencies, factory, type.NormalizeInstantiation());
                else if ((isOut && !type.IsGCPointer) || isMaximallyConstructibleByPolicy)
                    dependencies.Add(factory.MaximallyConstructableType(type.NormalizeInstantiation()), reason);
                else
                    dependencies.Add(factory.NecessaryTypeSymbol(type.NormalizeInstantiation()), reason);
            }
            catch (TypeSystemException)
            {
                // It's fine to continue compiling if there's a problem getting these. There's going to be a MissingMetadata
                // exception when actually trying to invoke this and the exception will be different than the one we'd get with
                // a JIT, but that's fine, we don't need to be bug-for-bug compatible.
            }
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            // This node does not trigger generation of other nodes.
            if (relocsOnly)
                return new ObjectData(Array.Empty<byte>(), Array.Empty<Relocation>(), 1, new ISymbolDefinitionNode[] { this });

            // Ensure the native layout blob has been saved
            factory.MetadataManager.NativeLayoutInfo.SaveNativeLayoutInfoWriter(factory);

            var writer = new NativeWriter();
            var typeMapHashTable = new VertexHashtable();

            Section hashTableSection = writer.NewSection();
            hashTableSection.Place(typeMapHashTable);

            // Get a list of all methods that have a method body and metadata from the metadata manager.
            foreach (var mappingEntry in factory.MetadataManager.GetMethodMapping(factory))
            {
                MethodDesc method = mappingEntry.Entity;

                Debug.Assert(method == method.GetCanonMethodTarget(CanonicalFormKind.Specific));

                if (!factory.MetadataManager.ShouldMethodBeInInvokeMap(method))
                    continue;

                InvokeTableFlags flags = 0;

                if (method.HasInstantiation)
                    flags |= InvokeTableFlags.IsGenericMethod;

                if (method.RequiresInstArg())
                {
                    bool goesThroughInstantiatingUnboxingThunk = method.OwningType.IsValueType && !method.Signature.IsStatic && !method.HasInstantiation;
                    if (!goesThroughInstantiatingUnboxingThunk)
                        flags |= InvokeTableFlags.RequiresInstArg;
                }

                if (method.IsDefaultConstructor)
                    flags |= InvokeTableFlags.IsDefaultConstructor;

                if (ReflectionVirtualInvokeMapNode.NeedsVirtualInvokeInfo(factory, method))
                    flags |= InvokeTableFlags.HasVirtualInvoke;

                if (!method.IsAbstract)
                    flags |= InvokeTableFlags.HasEntrypoint;

                if (!factory.MetadataManager.HasReflectionInvokeStubForInvokableMethod(method))
                    flags |= InvokeTableFlags.NeedsParameterInterpretation;

                // TODO: native signature for P/Invokes and UnmanagedCallersOnly methods
                if (method.IsRawPInvoke() || method.IsUnmanagedCallersOnly)
                    continue;

                // Grammar of an entry in the hash table:
                // Flags + DeclaringType + MetadataHandle/NameAndSig + Entrypoint + DynamicInvokeMethod + [NumGenericArgs + GenericArgs]

                Vertex vertex = writer.GetUnsignedConstant((uint)flags);

                // Only store the offset portion of the metadata handle to get better integer compression
                vertex = writer.GetTuple(vertex,
                    writer.GetUnsignedConstant((uint)(mappingEntry.MetadataHandle & MetadataManager.MetadataOffsetMask)));

                // Go with a necessary type symbol. It will be upgraded to a constructed one if a constructed was emitted.
                IEETypeNode owningTypeSymbol = factory.NecessaryTypeSymbol(method.OwningType);
                vertex = writer.GetTuple(vertex,
                    writer.GetUnsignedConstant(_externalReferences.GetIndex(owningTypeSymbol)));

                if ((flags & InvokeTableFlags.HasEntrypoint) != 0)
                {
                    vertex = writer.GetTuple(vertex,
                        writer.GetUnsignedConstant(_externalReferences.GetIndex(
                            factory.AddressTakenMethodEntrypoint(method,
                            unboxingStub: method.OwningType.IsValueType && !method.Signature.IsStatic))));
                }

                if ((flags & InvokeTableFlags.NeedsParameterInterpretation) == 0)
                {
                    MethodDesc invokeStub = factory.MetadataManager.GetReflectionInvokeStub(method);
                    vertex = writer.GetTuple(vertex,
                        writer.GetUnsignedConstant(_externalReferences.GetIndex(factory.MethodEntrypoint(invokeStub))));
                }

                if ((flags & InvokeTableFlags.IsGenericMethod) != 0)
                {
                    VertexSequence args = new VertexSequence();
                    for (int i = 0; i < method.Instantiation.Length; i++)
                    {
                        uint argId = _externalReferences.GetIndex(factory.NecessaryTypeSymbol(method.Instantiation[i]));
                        args.Append(writer.GetUnsignedConstant(argId));
                    }
                    vertex = writer.GetTuple(vertex, args);
                }

                int hashCode = method.OwningType.GetHashCode();
                typeMapHashTable.Append((uint)hashCode, hashTableSection.Place(vertex));
            }

            byte[] hashTableBytes = writer.Save();

            _size = hashTableBytes.Length;

            return new ObjectData(hashTableBytes, Array.Empty<Relocation>(), 1, new ISymbolDefinitionNode[] { this });
        }

        protected internal override int Phase => (int)ObjectNodePhase.Ordered;
        public override int ClassCode => (int)ObjectNodeOrder.ReflectionInvokeMapNode;
    }
}
