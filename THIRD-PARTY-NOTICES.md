# Third-party notices

csharpc includes source code derived from the following projects. Their license terms apply to
those parts of csharpc.

## .NET Runtime (dotnet/runtime)

- **What:** the regular-expression engine in `src/Csharpc/corelib/Regex/` is a port of
  `src/libraries/System.Text.RegularExpressions/src` (parser, tree optimizer, find optimizations,
  case-equivalence tables, writer, interpreter and the public `Regex` / `Match` / `Group` /
  `Capture` API), adapted to the csharpc corelib. Each ported file keeps the upstream license
  header.
- **Source:** <https://github.com/dotnet/runtime>, release/11.0, commit
  `bc123d0bd173a538addc07e0f8fa38a90f0925c1`.
- **License:** MIT

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
