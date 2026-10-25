// POSIX signals for PosixSignalRegistration (corelib/RuntimeSignals.cs), as .NET's System.Native does it:
// the handler only writes the signal number to a pipe; a managed thread reads the pipe and runs the
// registered handlers on the thread pool. A signal no handler canceled gets its default disposition
// (terminating the process for SIGINT/SIGTERM/...), re-raised from csrt_signal_default.
#define _GNU_SOURCE
#include "csrt.h"

#if !defined(_WIN32) && !defined(__wasi__)
#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <string.h>
#include <unistd.h>

static int s_pipe[2] = { -1, -1 };

static void on_signal(int signo) {
    int saved = errno;
    uint8_t b = (uint8_t)signo;
    ssize_t ignored = write(s_pipe[1], &b, 1);
    (void)ignored;
    errno = saved;
}

/// The read end of the signal pipe (created once), or -errno.
int32_t csrt_signal_pipe(void) {
    if (s_pipe[0] >= 0) return s_pipe[0];
    if (pipe(s_pipe) != 0) return -errno;
    fcntl(s_pipe[0], F_SETFD, FD_CLOEXEC);
    fcntl(s_pipe[1], F_SETFD, FD_CLOEXEC);
    fcntl(s_pipe[1], F_SETFL, fcntl(s_pipe[1], F_GETFL) | O_NONBLOCK);
    return s_pipe[0];
}

/// Routes `signo` to the pipe; 0 or an errno.
int32_t csrt_signal_enable(int32_t signo) {
    struct sigaction sa;
    memset(&sa, 0, sizeof sa);
    sa.sa_handler = on_signal;
    sa.sa_flags = SA_RESTART;
    sigemptyset(&sa.sa_mask);
    return sigaction(signo, &sa, NULL) == 0 ? 0 : errno;
}

/// The last registration for `signo` went away: its default disposition again.
void csrt_signal_disable(int32_t signo) {
    struct sigaction sa;
    memset(&sa, 0, sizeof sa);
    sa.sa_handler = SIG_DFL;
    sigemptyset(&sa.sa_mask);
    sigaction(signo, &sa, NULL);
}

/// No handler canceled the signal: the default action (termination for SIGINT, SIGTERM, SIGQUIT, SIGHUP),
/// then back to the handler if the process goes on (SIGCONT, SIGCHLD, SIGWINCH, the stop signals).
void csrt_signal_default(int32_t signo) {
    csrt_signal_disable(signo);
    raise(signo);
    csrt_signal_enable(signo);
}
#endif
