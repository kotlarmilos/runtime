# NativeAOT Android sample app

## Description

This sample application serves as PoC for verifying support for the android platforms. The sample shares the source code with the Mono sample specified at: `../Android/Program.cs` and in general should have the same behavior as Mono AOT.

## Build and test

When building for the first time (on a clean checkout) run the commands below.

Setup the local environment:
```bash
git clone https://github.com/kotlarmilos/runtime.git
```
```bash
git checkout feature/nativeaot-android-poc
```

Export SDK and NDK:
```bash
export ANDROID_SDK_ROOT=~/android-sdk                                                                             
export ANDROID_NDK_ROOT=~/android-ndk-r23c
```
Build the ilc for the host:
```bash
./build.sh clr+clr.aot
```
Build the native libs:
```bash
TARGET_BUILD_ARCH=arm64 ./build.sh -s clr.nativeaotruntime+clr.nativeaotlibs -os linux-bionic
```

Build managed libs:
```bash
./build.sh -s libs -os android
```

Create a bundle:
``` bash
make run
```

The current build fails due to misconfigured cmake monodroid templates. Currently, the build steps should help identify regressons.