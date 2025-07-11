// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef __thread_h__
#define __thread_h__

#include "StackFrameIterator.h"
#include "slist.h" // DefaultSListTraits
#include <minipal/xoshiro128pp.h>

struct gc_alloc_context;
class RuntimeInstance;
class ThreadStore;
class CLREventStatic;
class Thread;
class TypeManager;

#ifdef TARGET_UNIX
#include "NativeContext.h"
#endif

// The offsets of some fields in the thread (in particular, m_pTransitionFrame) are known to the compiler and get
// inlined into the code.  Let's make sure they don't change just because we enable/disable server GC in a particular
// runtime build.
#define KEEP_THREAD_LAYOUT_CONSTANT

#ifndef HOST_64BIT
# if defined(FEATURE_SVR_GC) || defined(KEEP_THREAD_LAYOUT_CONSTANT)
#  define SIZEOF_ALLOC_CONTEXT 40
# else
#  define SIZEOF_ALLOC_CONTEXT 28
# endif
#else // HOST_64BIT
# if defined(FEATURE_SVR_GC) || defined(KEEP_THREAD_LAYOUT_CONSTANT)
#  define SIZEOF_ALLOC_CONTEXT 56
# else
#  define SIZEOF_ALLOC_CONTEXT 40
# endif
#endif // HOST_64BIT

#define TOP_OF_STACK_MARKER ((PInvokeTransitionFrame*)(ptrdiff_t)-1)

// the thread has been interrupted and context for the interruption point
// can be retrieved via GetInterruptedContext()
#define INTERRUPTED_THREAD_MARKER ((PInvokeTransitionFrame*)(ptrdiff_t)-2)

typedef DPTR(PAL_LIMITED_CONTEXT) PTR_PAL_LIMITED_CONTEXT;

struct ExInfo;
typedef DPTR(ExInfo) PTR_ExInfo;

// Also defined in ExceptionHandling.cs, layouts must match.
// When adding new fields to this struct, ensure they get properly initialized in the exception handling
// assembly stubs
struct ExInfo
{

    PTR_ExInfo              m_pPrevExInfo;
    PTR_PAL_LIMITED_CONTEXT m_pExContext;
    PTR_Object              m_exception;  // actual object reference, specially reported by GcScanRootsWorker
    ExKind                  m_kind;
    uint8_t                 m_passNumber;
    uint32_t                m_idxCurClause;
    StackFrameIterator      m_frameIter;
    volatile void*          m_notifyDebuggerSP;
};

struct GCFrameRegistration
{
    Thread* m_pThread;
    GCFrameRegistration* m_pNext;
    void** m_pObjRefs;
    uint32_t m_numObjRefs;
    int m_MaybeInterior;
};

struct InlinedThreadStaticRoot
{
    // The reference to the memory block that stores variables for the current {thread, typeManager} combination
    Object* m_threadStaticsBase;
    // The next root in the list. All roots in the list belong to the same thread, but to different typeManagers.
    InlinedThreadStaticRoot* m_next;
    // m_typeManager is used by NativeAOT.natvis when debugging
    TypeManager* m_typeManager;
};

// This struct allows adding some state that is only visible to the EE onto the standard gc_alloc_context
struct ee_alloc_context
{
    // Any allocation that would overlap combined_limit needs to be handled by the allocation slow path.
    // combined_limit is the minimum of:
    //  - gc_alloc_context.alloc_limit (the end of the current AC)
    //  - the sampling_limit
    //
    // In the simple case that randomized sampling is disabled, combined_limit is always equal to alloc_limit.
    //
    // There are two different useful interpretations for the sampling_limit. One is to treat the sampling_limit
    // as an address and when we allocate an object that overlaps that address we should emit a sampling event.
    // The other is that we can treat (sampling_limit - alloc_ptr) as a budget of how many bytes we can allocate
    // before emitting a sampling event. If we always allocated objects contiguously in the AC and incremented
    // alloc_ptr by the size of the object, these two interpretations would be equivalent. However, when objects
    // don't fit in the AC we allocate them in some other address range. The budget interpretation is more
    // flexible to handle those cases.
    //
    // The sampling limit isn't stored in any separate field explicitly, instead it is implied:
    // - if combined_limit == alloc_limit there is no sampled byte in the AC. In the budget interpretation
    //   we can allocate (alloc_limit - alloc_ptr) unsampled bytes. We'll need a new random number after
    //   that to determine whether future allocated bytes should be sampled.
    //   This occurs either because the sampling feature is disabled, or because the randomized selection
    //   of sampled bytes didn't select a byte in this AC.
    // - if combined_limit < alloc_limit there is a sample limit in the AC. sample_limit = combined_limit.
    uint8_t* combined_limit;
    uint8_t m_rgbAllocContextBuffer[SIZEOF_ALLOC_CONTEXT];

