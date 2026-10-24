#!/usr/bin/env python3
"""TechEmpower test requests against a running server (benchmarks/techempower/run.sh).

    verify.py HOST:PORT > transcript.txt

Sends the plaintext and JSON test types' requests (single, keep-alive, and 16 pipelined plaintext
requests on one connection, as the plaintext test's load does) and checks TechEmpower's rules
(https://github.com/TechEmpower/FrameworkBenchmarks/wiki/Project-Information-Framework-Tests-Overview):
status 200, the Content-Type, a Content-Length (or chunked body), the Server and Date headers, and
the exact bodies. Prints a normalized transcript (header names lower-cased and sorted, the Date value
replaced by its format) so the transcripts of two builds can be diffed; exits 1 when a rule fails.
"""
import email.utils
import json
import socket
import sys

HEADERS = "Accept: text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7\r\nConnection: keep-alive\r\n"


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


def request(host, path):
    return f"GET {path} HTTP/1.1\r\nHost: {host}\r\n{HEADERS}\r\n".encode()


def main():
    target = sys.argv[1]
    host, _, port = target.rpartition(":")
    errors, out = [], []

    def check(name, status, headers, body, content_type, expected):
        h = dict(headers)
        if status != "HTTP/1.1 200 OK":
            errors.append(f"{name}: status '{status}'")
        if not h.get("content-type", "").startswith(content_type):
            errors.append(f"{name}: Content-Type '{h.get('content-type')}', expected {content_type}")
        if "content-length" not in h and h.get("transfer-encoding", "").lower() != "chunked":
            errors.append(f"{name}: neither Content-Length nor chunked encoding")
        if not h.get("server"):
            errors.append(f"{name}: no Server header")
        if not h.get("date") or email.utils.parsedate_to_datetime(h["date"]) is None:
            errors.append(f"{name}: missing or malformed Date header '{h.get('date')}'")
        if expected(body) is False:
            errors.append(f"{name}: body {body!r}")
        out.append(f"== {name}")
        out.append(status)
        for k, v in sorted(headers):
            out.append(f"{k}: {'<RFC 1123 date>' if k == 'date' else v}")
        out.append(body.decode("utf-8", "replace"))

    plaintext = lambda b: b == b"Hello, World!"
    json_ok = lambda b: json.loads(b) == {"message": "Hello, World!"} and len(b) <= 28

    with socket.create_connection((host, int(port)), timeout=10) as s:
        f = s.makefile("rb")
        # One connection, keep-alive: plaintext, JSON, plaintext again.
        for name, path, ct, ok in [("plaintext", "/plaintext", "text/plain", plaintext),
                                   ("json", "/json", "application/json", json_ok),
                                   ("plaintext keep-alive", "/plaintext", "text/plain", plaintext)]:
            s.sendall(request(target, path))
            check(name, *read_response(f), ct, ok)
        # The plaintext test pipelines 16 requests per write.
        s.sendall(request(target, "/plaintext") * 16)
        responses = [read_response(f) for _ in range(16)]
        for i, r in enumerate(responses):
            if r[0] != "HTTP/1.1 200 OK" or r[2] != b"Hello, World!":
                errors.append(f"pipelined plaintext #{i}: {r[0]} {r[2]!r}")
        out.append(f"== 16 pipelined plaintext requests: {sum(1 for r in responses if r[2] == b'Hello, World!')} answered")
    with socket.create_connection((host, int(port)), timeout=10) as s:
        f = s.makefile("rb")
        s.sendall(f"GET /unknown HTTP/1.1\r\nHost: {target}\r\n\r\n".encode())
        status, headers, _ = read_response(f)
        out.append("== unknown path")
        out.append(status)

    print("\n".join(out))
    for e in errors:
        print(f"verify: {e}", file=sys.stderr)
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
