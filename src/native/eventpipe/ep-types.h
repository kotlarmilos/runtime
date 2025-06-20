#ifndef __EVENTPIPE_TYPES_H__
#define __EVENTPIPE_TYPES_H__

#ifdef ENABLE_PERFTRACING
#include "ep-ipc-pal-types.h"

#undef EP_IMPL_GETTER_SETTER
#ifdef EP_IMPL_EP_GETTER_SETTER
#define EP_IMPL_GETTER_SETTER
#endif
#include "ep-getter-setter.h"

#include "ep-types-forward.h"

#include "ep-rt-types.h"

#include <containers/dn-vector.h>
#include <containers/dn-vector-ptr.h>
#include <containers/dn-list.h>
#include <containers/dn-queue.h>
#include <containers/dn-umap-t.h>
#include <minipal/guid.h>

/*
 * EventFilterDescriptor.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventFilterDescriptor {
#else
struct _EventFilterDescriptor_Internal {
#endif
	uint64_t ptr;
	uint32_t size;
	uint32_t type;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventFilterDescriptor {
	uint8_t _internal [sizeof (struct _EventFilterDescriptor_Internal)];
};
#endif

EventFilterDescriptor *
ep_event_filter_desc_alloc (
	uint64_t ptr,
	uint32_t size,
	uint32_t type);

EventFilterDescriptor *
ep_event_filter_desc_init (
	EventFilterDescriptor *event_filter_desc,
	uint64_t ptr,
	uint32_t size,
	uint32_t type
);

void
ep_event_filter_desc_fini (EventFilterDescriptor * filter_desc);

void
ep_event_filter_desc_free (EventFilterDescriptor * filter_desc);

/*
 * EventPipeProviderCallbackData.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderCallbackData {
#else
struct _EventPipeProviderCallbackData_Internal {
#endif
	ep_char8_t *filter_data;
	EventPipeCallback callback_function;
	void *callback_data;
	int64_t keywords;
	EventPipeEventLevel provider_level;
	bool enabled;
	EventPipeSessionID session_id;
	EventPipeProvider *provider;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderCallbackData {
	uint8_t _internal [sizeof (struct _EventPipeProviderCallbackData_Internal)];
};
#endif

EP_DEFINE_GETTER(EventPipeProviderCallbackData *, provider_callback_data, const ep_char8_t *, filter_data)
EP_DEFINE_GETTER(EventPipeProviderCallbackData *, provider_callback_data, EventPipeCallback, callback_function)
EP_DEFINE_GETTER(EventPipeProviderCallbackData *, provider_callback_data, void *, callback_data)
EP_DEFINE_GETTER(EventPipeProviderCallbackData *, provider_callback_data, int64_t, keywords)
EP_DEFINE_GETTER(EventPipeProviderCallbackData *, provider_callback_data, EventPipeEventLevel, provider_level)
EP_DEFINE_GETTER(EventPipeProviderCallbackData *, provider_callback_data, bool, enabled)
EP_DEFINE_GETTER(EventPipeProviderCallbackData *, provider_callback_data, EventPipeSessionID, session_id)

EventPipeProviderCallbackData *
ep_provider_callback_data_alloc (
	const ep_char8_t *filter_data,
	EventPipeCallback callback_function,
	void *callback_data,
	int64_t keywords,
	EventPipeEventLevel provider_level,
	bool enabled,
	EventPipeSessionID session_id,
	EventPipeProvider *provider);

EventPipeProviderCallbackData *
ep_provider_callback_data_alloc_copy (EventPipeProviderCallbackData *provider_callback_data_src);

EventPipeProviderCallbackData *
ep_provider_callback_data_alloc_move (EventPipeProviderCallbackData *provider_callback_data_src);

EventPipeProviderCallbackData *
ep_provider_callback_data_init (
	EventPipeProviderCallbackData *provider_callback_data,
	const ep_char8_t *filter_data,
	EventPipeCallback callback_function,
	void *callback_data,
	int64_t keywords,
	EventPipeEventLevel provider_level,
	bool enabled,
	EventPipeSessionID session_id,
	EventPipeProvider *provider);

EventPipeProviderCallbackData *
ep_provider_callback_data_init_copy (
	EventPipeProviderCallbackData *provider_callback_data_dst,
	EventPipeProviderCallbackData *provider_callback_data_src);

EventPipeProviderCallbackData *
ep_provider_callback_data_init_move (
	EventPipeProviderCallbackData *provider_callback_data_dst,
	EventPipeProviderCallbackData *provider_callback_data_src);

void
ep_provider_callback_data_fini (EventPipeProviderCallbackData *provider_callback_data);

void
ep_provider_callback_data_free (EventPipeProviderCallbackData *provider_callback_data);

/*
 * EventPipeProviderCallbackDataQueue.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderCallbackDataQueue {
#else
struct _EventPipeProviderCallbackDataQueue_Internal {
#endif
	dn_queue_t *queue;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderCallbackDataQueue {
	uint8_t _internal [sizeof (struct _EventPipeProviderCallbackDataQueue_Internal)];
};
#endif

EP_DEFINE_GETTER(EventPipeProviderCallbackDataQueue *, provider_callback_data_queue, dn_queue_t *, queue)

EventPipeProviderCallbackDataQueue *
ep_provider_callback_data_queue_init (EventPipeProviderCallbackDataQueue *provider_callback_data_queue);

void
ep_provider_callback_data_queue_fini (EventPipeProviderCallbackDataQueue *provider_callback_data_queue);

bool
ep_provider_callback_data_queue_enqueue (
	EventPipeProviderCallbackDataQueue *provider_callback_data_queue,
	EventPipeProviderCallbackData *provider_callback_data);

bool
ep_provider_callback_data_queue_try_dequeue (
	EventPipeProviderCallbackDataQueue *provider_callback_data_queue,
	EventPipeProviderCallbackData *provider_callback_data);

/*
 * EventPipeProviderEventFilter.
 *
 * Used as read-only data to configure a SessionProvider's set EventFilter,
 * matching the DiagnosticServer IPC Protocol encoding specification.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderEventFilter {
#else
struct _EventPipeProviderEventFilter_Internal {
#endif
	bool enable;
	uint32_t length;
	uint32_t *event_ids;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderEventFilter {
	uint8_t _internal [sizeof (struct _EventPipeProviderEventFilter_Internal)];
};
#endif

void
eventpipe_collect_tracing_command_free_event_filter (EventPipeProviderEventFilter *event_filter);

/*
 * EventPipeProviderTracepointSet.
 *
 * Used as read-only data to configure a Tracepoint Configuration's set of non-default
 * tracepoints, matching the DiagnosticServer IPC Protocol encoding specification.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderTracepointSet {
#else
struct _EventPipeProviderTracepointSet_Internal {
#endif
	ep_char8_t *tracepoint_name;
	uint32_t *event_ids;
	uint32_t event_ids_length;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderTracepointSet {
	uint8_t _internal [sizeof (struct _EventPipeProviderTracepointSet_Internal)];
};
#endif

void
eventpipe_collect_tracing_command_free_tracepoint_sets (EventPipeProviderTracepointSet *tracepoint_sets, uint32_t tracepoint_sets_len);

/*
 * EventPipeProviderTracepointConfiguration.
 *
 * Used as read-only data to configure the SessionProvider's Tracepoint
 * configuration, matching the DiagnosticServer IPC Protocol encoding specification.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderTracepointConfiguration {
#else
struct _EventPipeProviderTracepointConfiguration_Internal {
#endif
	ep_char8_t *default_tracepoint_name;
	EventPipeProviderTracepointSet *non_default_tracepoints;
	uint32_t non_default_tracepoints_length;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderTracepointConfiguration {
	uint8_t _internal [sizeof (struct _EventPipeProviderTracepointConfiguration_Internal)];
};
#endif

void
eventpipe_collect_tracing_command_free_tracepoint_config (EventPipeProviderTracepointConfiguration *tracepoint_config);

/*
 * EventPipeProviderConfiguration.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderConfiguration {
#else
struct _EventPipeProviderConfiguration_Internal {
#endif
	ep_char8_t *provider_name;
	ep_char8_t *filter_data;
	uint64_t keywords;
	EventPipeEventLevel logging_level;
	EventPipeProviderEventFilter *event_filter;
	EventPipeProviderTracepointConfiguration *tracepoint_config;
};


#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeProviderConfiguration {
	uint8_t _internal [sizeof (struct _EventPipeProviderConfiguration_Internal)];
};
#endif

EP_DEFINE_GETTER(EventPipeProviderConfiguration *, provider_config, const ep_char8_t *, provider_name)
EP_DEFINE_GETTER(EventPipeProviderConfiguration *, provider_config, const ep_char8_t *, filter_data)
EP_DEFINE_GETTER(EventPipeProviderConfiguration *, provider_config, uint64_t, keywords)
EP_DEFINE_GETTER(EventPipeProviderConfiguration *, provider_config, EventPipeEventLevel, logging_level)
EP_DEFINE_GETTER(EventPipeProviderConfiguration *, provider_config, const EventPipeProviderEventFilter *, event_filter)
EP_DEFINE_GETTER(EventPipeProviderConfiguration *, provider_config, const EventPipeProviderTracepointConfiguration *, tracepoint_config)

EventPipeProviderConfiguration *
ep_provider_config_init (
	EventPipeProviderConfiguration *provider_config,
	const ep_char8_t *provider_name,
	uint64_t keywords,
	EventPipeEventLevel logging_level,
	const ep_char8_t *filter_data);

void
ep_provider_config_fini (EventPipeProviderConfiguration *provider_config);

/*
 * EventPipeExecutionCheckpoint.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeExecutionCheckpoint {
#else
struct _EventPipeExecutionCheckpoint_Internal {
#endif
	ep_char8_t *name;
	ep_timestamp_t timestamp;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeExecutionCheckpoint {
	uint8_t _internal [sizeof (struct _EventPipeExecutionCheckpoint_Internal)];
};
#endif

EP_DEFINE_GETTER(EventPipeExecutionCheckpoint *, execution_checkpoint, const ep_char8_t *, name)
EP_DEFINE_GETTER(EventPipeExecutionCheckpoint *, execution_checkpoint, const ep_timestamp_t, timestamp)

EventPipeExecutionCheckpoint *
ep_execution_checkpoint_alloc (
	const ep_char8_t *name,
	ep_timestamp_t timestamp);

void
ep_execution_checkpoint_free (EventPipeExecutionCheckpoint *execution_checkpoint);

static
inline
const ep_char8_t *
ep_config_get_default_provider_name_utf8 (void)
{
	return "Microsoft-DotNETCore-EventPipeConfiguration";
}

static
inline
const ep_char8_t *
ep_config_get_public_provider_name_utf8 (void)
{
	return "Microsoft-Windows-DotNETRuntime";
}

static
inline
const ep_char8_t *
ep_config_get_private_provider_name_utf8 (void)
{
	return "Microsoft-Windows-DotNETRuntimePrivate";
}

static
inline
const ep_char8_t *
ep_config_get_rundown_provider_name_utf8 (void)
{
	return "Microsoft-Windows-DotNETRuntimeRundown";
}

static
inline
const ep_char8_t *
ep_config_get_sample_profiler_provider_name_utf8 (void)
{
	return "Microsoft-DotNETCore-SampleProfiler";
}

/*
 * EventPipeSystemTime.
 */

#if defined(EP_INLINE_GETTER_SETTER) || defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeSystemTime {
#else
struct _EventPipeSystemTime_Internal {
#endif
	uint16_t year;
	uint16_t month;
	uint16_t day_of_week;
	uint16_t day;
	uint16_t hour;
	uint16_t minute;
	uint16_t second;
	uint16_t milliseconds;
};

#if !defined(EP_INLINE_GETTER_SETTER) && !defined(EP_IMPL_EP_GETTER_SETTER)
struct _EventPipeSystemTime {
	uint8_t _internal [sizeof (struct _EventPipeSystemTime_Internal)];
};
#endif

EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, year);
EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, month);
EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, day_of_week);
EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, day);
EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, hour);
EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, minute);
EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, second);
EP_DEFINE_GETTER(EventPipeSystemTime *, system_time, uint16_t, milliseconds);

void
ep_system_time_set (
	EventPipeSystemTime *system_time,
	uint16_t year,
	uint16_t month,
	uint16_t day_of_week,
	uint16_t day,
	uint16_t hour,
	uint16_t minute,
	uint16_t second,
	uint16_t milliseconds);

#endif /* ENABLE_PERFTRACING */
#endif /* __EVENTPIPE_TYPES_H__ */
