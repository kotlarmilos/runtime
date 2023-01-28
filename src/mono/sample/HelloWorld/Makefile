TOP=../../../../
DOTNET:=$(TOP)dotnet.sh
DOTNET_Q_ARGS=--nologo -v:q -consoleloggerparameters:NoSummary
TOOLS_DIR=../../../tasks

MONO_CONFIG?=Debug
MONO_ARCH?=$(shell . $(TOP)eng/native/init-os-and-arch.sh && echo $${arch})
TARGET_OS?=$(shell . $(TOP)eng/native/init-os-and-arch.sh && echo $${os})
AOT?=true

#NET_TRACE_PATH=<path-to-trace-of-sample>
#PGO_BINARY_PATH=<path-to-dotnet-pgo-executable>
#MIBC_PROFILE_PATH=<path-to-mibc-for-sample>

MONO_ENV_OPTIONS ?=

publish: clean
	$(DOTNET) publish \
	-c $(MONO_CONFIG) \
	-r $(TARGET_OS)-$(MONO_ARCH) \
	/p:RunAOTCompilation=$(AOT) \
	'/p:NetTracePath="$(NET_TRACE_PATH)"' \
	'/p:PgoBinaryPath="$(PGO_BINARY_PATH)"' \
	'/p:MibcProfilePath="$(MIBC_PROFILE_PATH)"' \
	/bl

run: publish
	COMPlus_DebugWriteToStdErr=1 \
	MONO_ENV_OPTIONS="$(MONO_ENV_OPTIONS)" \
	$(TOP)artifacts/bin/HelloWorld/$(MONO_ARCH)/$(MONO_CONFIG)/$(TARGET_OS)-$(MONO_ARCH)/publish/HelloWorld

clean:
	rm -rf $(TOP)artifacts/bin/HelloWorld/

tools:
	$(DOTNET) build -c Debug $(TOOLS_DIR)/AotCompilerTask/MonoAOTCompiler.csproj

