// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using System.Runtime.InteropServices;

public static class Program
{

    [DllImport("__Internal")]
    unsafe private static extern void android_set_text(byte* value);

    [DllImport("__Internal")]
    unsafe private static extern void android_register_button_click(delegate* unmanaged<void> callback);

    private static int counter = 0;

    private static void SetText(string txt)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(txt);
        
        unsafe 
        {
            fixed (byte* bytesPtr = bytes)
            {
                android_set_text(bytesPtr);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static void OnButtonClick()
    {
        SetText("OnButtonClick! #" + counter++);
    }

    public static int Main(string[] args)
    {
        unsafe {
            delegate* unmanaged<void> unmanagedPtr = &OnButtonClick;
            android_register_button_click(unmanagedPtr);
        }
        Console.WriteLine("Hello, Android!"); // logcat
        return 42;
    }
}
