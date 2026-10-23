#!/usr/bin/env bash
# Runs a csharpc-compiled WebAssembly module (--target wasm32-wasip1) under wasmtime the way the
# tests run native executables: environment variables inherited, the host file system preopened at
# "/" (csrt follows $PWD as the current directory), and room for .NET's 8 MiB main-thread stack.
#
#   tests/wasm-run.sh program.wasm [args...]          (WASMTIME=path/to/wasmtime overrides the engine)
#
# An unhandled exception traps (abort), which wasmtime reports with exit status 134 like SIGABRT.
PWD="$(pwd -P)"; export PWD
exec "${WASMTIME:-wasmtime}" run -S inherit-env=y --dir=/::/ -W max-wasm-stack=8388608 "$@"