    gc_alloc_context* GetGCAllocContext();
    uint8_t* GetCombinedLimit();
    void UpdateCombinedLimit(bool samplingEnabled);
    static bool IsRandomizedSamplingEnabled();
    static uint32_t ComputeGeometricRandom();

    struct PerThreadRandom
    {
        minipal_xoshiro128pp random_state;

        PerThreadRandom();
        double NextDouble();
    };

    static thread_local PerThreadRandom t_random;
};


struct RuntimeThreadLocals
{
    ee_alloc_context        m_eeAllocContext;               
    uint32_t volatile       m_ThreadStateFlags;                     // see Thread::ThreadStateFlags enum
    PInvokeTransitionFrame* m_pTransitionFrame;
    PInvokeTransitionFrame* m_pDeferredTransitionFrame;             // see Thread::EnablePreemptiveMode
    PInvokeTransitionFrame* m_pCachedTransitionFrame;
    PTR_Thread              m_pNext;                                // used by ThreadStore's SList<Thread>
#ifdef FEATURE_HIJACK
    void **                 m_ppvHijackedReturnAddressLocation;
    void *                  m_pvHijackedReturnAddress;
#endif // FEATURE_HIJACK
    PTR_ExInfo              m_pExInfoStackHead;
    Object*                 m_threadAbortException;                 // ThreadAbortException instance -set only during thread abort
#ifdef TARGET_X86
    uintptr_t               m_uHijackedReturnValueFlags;
    PCODE                   m_LastRedirectIP;
    uint64_t                m_SpinCount;
#endif
#ifdef TARGET_WINDOWS
    HANDLE                  m_hOSThread;                           // WARNING: this may legitimately be INVALID_HANDLE_VALUE
#else
    pthread_t               m_hOSThread;
#endif
    Object*                 m_pThreadLocalStatics;
    InlinedThreadStaticRoot* m_pInlinedThreadLocalStatics;
    GCFrameRegistration*    m_pGCFrameRegistrations;
    PTR_VOID                m_pStackLow;
    PTR_VOID                m_pStackHigh;
    uint64_t                m_threadId;                             // OS thread ID
    PTR_VOID                m_pThreadStressLog;                     // pointer to head of thread's StressLogChunks
    NATIVE_CONTEXT*         m_interruptedContext;                   // context for an asynchronously interrupted thread.
#ifdef FEATURE_SUSPEND_REDIRECTION
    uint8_t*                m_redirectionContextBuffer;             // storage for redirection context, allocated on demand
#endif //FEATURE_SUSPEND_REDIRECTION

#ifdef FEATURE_GC_STRESS
    uint32_t                m_uRand;                                // current per-thread random number
#endif // FEATURE_GC_STRESS
};

struct ReversePInvokeFrame
{
    PInvokeTransitionFrame*   m_savedPInvokeTransitionFrame;
    Thread* m_savedThread;
};

class Thread : private RuntimeThreadLocals
{
    friend class AsmOffsets;
    friend struct DefaultSListTraits<Thread>;
    friend class ThreadStore;
    IN_DAC(friend class ClrDataAccess;)

public:
    enum ThreadStateFlags
    {
        TSF_Unknown             = 0x00000000,       // Threads are created in this state
        TSF_Attached            = 0x00000001,       // Thread was inited by first U->M transition on this thread
                                                    // ...Prior to setting this bit the state is TSF_Unknown.
        TSF_Detached            = 0x00000002,       // Thread was detached and no longer can run managed code.
                                                    // ...TSF_Attached is cleared when TSF_Detached is set.
        TSF_SuppressGcStress    = 0x00000008,       // Do not allow gc stress on this thread, used in DllMain
                                                    // ...and on the Finalizer thread
        TSF_DoNotTriggerGc      = 0x00000010,       // Do not allow hijacking of this thread, also intended to
                                                    // ...be checked during allocations in debug builds.
        TSF_IsGcSpecialThread   = 0x00000020,       // Set to indicate a GC worker thread used for background GC
#ifdef FEATURE_GC_STRESS
        TSF_IsRandSeedSet       = 0x00000040,       // set to indicate the random number generator for GCStress was inited
#endif // FEATURE_GC_STRESS

#ifdef FEATURE_SUSPEND_REDIRECTION
        TSF_Redirected          = 0x00000080,       // Set to indicate the thread is redirected and will inevitably
                                                    // suspend once resumed.
                                                    // If we see this flag, we skip hijacking as an optimization.
#endif //FEATURE_SUSPEND_REDIRECTION

