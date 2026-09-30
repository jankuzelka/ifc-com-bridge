# Known issues

This page lists deployment-level problems. Behaviour of the library that is kept on purpose, quirks Q1–Q19, is in [quirks.md](quirks.md).

## KI-1: binding redirects in the host's configuration can break the library

**Status:** open. It must be fixed as a change of its own, not as part of an unrelated refactoring, because any fix changes runtime behaviour.

### Cause

The library embeds its managed dependencies with Costura ([library-build.md](library-build.md)). An in-process COM server runs under the configuration of the host process, `<host>.exe.config`, including its binding redirects. When the library needs a dependency, these steps follow:

1. The CLR applies the host's redirect.
2. It looks for the redirected version on disk: first the GAC, then the host's folder.
3. Only if nothing is found does it ask Costura, which returns its embedded copy whatever version was requested.

### Consequences

- **Redirect to a version other than the embedded one, not found on disk: the host process crashes.** The CLR applies the redirect again to the copy Costura returns and asks again. The recursion ends in a stack overflow or an access violation, which terminates the host.
  - Seen with Microsoft.Extensions.Options: redirects to 3.1.3.0 or 8.0.0.0 crash, while a redirect to 7.0.0.1 (the embedded version) works.
  - It happens while the first `ComRuntime` object is created (logger setup), so no call works.
- **Redirect to a version that is neither on disk nor embedded: every call fails** with `FileNotFoundException` or `TypeInitializationException`. For example, System.ValueTuple redirected to 4.0.3.0 works without the DLL in the host folder only because the library embeds exactly 4.0.3.0.
- **Not affected:**
  - hosts without redirects for these assemblies, including native COM hosts;
  - hosts that ship the redirected version in their own folder, because it is found on disk before Costura is asked.

In principle, every embedded assembly the library loads is exposed:
- Microsoft.Extensions.Logging and Microsoft.Extensions.Logging.Abstractions 3.1.3.0;
- Microsoft.Extensions.Options 7.0.0.1;
- Newtonsoft.Json 13.0.0.0;
- Serilog 2.0.0.0, Serilog.Extensions.Logging 2.0.0.0 and Serilog.Sinks.Console 4.1.0.0;
- the Xbim assemblies;
- System.ValueTuple 4.0.3.0, when a host redirects to it.

The behaviour was verified for Options and ValueTuple. Embedded assemblies that no COM call loads are not affected (verified).

### Current mitigation

The embedded versions of the dependencies the library loads are fixed on purpose. Two of them are pinned explicitly: Microsoft.Extensions.Options 7.0.1 (assembly 7.0.0.1) and System.ValueTuple 4.5.0 (assembly 4.0.3.0). These dependency versions are pinned because host binding redirects interact with Costura assembly resolution; changing them may alter runtime behaviour in COM hosts. The host configurations described above, those that work and those that fail, are part of a tested host-redirect matrix, and their behaviour is intentionally preserved. The matrix runs in a COM-host simulation, a test tool that is not part of this repository; see [library-build.md](library-build.md).

### Workaround for an affected host

In the host's `.exe.config`, either remove the redirect for the affected assembly, or change its target to the version the library embeds. Alternatively, ship the redirected version next to the host executable.

### Fixing it later

Candidate approaches have to be designed and evaluated separately. Examples:
- resolve dependencies from the library's own folder instead of from embedded resources;
- change how the embedded assemblies are resolved or loaded;
- isolate the library from the host's binding configuration.

Whatever is chosen, turn the host-redirect matrix into a regression test first: host configurations with and without redirects, and with and without the redirected DLL in the host folder.
