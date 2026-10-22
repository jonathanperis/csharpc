// maxrss: run a command once and print the child's peak resident set size (KiB) to stderr.
// A tiny C parent (unlike a Python one) adds almost nothing to the child's pre-exec footprint.
//   cc -O2 -o maxrss tools/maxrss.c && ./maxrss ./hello
#define _GNU_SOURCE
#include <stdio.h>
#include <sys/resource.h>
#include <sys/wait.h>
#include <unistd.h>

int main(int argc, char** argv) {
    if (argc < 2) { fprintf(stderr, "usage: maxrss <command> [args...]\n"); return 2; }
    pid_t pid = fork();
    if (pid == 0) { execvp(argv[1], argv + 1); _exit(127); }
    int status;
    struct rusage usage;
    if (wait4(pid, &status, 0, &usage) < 0) return 1;
    fprintf(stderr, "%ld\n", usage.ru_maxrss);
    return WIFEXITED(status) ? WEXITSTATUS(status) : 1;
}
