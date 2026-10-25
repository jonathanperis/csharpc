// epoll for the corelib's socket engine (corelib/Net/SocketEngine.cs): struct epoll_event is packed on
// x86-64 and naturally aligned on arm64, so its layout stays on this side of the P/Invoke boundary.
#include "csrt.h"

#if defined(__linux__)
#include <errno.h>
#include <sys/epoll.h>

/// A new epoll instance (close-on-exec), or -errno.
int32_t csrt_epoll_create(void) {
    int fd = epoll_create1(EPOLL_CLOEXEC);
    return fd < 0 ? -errno : fd;
}

/// Watches `fd` edge-triggered for reading and writing, tagged with `data`; 0 or an errno.
int32_t csrt_epoll_add(int32_t epfd, int32_t fd, uint64_t data) {
    struct epoll_event ev = { .events = EPOLLIN | EPOLLOUT | EPOLLRDHUP | EPOLLET };
    ev.data.u64 = data;
    return epoll_ctl(epfd, EPOLL_CTL_ADD, fd, &ev) == 0 ? 0 : errno;
}

/// Waits for events (retrying EINTR): their tags and event masks; the count, or -errno.
int32_t csrt_epoll_wait(int32_t epfd, uint64_t* datas, uint32_t* events, int32_t max) {
    struct epoll_event buffer[256];
    if (max > 256) max = 256;
    for (;;) {
        int n = epoll_wait(epfd, buffer, max, -1);
        if (n < 0 && errno == EINTR) continue;
        if (n < 0) return -errno;
        for (int i = 0; i < n; i++) {
            datas[i] = buffer[i].data.u64;
            events[i] = buffer[i].events;
        }
        return n;
    }
}
#endif
