// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TypeSystemProxy;
using Mono.Cecil;
using Mono.Linker;
using Mono.Linker.Dataflow;
using TypeReference = Mono.Cecil.TypeReference;


namespace ILLink.Shared.TrimAnalysis
{
	/// <summary>
	/// Return value from a method
	/// </summary>
	internal partial record MethodReturnValue
	{
		public static MethodReturnValue Create (MethodProxy method, bool isNewObj, DynamicallyAccessedMemberTypes dynamicallyAccessedMemberTypes, ITryResolveMetadata resolver)
		{
			Debug.Assert (!isNewObj || method.Definition.IsConstructor, "isNewObj can only be true for constructors");
			var methodRef = method.Method;
			var staticType = isNewObj ? methodRef.DeclaringType : methodRef.ReturnType.InflateFrom (methodRef as IGenericInstance ?? methodRef.DeclaringType as IGenericInstance);
			return new MethodReturnValue (staticType, method.Definition, dynamicallyAccessedMemberTypes, resolver);
		}

		private MethodReturnValue (TypeReference? staticType, MethodDefinition method, DynamicallyAccessedMemberTypes dynamicallyAccessedMemberTypes, ITryResolveMetadata resolver)
		{
			StaticType = staticType == null ? null : new (staticType, resolver);
			Method = method;
			DynamicallyAccessedMemberTypes = dynamicallyAccessedMemberTypes;
		}

		public readonly MethodDefinition Method;

		public override DynamicallyAccessedMemberTypes DynamicallyAccessedMemberTypes { get; }

		public override IEnumerable<string> GetDiagnosticArgumentsForAnnotationMismatch ()
			=> new string[] { DiagnosticUtilities.GetMethodSignatureDisplayName (Method) };

		public override SingleValue DeepCopy () => this; // This value is immutable

		public override string ToString () => this.ValueToString (Method, DynamicallyAccessedMemberTypes);
	}
}
