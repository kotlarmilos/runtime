// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifdef __cplusplus
extern "C" {
#endif // __cplusplus

// Gluing macro expansions together requires nested macro invocation :/
#ifndef DN_SIMDHASH_GLUE
#define DN_SIMDHASH_GLUE_INNER(a, b) a ## b
#define DN_SIMDHASH_GLUE(a,b) DN_SIMDHASH_GLUE_INNER(a, b)
#endif
#ifndef DN_SIMDHASH_GLUE_3
#define DN_SIMDHASH_GLUE_3_INNER(a, b, c) a ## b ## c
#define DN_SIMDHASH_GLUE_3(a, b, c) DN_SIMDHASH_GLUE_3_INNER(a, b, c)
#endif

#ifndef DN_SIMDHASH_ACCESSOR_SUFFIX
#define DN_SIMDHASH_ACCESSOR_SUFFIX
#endif

// We generate unique names for each specialization so that they will be easy to distinguish
//  when debugging, profiling, or disassembling. Otherwise they would have linker-assigned names
#define DN_SIMDHASH_T_NAME DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_t)
#define DN_SIMDHASH_T_PTR DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_t *)
#define DN_SIMDHASH_T_VTABLE DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_vtable)
#define DN_SIMDHASH_T_META DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_meta)
#define DN_SIMDHASH_SCAN_BUCKET_INTERNAL DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_scan_bucket_internal)
#define DN_SIMDHASH_FIND_VALUE_INTERNAL DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_find_value_internal)
#define DN_SIMDHASH_TRY_INSERT_INTERNAL DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_try_insert_internal)
#define DN_SIMDHASH_REHASH_INTERNAL DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_rehash_internal)
#define DN_SIMDHASH_NEW DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_new)
#define DN_SIMDHASH_TRY_ADD DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_add,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_TRY_ADD_WITH_HASH DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_add_with_hash,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_TRY_GET_VALUE DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_get_value,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_TRY_GET_VALUE_WITH_HASH DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_get_value_with_hash,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_TRY_REMOVE DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_remove,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_TRY_REMOVE_WITH_HASH DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_remove_with_hash,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_TRY_REPLACE_VALUE DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_replace_value,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_TRY_REPLACE_VALUE_WITH_HASH DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_try_replace_value_with_hash,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_FOREACH DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_foreach,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_FOREACH_FUNC DN_SIMDHASH_GLUE_3(DN_SIMDHASH_T,_foreach_func,DN_SIMDHASH_ACCESSOR_SUFFIX)
#define DN_SIMDHASH_DESTROY_ALL DN_SIMDHASH_GLUE(DN_SIMDHASH_T,_destroy_all)

typedef void (*DN_SIMDHASH_FOREACH_FUNC) (DN_SIMDHASH_KEY_T key, DN_SIMDHASH_VALUE_T value, void *user_data);

// Declare a specific alias so intellisense gives more helpful info
typedef dn_simdhash_t DN_SIMDHASH_T_NAME;

#ifndef DN_SIMDHASH_NO_DEFAULT_NEW
DN_SIMDHASH_T_PTR
DN_SIMDHASH_NEW (uint32_t capacity, dn_allocator_t *allocator);
#endif

dn_simdhash_add_result
DN_SIMDHASH_TRY_ADD (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key, DN_SIMDHASH_VALUE_T value);

dn_simdhash_add_result
DN_SIMDHASH_TRY_ADD_WITH_HASH (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key, uint32_t key_hash, DN_SIMDHASH_VALUE_T value);

// result is an optional parameter that will be set to the value of the item if it was found.
uint8_t
DN_SIMDHASH_TRY_GET_VALUE (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key, DN_SIMDHASH_VALUE_T *result);

// result is an optional parameter that will be set to the value of the item if it was found.
uint8_t
DN_SIMDHASH_TRY_GET_VALUE_WITH_HASH (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key, uint32_t key_hash, DN_SIMDHASH_VALUE_T *result);

uint8_t
DN_SIMDHASH_TRY_REMOVE (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key);

uint8_t
DN_SIMDHASH_TRY_REMOVE_WITH_HASH (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key, uint32_t key_hash);

uint8_t
DN_SIMDHASH_TRY_REPLACE_VALUE (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key, DN_SIMDHASH_VALUE_T new_value);

uint8_t
DN_SIMDHASH_TRY_REPLACE_VALUE_WITH_HASH (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_KEY_T key, uint32_t key_hash, DN_SIMDHASH_VALUE_T new_value);

void
DN_SIMDHASH_FOREACH (DN_SIMDHASH_T_PTR hash, DN_SIMDHASH_FOREACH_FUNC func, void *user_data);

#ifdef __cplusplus
}
#endif // __cplusplus
