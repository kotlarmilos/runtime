// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;

public class UnmanagedCallersOnlyTests
{
    private const string SwiftLib = "libSwiftUnmanagedCallersOnly.dylib";

    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvSwift) })]
    [DllImport(SwiftLib, EntryPoint = "$s25SwiftUnmanagedCallersOnly26nativeFunctionWithCallback8callback13expectedValueyySvXE_SitF")]
    public static extern unsafe IntPtr NativeFunctionWithCallback(delegate* unmanaged[Swift]<IntPtr, SwiftSelf, SwiftError*, void> callback, IntPtr expectedValue, SwiftSelf self, SwiftError* error);

    [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvSwift) })]
    public static unsafe void ProxyMethod(IntPtr expectedValue, SwiftSelf self, SwiftError* error) {
        // Self register is callee saved so we can't rely on it being preserved across calls.
        IntPtr value = self.Value;
        Assert.True(value == expectedValue, string.Format("The value retrieved does not match the expected value. Expected: {0}, Actual: {1}", expectedValue, value));
        *error = *(SwiftError*)(void*)&value;
    }

    [Fact]
    public static unsafe void TestUnmanagedCallersOnly()
    {
        IntPtr expectedValue = 42;
        SwiftSelf self = new SwiftSelf(expectedValue);
        SwiftError error;

        NativeFunctionWithCallback(&ProxyMethod, expectedValue, self, &error);

        IntPtr value = error.Value;
        Assert.True(value == expectedValue, string.Format("The value retrieved does not match the expected value. Expected: {0}, Actual: {1}", expectedValue, value));
    }
}
