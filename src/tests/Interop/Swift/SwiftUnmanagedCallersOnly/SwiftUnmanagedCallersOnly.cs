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
    [DllImport(SwiftLib, EntryPoint = "$s25SwiftUnmanagedCallersOnly26nativeFunctionWithCallbackyyyyXEF")]
    public static extern unsafe IntPtr NativeFunctionWithCallback(delegate* unmanaged[Swift]<SwiftSelf, SwiftError*, void> callback, SwiftSelf self, SwiftError* error);

    [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvSwift) })]
    public static unsafe void ProxyMethod(SwiftSelf self, SwiftError* error) {
        Console.WriteLine("SwiftSelf pointer: 0x{0:X}", (IntPtr)(void*)&self);
        Console.WriteLine("SwiftSelf pointee: 0x{0:X}", self.Value);
        Console.WriteLine("SwiftError pointer: 0x{0:X}", (IntPtr)error);
        Console.WriteLine("SwiftError pointee: 0x{0:X}", (*error).Value);
        // Self register is callee saved so we can't rely on it being preserved across calls. 
        // Change the test to explicitly set the value in Swift code?
        long value = *(long*)self.Value;
        Console.WriteLine ("ProxyMethod: {0}", value);
        *error = *(SwiftError*)(void*)&value;
    }

    [Fact]
    public static unsafe void TestUnmanagedCallersOnly()
    {
        long expectedValue = 42;
        SwiftSelf self = new SwiftSelf((IntPtr)(&expectedValue));
        SwiftError error;

        NativeFunctionWithCallback(&ProxyMethod, self, &error);

        long value = error.Value;
        Assert.True(value == expectedValue, string.Format("The value retrieved does not match the expected value. Expected: {0}, Actual: {1}", expectedValue, value));
    }
}
