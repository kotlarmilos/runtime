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

    [DllImport(SwiftLib, EntryPoint = "$s25SwiftUnmanagedCallersOnly10SelfLibaryC11getInstanceSvyFZ")]
    public unsafe static extern IntPtr getInstance();

    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvSwift) })]
    [DllImport(SwiftLib, EntryPoint = "$s25SwiftUnmanagedCallersOnly10SelfLibaryC06verifyaE8CallbackySiyyXEKF")]
    public static extern unsafe int verifySwiftSelfCallback(delegate* unmanaged[Swift]<SwiftSelf, SwiftError*, void> callback, SwiftSelf self, SwiftError* error);

    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvSwift) })]
    [DllImport(SwiftLib, EntryPoint = "$s25SwiftUnmanagedCallersOnly10SelfLibaryC11setModifiedyyF")]
    public static extern unsafe void nativeModifySelf();

    [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvSwift) })]
    public static unsafe void ModifySwiftSelf(SwiftSelf self, SwiftError* error) 
    {
        Console.WriteLine("Modifying SwiftSelf");
        nativeModifySelf();        
    }

    [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvSwift) })]
    public static unsafe void NotModifySwiftSelf(SwiftSelf self, SwiftError* error) 
    {
        Console.WriteLine("Not modifying SwiftSelf");  
    }



    [Fact]
    public static unsafe void TestSwiftSelf()
    {
        IntPtr pointer = getInstance();
        SwiftSelf self = new SwiftSelf(pointer);
        Assert.True(self.Value != IntPtr.Zero, "Failed to obtain an instance of SwiftSelf from the Swift library.");

        SwiftError error;

        // This will not modify the SwiftSelf instance
        verifySwiftSelfCallback(&NotModifySwiftSelf, self, &error);
        Assert.True(error.Value != IntPtr.Zero, "A Swift error was expected to be thrown.");

        // This will modify the SwiftSelf instance
        int result = verifySwiftSelfCallback(&ModifySwiftSelf, self, &error);

        Assert.True(error.Value == IntPtr.Zero, "No Swift error was expected to be thrown.");
        Assert.True(result == 42, "The result from Swift does not match the expected value.");
    }

}