        TSF_ActivationPending   = 0x00000100,       // An APC with QUEUE_USER_APC_FLAGS_SPECIAL_USER_APC can interrupt another APC.
                                                    // For suspension APCs it is mostly harmless, but wasteful and in extreme
                                                    // cases may force the target thread into stack oveflow.
                                                    // We use this flag to avoid sending another APC when one is still going through.
                                                    //
                                                    // On Unix this is an optimization to not queue up more signals when one is
                                                    // still being processed.
    };
private:

    void Construct();

    void SetState(ThreadStateFlags flags);
    void ClearState(ThreadStateFlags flags);
    bool IsStateSet(ThreadStateFlags flags);

#ifdef FEATURE_HIJACK
    void HijackReturnAddress(PAL_LIMITED_CONTEXT* pSuspendCtx, HijackFunc* pfnHijackFunction);
    void HijackReturnAddress(NATIVE_CONTEXT* pSuspendCtx, HijackFunc* pfnHijackFunction);
    void HijackReturnAddressWorker(StackFrameIterator* frameIterator, HijackFunc* pfnHijackFunction);
    void CrossThreadUnhijack();
    void UnhijackWorker();
#else // FEATURE_HIJACK
    void CrossThreadUnhijack() { }
#endif // FEATURE_HIJACK

#ifdef FEATURE_SUSPEND_REDIRECTION
    bool Redirect();
#endif //FEATURE_SUSPEND_REDIRECTION

    bool CacheTransitionFrameForSuspend();
    void ResetCachedTransitionFrame();
    void EnsureRuntimeInitialized();

    //
    // SyncState members
    //
    PInvokeTransitionFrame* GetTransitionFrame();

    void GcScanRootsWorker(ScanFunc* pfnEnumCallback, ScanContext* pvCallbackData, StackFrameIterator & sfIter);

    // Tracks the amount of bytes that were reserved for threads in their gc_alloc_context and went unused when they died.
    // Used for GC.GetTotalAllocatedBytes
    static uint64_t s_DeadThreadsNonAllocBytes;

public:
    bool InlineSuspend(NATIVE_CONTEXT* interruptedContext);

    static uint64_t GetDeadThreadsNonAllocBytes();

    // First phase of thread destructor, disposes stuff related to GC.
    // Executed with thread store lock taken so GC cannot happen.
    void Detach();
    // Second phase of thread destructor.
    // Executed without thread store lock taken.
    void Destroy();

    bool                IsInitialized();

    ee_alloc_context *  GetEEAllocContext();
    gc_alloc_context *  GetAllocContext();

    uint64_t            GetPalThreadIdForLogging();

    void                GcScanRoots(ScanFunc* pfnEnumCallback, ScanContext * pvCallbackData);

#ifdef FEATURE_HIJACK
    void                Hijack();
    void                Unhijack();
    bool                IsHijacked();
    void*               GetHijackedReturnAddress();
    static bool         IsHijackTarget(void * address);

    static void HijackCallback(NATIVE_CONTEXT* pThreadContext, Thread* pThreadToHijack);
#else // FEATURE_HIJACK
    void                Unhijack() { }
    bool                IsHijacked() { return false; }
    static bool         IsHijackTarget(void * address) { return false; }
#endif // FEATURE_HIJACK

#ifdef FEATURE_GC_STRESS
    static void         HijackForGcStress(PAL_LIMITED_CONTEXT * pSuspendCtx);
#endif // FEATURE_GC_STRESS

