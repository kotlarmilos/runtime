// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public func nativeFunctionWithCallback(callback: (UnsafeMutableRawPointer) -> Void, expectedValue: Int) {
    // FIXME: expectedValue is not set correctly in Interpreter
    let pointer = UnsafeMutableRawPointer(bitPattern: UInt(bitPattern: 42))
    if let unwrappedPointer = pointer {
        callback(unwrappedPointer)
    } else {
        fatalError("Failed to unwrap pointer")
    }
}