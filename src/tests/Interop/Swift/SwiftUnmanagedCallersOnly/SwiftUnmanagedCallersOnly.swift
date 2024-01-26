// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
import Foundation

public func nativeFunctionWithCallback(callback: (Int32) -> Void, expectedValue: Int32) {
    callback(expectedValue)
}

public enum MyError: Error {
    case runtimeError(message: NSString)
}

public class SelfLibary {
    public var modified: Bool
    public static let shared = SelfLibary(modified: false)

    public init(modified: Bool) {
        self.modified = modified
    }

    public func setModified() {
        self.modified = true
    }

    public static func getInstance() -> UnsafeMutableRawPointer {
        let unmanagedInstance = Unmanaged.passUnretained(shared)
        let pointer = unmanagedInstance.toOpaque()
        return pointer
    }

    public func verifySwiftSelfCallback(_ callback: () -> Void) throws -> Int {
        callback()

        if self.modified {
            return 42
        } else {
            throw MyError.runtimeError(message: "self is not modified by callback" as NSString)
        }
    }
}
