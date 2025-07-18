// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef __eetype_inl__
#define __eetype_inl__
//-----------------------------------------------------------------------------------------------------------
#if !defined(DACCESS_COMPILE)
inline PTR_uint8_t FollowRelativePointer(const int32_t* pDist)
{
    int32_t dist = *pDist;

    PTR_uint8_t result = (PTR_uint8_t)pDist + dist;

    return result;
}

inline TypeManagerHandle* MethodTable::GetTypeManagerPtr()
{
    uint32_t cbOffset = GetFieldOffset(ETF_TypeManagerIndirection);

#if !defined(USE_PORTABLE_HELPERS)
    if (!IsDynamicType())
    {
        return (TypeManagerHandle*)FollowRelativePointer((int32_t*)((uint8_t*)this + cbOffset));
    }
    else
#endif
    {
        return *(TypeManagerHandle**)((uint8_t*)this + cbOffset);
    }
}

inline MethodTable* MethodTable::GetDynamicTemplateType()
{
    uint32_t cbOffset = GetFieldOffset(ETF_DynamicTemplateType);
    return *(MethodTable**)((uint8_t*)this + cbOffset);
}

#endif // !defined(DACCESS_COMPILE)

// Calculate the offset of a field of the MethodTable that has a variable offset.
__forceinline uint32_t MethodTable::GetFieldOffset(EETypeField eField)
{
    // First part of MethodTable consists of the fixed portion followed by the vtable.
    uint32_t cbOffset = offsetof(MethodTable, m_VTable) + (sizeof(UIntTarget) * m_usNumVtableSlots);

    // Followed by interface list
    cbOffset += sizeof(MethodTable*) * GetNumInterfaces();

    const uint32_t relativeOrFullPointerOffset =
#if USE_PORTABLE_HELPERS
        sizeof(UIntTarget);
#else
        IsDynamicType() ? sizeof(UIntTarget) : sizeof(uint32_t);
#endif

    // Followed by the type manager indirection cell.
    if (eField == ETF_TypeManagerIndirection)
    {
        return cbOffset;
    }
    cbOffset += relativeOrFullPointerOffset;

    // Followed by writable data.
    if (eField == ETF_WritableData)
    {
        return cbOffset;
    }
    cbOffset += relativeOrFullPointerOffset;

    // Followed by pointer to the dispatch map
    if (eField == ETF_DispatchMap)
    {
        ASSERT(HasDispatchMap());
        return cbOffset;
    }
    if (HasDispatchMap())
        cbOffset += relativeOrFullPointerOffset;

    // Followed by the pointer to the finalizer method.
    if (eField == ETF_Finalizer)
    {
        ASSERT(HasFinalizer());
        return cbOffset;
    }
    if (HasFinalizer())
        cbOffset += relativeOrFullPointerOffset;

    // Followed by the pointer to the sealed virtual slots
    if (eField == ETF_SealedVirtualSlots)
    {
        ASSERT(HasSealedVTableEntries());
        return cbOffset;
    }
    if (HasSealedVTableEntries())
        cbOffset += relativeOrFullPointerOffset;

    if (eField == ETF_GenericDefinition)
    {
        ASSERT(IsGeneric());
        return cbOffset;
    }
    if (IsGeneric())
        cbOffset += relativeOrFullPointerOffset;

    if (eField == ETF_GenericComposition)
    {
        ASSERT(IsGeneric() || (IsGenericTypeDefinition() && HasGenericVariance()));
        return cbOffset;
    }
    if (IsGeneric() || (IsGenericTypeDefinition() && HasGenericVariance()))
        cbOffset += relativeOrFullPointerOffset;

    if (eField == ETF_FunctionPointerParameters)
    {
        ASSERT(IsFunctionPointer());
        return cbOffset;
    }
    if (IsFunctionPointer())
        cbOffset += GetNumFunctionPointerParameters() * relativeOrFullPointerOffset;

    if (eField == ETF_DynamicTemplateType)
    {
        ASSERT(IsDynamicType());
        return cbOffset;
    }
    ASSERT(!"NYI");

    return 0;
}
#endif // __eetype_inl__
