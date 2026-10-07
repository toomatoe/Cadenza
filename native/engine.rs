//! Native command/event foundation. No Spotify session or audio backend is wired yet.
//! Opcode 0 is a bridge diagnostic. All playback commands explicitly return unsupported.
use std::collections::{HashMap, VecDeque};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock, mpsc};
use std::thread::{self, JoinHandle};
use std::time::Duration;

const CAPACITY: usize = 32;
const OK: i32 = 0;
const EMPTY: i32 = 1;
const INVALID: i32 = 2;
const FULL: i32 = 3;
const UNSUPPORTED: i32 = 4;
const INTERNAL: i32 = 5;

#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct Event {
    pub request_id: u64,
    pub kind: u32,
    pub status: i32,
    pub value: u32,
    pub reserved: u32,
}

#[derive(Clone, Copy)]
struct Command {
    request_id: u64,
    opcode: u32,
}
#[derive(Default)]
struct State {
    events: VecDeque<Event>,
    outstanding: usize,
}
struct Engine {
    tx: mpsc::SyncSender<Command>,
    state: Arc<Mutex<State>>,
    running: Arc<AtomicBool>,
    worker: Mutex<Option<JoinHandle<()>>>,
}
static ENGINES: OnceLock<Mutex<HashMap<u64, Arc<Engine>>>> = OnceLock::new();
static NEXT_ID: AtomicU64 = AtomicU64::new(1);
fn registry() -> &'static Mutex<HashMap<u64, Arc<Engine>>> {
    ENGINES.get_or_init(Default::default)
}
fn find(id: u64) -> Option<Arc<Engine>> {
    registry().lock().ok()?.get(&id).cloned()
}
fn guarded(action: impl FnOnce() -> i32) -> i32 {
    catch_unwind(AssertUnwindSafe(action)).unwrap_or(INTERNAL)
}

#[unsafe(no_mangle)]
pub extern "C" fn apurva_abi_version() -> u32 {
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn apurva_create() -> u64 {
    catch_unwind(|| {
        let (tx, rx) = mpsc::sync_channel::<Command>(CAPACITY);
        let state = Arc::new(Mutex::new(State::default()));
        let running = Arc::new(AtomicBool::new(true));
        let worker_state = state.clone();
        let worker_running = running.clone();
        let Ok(worker) = thread::Builder::new()
            .name("apurva-audio".into())
            .spawn(move || {
                while worker_running.load(Ordering::Acquire) {
                    match rx.recv_timeout(Duration::from_millis(50)) {
                        Ok(command) => {
                            let event = Event {
                                request_id: command.request_id,
                                kind: if command.opcode == 0 { 1 } else { 2 },
                                status: if command.opcode == 0 { OK } else { UNSUPPORTED },
                                value: if command.opcode == 0 { 1 } else { 0 },
                                reserved: 0,
                            };
                            let Ok(mut state) = worker_state.lock() else {
                                break;
                            };
                            state.events.push_back(event);
                        }
                        Err(mpsc::RecvTimeoutError::Timeout) => {}
                        Err(mpsc::RecvTimeoutError::Disconnected) => break,
                    }
                }
            })
        else {
            return 0;
        };
        let engine = Arc::new(Engine {
            tx,
            state,
            running,
            worker: Mutex::new(Some(worker)),
        });
        let Ok(mut all) = registry().lock() else {
            engine.running.store(false, Ordering::Release);
            if let Ok(mut worker) = engine.worker.lock()
                && let Some(w) = worker.take()
            {
                let _ = w.join();
            }
            return 0;
        };
        let id = NEXT_ID.fetch_add(1, Ordering::Relaxed);
        all.insert(id, engine);
        id
    })
    .unwrap_or(0)
}

#[unsafe(no_mangle)]
pub extern "C" fn apurva_submit(id: u64, request_id: u64, opcode: u32) -> i32 {
    guarded(|| {
        let Some(engine) = find(id) else {
            return INVALID;
        };
        if request_id == 0 || !engine.running.load(Ordering::Acquire) {
            return INVALID;
        }
        let Ok(mut state) = engine.state.lock() else {
            return INTERNAL;
        };
        // Includes queued commands and unconsumed events: no completion is silently dropped.
        if state.outstanding >= CAPACITY {
            return FULL;
        }
        match engine.tx.try_send(Command { request_id, opcode }) {
            Ok(()) => {
                state.outstanding += 1;
                OK
            }
            Err(mpsc::TrySendError::Full(_)) => FULL,
            Err(mpsc::TrySendError::Disconnected(_)) => INTERNAL,
        }
    })
}

/// # Safety
/// `output` must point to one writable, aligned Event for the duration of this call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn apurva_poll(id: u64, output: *mut Event) -> i32 {
    guarded(|| {
        if output.is_null() {
            return INVALID;
        }
        let Some(engine) = find(id) else {
            return INVALID;
        };
        let Ok(mut state) = engine.state.lock() else {
            return INTERNAL;
        };
        match state.events.pop_front() {
            Some(event) => {
                state.outstanding -= 1;
                unsafe {
                    output.write(event);
                }
                OK
            }
            None => EMPTY,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn apurva_destroy(id: u64) -> i32 {
    guarded(|| {
        let Some(engine) = registry().lock().ok().and_then(|mut all| all.remove(&id)) else {
            return INVALID;
        };
        engine.running.store(false, Ordering::Release);
        let Ok(mut worker) = engine.worker.lock() else {
            return INTERNAL;
        };
        if let Some(worker) = worker.take()
            && worker.join().is_err()
        {
            return INTERNAL;
        }
        OK
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    fn receive(id: u64) -> Event {
        let deadline = std::time::Instant::now() + Duration::from_secs(2);
        loop {
            let mut event = Event::default();
            let result = unsafe { apurva_poll(id, &mut event) };
            if result == OK {
                return event;
            }
            assert_eq!(result, EMPTY);
            assert!(std::time::Instant::now() < deadline);
            thread::yield_now();
        }
    }
    #[test]
    fn diagnostic_and_unsupported_playback() {
        assert_eq!(std::mem::size_of::<Event>(), 24);
        let id = apurva_create();
        assert_ne!(id, 0);
        assert_eq!(apurva_submit(id, 7, 0), OK);
        assert_eq!(receive(id).request_id, 7);
        assert_eq!(apurva_submit(id, 8, 1), OK);
        assert_eq!(receive(id).status, UNSUPPORTED);
        assert_eq!(apurva_destroy(id), OK);
        assert_eq!(apurva_submit(id, 9, 0), INVALID);
        assert_eq!(apurva_destroy(id), INVALID);
    }
    #[test]
    fn bounded_backpressure_without_lost_events() {
        let id = apurva_create();
        for request in 1..=CAPACITY as u64 {
            assert_eq!(apurva_submit(id, request, 0), OK);
        }
        assert_eq!(apurva_submit(id, 100, 0), FULL);
        for request in 1..=CAPACITY as u64 {
            assert_eq!(receive(id).request_id, request);
        }
        assert_eq!(apurva_submit(id, 100, 0), OK);
        assert_eq!(receive(id).request_id, 100);
        assert_eq!(apurva_destroy(id), OK);
    }
    #[test]
    fn invalid_inputs() {
        let id = apurva_create();
        assert_eq!(apurva_submit(id, 0, 0), INVALID);
        assert_eq!(unsafe { apurva_poll(id, std::ptr::null_mut()) }, INVALID);
        assert_eq!(apurva_destroy(id), OK);
    }
}
