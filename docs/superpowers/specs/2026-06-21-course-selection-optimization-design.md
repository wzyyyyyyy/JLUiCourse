# Aggressive Course Selection and Per-Course Status Design

## Goal

Improve the chance of selecting a course during a short availability window while keeping concurrency bounded, cancelling redundant work promptly, and replacing the scrolling log panel with stable per-course status rows.

## Scope

This change covers the course-selection execution path started from the main window and the presentation of that run. It does not change authentication, batch selection, course search, favorites management, or packaging.

The selected behavior is intentionally aggressive:

- Each course uses two concurrent racing lanes.
- At most 20 HTTP requests may be in flight across the entire run.
- A full course is a terminal failure and is not retried.
- Explicit rate limiting is respected with adaptive backoff.
- Only one selection run may exist at a time.

## Current Problems

The current implementation starts a CPU-sized worker pool for each click and stores the cancellation source and queue on the API object. Repeated clicks can accumulate consumers, and completion of one run can cancel workers belonging to another. A course occupies one worker and retries inside an unbounded loop, while request headers are mutated on the shared client during concurrent work.

Selection feedback is written into the same scrolling log used for login and diagnostics. The user cannot quickly see which course is waiting, retrying, successful, or failed, and repeated messages create avoidable UI and logging overhead.

## Architecture

### Course selection transport

Introduce a focused transport boundary for a single selection attempt. It creates a new `HttpRequestMessage` and form content for every attempt, adds the course-selection referrer to that request, sends it with a cancellation token, and returns the HTTP status plus response body. Shared `DefaultRequestHeaders` are not mutated during concurrent attempts.

The transport is independently replaceable in tests, so the selection algorithm does not need a live university service.

### Response classifier

Introduce a pure response classifier that converts an HTTP/business response into one of:

- `Success`: HTTP/business success or the course is already present in the result.
- `TerminalFailure`: capacity full, time conflict, ineligible student, invalid course, ended batch, or another explicit permanent business failure.
- `Retry`: not-yet-open, temporary server busy, network failure, timeout, HTTP 5xx, or another recognized transient response.
- `RateLimited`: HTTP 429 or an equivalent business response, including an optional server-provided retry delay.

The classifier also provides the concise user-facing reason shown in the status table. A capacity-full response always maps directly to `TerminalFailure`.

### Selection engine

Create a selection engine whose inputs are the favorite courses, the attempt transport, progress callback, and cancellation token. The engine owns all state for one run; it does not use a persistent queue or persistent workers.

For every course, the engine creates a linked course cancellation source and starts two racing lanes. Each lane follows this loop:

1. Wait for a slot in the global `SemaphoreSlim(20)`.
2. Send one fresh request and release the slot in `finally`.
3. Classify the response and publish an immutable course-status snapshot.
4. Finish on success or terminal failure.
5. Otherwise wait using a cancellable delay and try again.

Normal transient retries use 40–100 ms of jitter. Rate-limited requests use `Retry-After` when present, otherwise a short exponential delay capped at two seconds. Five consecutive identical unknown responses across both lanes of one course are treated as a terminal failure so that course cannot loop forever on an unrecognized response.

The first lane that produces success or terminal failure atomically sets the course result and cancels its sibling. Late results are ignored. The engine completes only after every course has one final result or the run is cancelled.

### Run coordinator

`JLUiCourseApi` remains responsible for loading favorites and invoking the engine, but no longer owns a queue or worker pool. It guards the active run so a second start request returns without starting work and reports “选课任务正在进行中”. It exposes cancellation for the Stop action and always clears the active-run state in `finally`.

The existing keep-online work remains separate. It must not mutate selection request headers, and its lifetime must remain tied to the authenticated session rather than to each course-selection run.

## Data Flow

