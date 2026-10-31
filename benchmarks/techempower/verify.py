#!/usr/bin/env python3
"""Normalized transcript of a running server's answers to TechEmpower's requests
(benchmarks/techempower/harness.py), for diffing two builds of the same app.

    verify.py HOST:PORT [--test plaintext --test json ...] > transcript.txt

TechEmpower's own rules are checked by tfb_verify.py; this script makes csharpc's answers comparable
byte for byte with NativeAOT's and the JIT's: every test type's requests (plaintext single, keep-alive
and 16 pipelined on one connection; json; db; the queries parameter cases 1, 2, 0, foo, 501, 20 and
empty for query, update and cached-query; fortunes; an unknown path), header names lower-cased and
sorted, the Date value replaced by its format and, for the database types, every number replaced by
N (the rows are random) and Content-Length by its presence. Fortunes are deterministic and kept
exact. Exits 1 when a basic rule fails (status 200, Content-Type, Content-Length or chunked, Server,
Date): the transcript is meaningless then.
"""
import argparse
import email.utils
import re
import socket
import sys

ACCEPT = {
    "plaintext": "text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7",
    "json": "application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7",
    "html": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
}
URLS = {"plaintext": "/plaintext", "json": "/json", "db": "/db", "query": "/queries/", "fortune": "/fortunes",
        "update": "/updates/", "cached-query": "/cached-worlds/"}
CONTENT_TYPES = {"plaintext": "text/plain", "fortune": "text/html"}
QUERY_CASES = ["1", "2", "0", "foo", "501", "20", ""]
RANDOM = {"db", "query", "update", "cached-query"}


def read_response(f):
    status = f.readline().decode("latin-1").rstrip("\r\n")
    headers = []
    while True:
        line = f.readline().decode("latin-1").rstrip("\r\n")
        if not line:
            break
        name, _, value = line.partition(":")
        headers.append((name.strip().lower(), value.strip()))
    h = dict(headers)
    if "content-length" in h:
        body = f.read(int(h["content-length"]))
    elif h.get("transfer-encoding", "").lower() == "chunked":
        body = b""
        while True:
            size = int(f.readline().split(b";")[0], 16)
            if size == 0:
                f.readline()
                break
            body += f.read(size)
            f.readline()
    else:
        body = b""
    return status, headers, body


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("target")
    ap.add_argument("--test", action="append")
    args = ap.parse_args()
    tests = args.test or ["plaintext", "json"]
    host, _, port = args.target.rpartition(":")
    errors, out = [], []

    def request(path, accept):
        return f"GET {path} HTTP/1.1\r\nHost: {args.target}\r\nAccept: {accept}\r\nConnection: keep-alive\r\n\r\n".encode()

    def record(name, test, status, headers, body):
        h = dict(headers)
        if status != "HTTP/1.1 200 OK":
            errors.append(f"{name}: status '{status}'")
        expected_type = CONTENT_TYPES.get(test, "application/json")
        if not h.get("content-type", "").startswith(expected_type):
            errors.append(f"{name}: Content-Type '{h.get('content-type')}', expected {expected_type}")
        if "content-length" not in h and h.get("transfer-encoding", "").lower() != "chunked":
            errors.append(f"{name}: neither Content-Length nor chunked encoding")
        if not h.get("server"):
            errors.append(f"{name}: no Server header")
        if not h.get("date") or email.utils.parsedate_to_datetime(h["date"]) is None:
            errors.append(f"{name}: missing or malformed Date header '{h.get('date')}'")
        out.append(f"== {name}")
        out.append(status)
        for k, v in sorted(headers):
            if k == "date":
                v = "<RFC 1123 date>"
            elif k == "content-length" and test in RANDOM:
                v = "<length>"
            out.append(f"{k}: {v}")
        text = body.decode("utf-8", "replace")
        out.append(re.sub(r"\d+", "N", text) if test in RANDOM else text)

    with socket.create_connection((host, int(port)), timeout=30) as s:
        f = s.makefile("rb")
        for test in tests:
            accept = ACCEPT["html" if test == "fortune" else "plaintext" if test == "plaintext" else "json"]
            paths = [URLS[test] + q for q in QUERY_CASES] if URLS[test].endswith("/") else [URLS[test]]
            for path in paths:
                s.sendall(request(path, accept))
                record(f"{test} {path}", test, *read_response(f))
            if test == "plaintext":
                # Keep-alive, then the 16 pipelined requests per write of TechEmpower's plaintext load.
                s.sendall(request("/plaintext", accept))
                record("plaintext keep-alive", test, *read_response(f))
                s.sendall(request("/plaintext", accept) * 16)
                responses = [read_response(f) for _ in range(16)]
                for i, r in enumerate(responses):
                    if r[0] != "HTTP/1.1 200 OK" or r[2] != b"Hello, World!":
                        errors.append(f"pipelined plaintext #{i}: {r[0]} {r[2]!r}")
                out.append(f"== 16 pipelined plaintext requests: {sum(1 for r in responses if r[2] == b'Hello, World!')} answered")
    with socket.create_connection((host, int(port)), timeout=30) as s:
        f = s.makefile("rb")
        s.sendall(f"GET /unknown HTTP/1.1\r\nHost: {args.target}\r\n\r\n".encode())
        status, headers, body = read_response(f)
        out.append("== unknown path")
        out.append(status)
        out.append(body.decode("utf-8", "replace"))

    print("\n".join(out))
    for e in errors:
        print(f"verify: {e}", file=sys.stderr)
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
