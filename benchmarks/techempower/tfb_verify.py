#!/usr/bin/env python3
"""TechEmpower's own verification of a running server (benchmarks/techempower/harness.py).

    tfb_verify.py --tfb DIR --test json [--test db ...] --url http://127.0.0.1:8080 [--database-host 127.0.0.1]

Runs the verify() of TechEmpower's test types (DIR/toolset/test_types/<type>/<type>.py, from a
FrameworkBenchmarks checkout at the commit harness.py pins) unchanged: headers (Server, Date,
Content-Type, Content-Length or Transfer-Encoding, a Date that changes), bodies, the queries
parameter cases (2, 0, foo, 501, empty), fortune HTML (escaping, sorting, the added fortune) and,
for the database types, the number of queries and rows PostgreSQL's pg_stat_statements counts while
siege sends 512 concurrent requests. The test's URLs come from the "aot" test of TechEmpower's
aspnetcore benchmark_config.json. Runs with is_ci off, i.e. the rules of a real TechEmpower run (no
margin on the query count, failed transactions fail). Prints every result and exits 1 when one is
a "fail". Needs python3-requests, python3-colorama, python3-psycopg2 and siege.
"""
import argparse
import importlib.util
import json
import os
import sys
import types


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tfb", required=True)
    ap.add_argument("--test", action="append", required=True)
    ap.add_argument("--url", required=True)
    ap.add_argument("--database-host", default="127.0.0.1")
    args = ap.parse_args()

    tfb = os.path.abspath(args.tfb)
    sys.path.insert(0, tfb)
    # toolset/databases/__init__.py discovers databases under /FrameworkBenchmarks only (TechEmpower's
    # container layout); register PostgreSQL in the same dict verifications.py imports.
    import toolset.databases as databases  # noqa: E402
    databases.databases["postgres"] = load(os.path.join(tfb, "toolset/databases/postgres/postgres.py"), "postgres_db").Database

    config = types.SimpleNamespace(
        concurrency_levels=[16, 32, 64, 128, 256, 512],
        pipeline_concurrency_levels=[256, 1024, 4096, 16384],
        query_levels=[1, 5, 10, 15, 20],
        cached_query_levels=[1, 10, 20, 50, 100],
        database_host=args.database_host,
        db_root=os.path.join(tfb, "toolset", "databases"),
        is_ci=False,
        duration=15,
        server_host="127.0.0.1",
    )
    entry = json.load(open(os.path.join(tfb, "frameworks/CSharp/aspnetcore/benchmark_config.json")))["tests"][0]["aot"]

    failed = False
    for test in args.test:
        folder = os.path.join(tfb, "toolset", "test_types", test)
        test_type = load(os.path.join(folder, f"{test}.py"), f"tfb_{test}").TestType(config).parse(entry)
        results = test_type.verify(args.url.rstrip("/"))
        for result, reason, url in results:
            print(f"{test}: {result.upper()} {url} {reason}".rstrip())
            failed |= result == "fail"
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