    bool                IsSuppressGcStressSet();
    void                SetSuppressGcStress();
    void                ClearSuppressGcStress();
    bool                IsWithinStackBounds(PTR_VOID p);
    void                GetStackBounds(PTR_VOID * ppStackLow, PTR_VOID * ppStackHigh);
    void                PushExInfo(ExInfo * pExInfo);
    void                ValidateExInfoPop(ExInfo * pExInfo, void * limitSP);
    void                ValidateExInfoStack();
    bool                IsDoNotTriggerGcSet();
    void                SetDoNotTriggerGc();
    void                ClearDoNotTriggerGc();

    bool                IsDetached();
    void                SetDetached();

    PTR_VOID            GetThreadStressLog() const;
#ifndef DACCESS_COMPILE
    void                SetThreadStressLog(void * ptsl);
#endif // DACCESS_COMPILE
#ifdef FEATURE_GC_STRESS
    void                SetRandomSeed(uint32_t seed);
    uint32_t            NextRand();
    bool                IsRandInited();
#endif // FEATURE_GC_STRESS
    PTR_ExInfo          GetCurExInfo();

    bool                IsCurrentThreadInCooperativeMode();

    PInvokeTransitionFrame* GetTransitionFrameForStackTrace();
    PInvokeTransitionFrame* GetTransitionFrameForSampling();
    void *              GetCurrentThreadPInvokeReturnAddress();

    //
    // The set of operations used to support unmanaged code running in cooperative mode
    //
    void                EnablePreemptiveMode();
    void                DisablePreemptiveMode();

    // Set the m_pDeferredTransitionFrame field for GC allocation helpers that setup transition frame
    // in assembly code. Do not use anywhere else.
    void                SetDeferredTransitionFrame(PInvokeTransitionFrame* pTransitionFrame);

    // Setup the m_pDeferredTransitionFrame field for GC helpers entered via regular PInvoke.
    // Do not use anywhere else.
    void                DeferTransitionFrame();

    // Setup the m_pDeferredTransitionFrame field for GC helpers entered from native helper thread
    // code (e.g. ETW or EventPipe threads). Do not use anywhere else.
    void                SetDeferredTransitionFrameForNativeHelperThread();

    //
    // GC support APIs - do not use except from GC itself
    //
    void SetGCSpecial();
    bool IsGCSpecial();

    //
    // Managed/unmanaged interop transitions support APIs
    //
    void WaitForGC(PInvokeTransitionFrame* pTransitionFrame);

    void ReversePInvokeAttachOrTrapThread(ReversePInvokeFrame * pFrame);

    bool InlineTryFastReversePInvoke(ReversePInvokeFrame * pFrame);
    void InlineReversePInvokeReturn(ReversePInvokeFrame * pFrame);

    void InlinePInvoke(PInvokeTransitionFrame * pFrame);
    void InlinePInvokeReturn(PInvokeTransitionFrame * pFrame);

    Object* GetThreadAbortException();
    void SetThreadAbortException(Object *exception);

    Object** GetThreadStaticStorage();

    InlinedThreadStaticRoot* GetInlinedThreadStaticList();
    void RegisterInlinedThreadStaticRoot(InlinedThreadStaticRoot* newRoot, TypeManager* typeManager);

    NATIVE_CONTEXT* GetInterruptedContext();

    void PushGCFrameRegistration(GCFrameRegistration* pRegistration);
    void PopGCFrameRegistration(GCFrameRegistration* pRegistration);

#ifdef FEATURE_SUSPEND_REDIRECTION
    NATIVE_CONTEXT* EnsureRedirectionContext();
#endif //FEATURE_SUSPEND_REDIRECTION

    bool                IsActivationPending();
    void                SetActivationPending(bool isPending);

#ifdef TARGET_WINDOWS
    HANDLE              GetOSThreadHandle() { return m_hOSThread; }
#else
    pthread_t           GetOSThreadHandle() { return m_hOSThread; }
#endif

#ifdef TARGET_X86
    void                SetPendingRedirect(PCODE eip);
    bool                CheckPendingRedirect(PCODE eip);
#endif
};

#ifndef __GCENV_BASE_INCLUDED__
typedef DPTR(Object) PTR_Object;
typedef DPTR(PTR_Object) PTR_PTR_Object;
#endif // !__GCENV_BASE_INCLUDED__

#endif // __thread_h__