1. The user clicks Start.
2. The view model marks the run active and asks the API to start.
3. Favorites are loaded once.
4. One initial `Waiting` snapshot is emitted for every course.
5. The engine runs two lanes per course under the global 20-request limiter.
6. Snapshots are dispatched to the UI thread and update the existing row matching `CourseId`.
7. Final snapshots update summary counts and overall progress.
8. The Start action is restored after all courses finish or the user stops the run.

## Per-Course Status Model

Each status row has one stable identity and contains:

- Course ID
- Course display name
- State: `Waiting`, `Racing`, `BackingOff`, `Succeeded`, `Failed`, or `Cancelled`
- Total attempt count across both lanes
- Elapsed duration
- Latest concise result/reason

Status updates replace these values on the existing object; they never append a new row for the same course.

While both lanes are active, the row shows `Racing` if either lane has a request in flight. It shows `BackingOff` only when neither lane has an in-flight request and at least one lane is waiting to retry. A final success, failure, or cancellation always takes precedence over an intermediate state.

## Main Window Design

Replace the right-side scrolling log panel with a status workspace:

- Header: total course count plus running, successful, and failed counts.
- Body: a read-only table with columns `课程`, `状态`, `尝试次数`, `耗时`, and `最新结果`.
- Footer: existing overall progress bar.
- Actions: Start is disabled during an active run; Stop is visible and enabled during an active run.

Status is communicated through both text and color, so color is not the only signal:

- Waiting and cancelled: neutral gray.
- Racing: blue.
- Backing off: amber.
- Succeeded: green.
- Failed: red.

Login, session-expiry, and other system-level messages appear as one concise banner above the table. Full diagnostic detail continues to be written to the log file, not streamed into the main window.

An empty favorites response produces a visible banner and leaves the table empty. It is not treated as a successful selection run.

## Error and Cancellation Behavior

- Success or already-selected cancels the sibling lane and completes the course successfully.
- Capacity full cancels the sibling lane and completes the course as failed immediately.
- Explicit permanent business errors complete the course as failed immediately.
- Transient business errors use the aggressive jittered retry.
- Timeout and network failures use a short progressive retry delay.
- Rate limiting respects the server delay and never attempts to bypass it.
- Five identical unknown responses complete the course as failed with that response visible.
- Stop cancels every lane and semaphore wait promptly. Unfinished rows become `Cancelled`.
- A second Start while a run is active returns immediately, reports “选课任务正在进行中”, and creates no additional work.

## Logging

The table is the live selection UI. The file logger records run start, final per-course result, run summary, and exceptional failures. It does not log every ordinary retry. This reduces UI dispatching, file I/O, and noise during the most latency-sensitive period.

## Testing and Acceptance Criteria

Use test-driven development for each behavior.

### Response classification

- Business success and already-selected map to success.
- Capacity full maps to terminal failure.
- Known permanent failures stop immediately.
- Known transient failures retry.
- HTTP 429 and equivalent messages map to rate limiting with the expected delay.
- Unknown response handling stops after five identical consecutive responses.

### Concurrency and racing

- Exactly two lanes are started per course.
- No more than 20 transport calls are in flight at once, including a run with at least 50 courses.
- Success in one lane cancels its sibling before another request is sent.
- Capacity full in one lane cancels its sibling immediately.
- Semaphore permits are released after success, failure, exception, and cancellation.
- Cancelling a run leaves no background consumers or unfinished engine tasks.
- Starting twice does not create a second concurrent run.

### Status presentation

- Initial favorites create exactly one row per course.
- Updates for the same course change the existing row rather than adding one.
- Attempts, elapsed time, reason, progress, and summary counts remain consistent.
- Start and Stop actions reflect the active-run state.
- Empty favorites and session-level errors appear in the banner.

### Repository verification

- `dotnet test --configuration Release` passes.
- The application builds successfully in Release configuration, including Avalonia XAML compilation.

## Out of Scope

- Retrying a course after a capacity-full response.
- User-configurable concurrency or retry timing.
- Course prioritization or reordering.
- Bypassing rate limiting or other server controls.
- Redesigning login, batch selection, course query, or favorites workflows.
