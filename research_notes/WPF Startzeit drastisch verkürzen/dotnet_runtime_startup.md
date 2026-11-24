# .NET runtime-level startup levers for a .NET 8 WPF app (framework-dependent, x64 + ARM64), state of November 2026

Reading notes for the report writer:

- Every bullet under "Cited Findings" was read on the linked page in this session unless it is tagged **[snippet only]** (seen only in a search-result summary, page not opened) — treat those as weaker.
- Statements from my own background knowledge that I could not source in this session are confined to "Inferences" or "Gaps" and are labelled as such.
- Overall honest magnitude: no runtime switch gives anything near 100x. Measured ranges found: ReadyToRun on a trivial WPF app is roughly 0-25% (inside run-to-run noise); an experimental Native AOT fork of WPF shows about 4.8x (652 ms to 136 ms); System.Text.Json source generation saves about 9-16 ms per first call. The only places where larger factors are even reported in the sources are environmental, not runtime knobs: x64 emulation on ARM (qualitative report: "a couple seconds" vs "immediately") and antivirus cloud scanning of freshly compiled assemblies (community reports from one specific tool; not verified for ordinary build output). Debugger overhead could not be quantified from any source.

## 1. ReadyToRun (PublishReadyToRun), Composite R2R, and R2R of the framework itself

### Takeaway
R2R is the one supported "flip a switch" lever for JIT time, but it only precompiles the assemblies you ship: in a framework-dependent app that is app code plus NuGet dependencies, because the shared framework is already R2R-compiled. The only WPF measurement found (empty window, .NET 8) shows 0.50-0.68 s without and 0.49-0.57 s with R2R, i.e. no difference outside noise for a small app; Microsoft's own docs say small apps "will likely not experience a significant improvement". Composite R2R is self-contained only and therefore not available to this app's deployment model.

### Cited Findings
- Switch: `<PublishReadyToRun>true</PublishReadyToRun>` plus a RID at publish (`dotnet publish -c Release -r win-x64 -p:PublishReadyToRun=true`). It only takes effect on publish, not on a normal build/F5 — [Microsoft Learn, ReadyToRun deployment overview (page updated 2026-11-10)](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Mechanism: R2R binaries contain both IL and precompiled native code; they cut "the amount of work the just-in-time (JIT) compiler needs to do as your application loads" — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Size cost: "the size of an assembly will grow to between two to three times larger", which "may reduce the performance of loading the assembly from disk, and increase working set" — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Framework already precompiled: "Applications that have small amounts of code will likely not experience a significant improvement from enabling ReadyToRun, as the .NET runtime libraries have already been precompiled with ReadyToRun." Conversely "most applications that have large amounts of code receive large performance benefits" (no number given) — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Which assemblies get compiled: "The SDK will precompile the assemblies that are distributed with the application. For self-contained applications, this set of assemblies will include the framework. C++/CLI binaries are not eligible for ReadyToRun compilation." Exclusions via `<PublishReadyToRunExclude Include="X.dll" />` — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- R2R does not eliminate JIT. Not precompiled: generic types defined in separate assemblies, native interop stubs, some hardware intrinsics, unusual IL, and dynamic methods via reflection or LINQ — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Tiering interaction: "Ahead-of-time generated code is not as highly optimized as code produced by the JIT... tiered compilation will replace commonly used ReadyToRun methods with JIT-generated methods." — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Composite: `<PublishReadyToRunComposite>true</PublishReadyToRunComposite>` compiles a set of assemblies together so more methods can be precompiled; costs: "Compilation takes significantly longer", "The application is significantly larger", "Only self-contained deployments are supported"; "`PublishReadyToRunComposite` is ignored if the deployment is not self-contained". Supported from .NET 6; on by default for self-contained single-file R2R apps in .NET 7+ — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Cross-compiling: an x64 Windows SDK can produce R2R images for Windows x86, x64 and Arm64 — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Runtime kill switch (diagnostic, not a speed-up): env var `DOTNET_ReadyToRun=0` "forces the runtime to JIT-compile framework code"; default is on — [Microsoft Learn, Compilation config settings (ms.date 2026-09-25)](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
- Measured, empty-window WPF app, .NET 8, Windows 11 24H2 x64 VM with 2 cores / 8 GB, time from process start to window shown: plain .NET 8 = 0.68 s and 0.50 s; .NET 8 + ReadyToRun = 0.52 s, 0.49 s, 0.57 s; .NET Framework 4.8.1 = 0.32 s, 0.31 s; WinUI 3 + Native AOT = 0.09 s, 0.18 s; native C++ = about 0 s. Author's own caveat: few runs, one shared VM, "not very precise" — [Zenn, suusanex, 2025-05-23](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en)
- Scale of JIT work in a real desktop app: paint.net 5.0.3 on .NET 8 preview 4 JIT-compiled 19,816 methods at Tier 0 on x64 (20,305 on arm64) between `Main()` start and "Ready", and only 14 (11) at Tier 1 — [GitHub gist, Kunal Pathak, 2023-06](https://gist.github.com/kunalspathak/da3685ed70c2b1df12317c454759834d)
- DevExpress lists R2R as a startup tip for .NET (Core) WPF and says it can be combined with Multicore JIT; its results are only in chart images, no numbers in text — [DevExpress blog, 2023-01-26](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)

### Inferences
- For this app (framework-dependent), R2R only helps the JIT time of the app's own assemblies and its NuGet dependencies. The expected gain scales with how many app/third-party methods run before first paint; for a small app the Zenn data suggests tens of milliseconds at most.
- "R2R the framework itself" requires self-contained publish; the framework that ships in the shared runtime is already R2R, so going self-contained purely for that brings nothing new except the option of Composite.
- Because R2R needs `dotnet publish` with a RID, it does nothing for the F5 / `dotnet run` inner loop unless the dev build is deliberately published.
- The .NET Framework 4.8.1 number (about 0.31 s vs about 0.5-0.7 s) shows the gap that NGen'd, OS-preloaded framework images give .NET Framework; NGen itself is .NET Framework only and not available on .NET 8 (DevExpress: ".NET/.NET Core projects don't support Ngen").

### Gaps
- No credible before/after measurement of R2R on a large, real WPF app on .NET 8/9/10 was found (DevExpress numbers are images only; no Rick Brewster post on R2R/crossgen was found — his posts found are from 2008-2012 and concern NGen/Multicore JIT on .NET Framework).
- Not confirmed from a primary source in this session: that every `Microsoft.WindowsDesktop.App` (WPF) assembly ships R2R. The Learn text says "the .NET runtime libraries have already been precompiled"; WPF's C++/CLI parts (DirectWriteForwarder, System.Printing — from background knowledge) cannot be R2R per the "C++/CLI binaries are not eligible" rule.
- No measured size delta for a whole WPF app publish folder with/without R2R.

## 2. Tiered compilation settings (TieredCompilation, QuickJit, QuickJitForLoops, OSR, TieredPGO, R2R interaction)

### Takeaway
The defaults (tiered compilation on, quick JIT on) are already the startup-optimised configuration; turning tiered compilation or quick JIT off makes startup worse, not better. The only knobs that could plausibly shave startup are `TieredPGO=false` (removes Tier-0 instrumentation) and `TieredCompilationQuickJitForLoops=true` (its current default is unverified, see Gaps), but no measured WPF numbers were found for either.

### Cited Findings
- Tiered compilation: first tier "generates code more quickly (quick JIT) or loads pre-compiled code (ReadyToRun)", second tier "generates optimized code in the background". Enabled by default since .NET Core 3.0. Knobs: runtimeconfig `System.Runtime.TieredCompilation`, MSBuild `<TieredCompilation>`, env `DOTNET_TieredCompilation` — [Microsoft Learn, Compilation config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
- Quick JIT: "Enabling quick JIT decreases startup time but can produce code with degraded performance characteristics." Default on since .NET Core 3.0. Knobs: `System.Runtime.TieredCompilation.QuickJit`, `<TieredCompilationQuickJit>`, `DOTNET_TC_QuickJit` — [Microsoft Learn, Compilation config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
- "If quick JIT is disabled but tiered compilation is enabled, only pre-compiled code participates in tiered compilation. If a method is not pre-compiled with ReadyToRun, the JIT behavior is the same as if tiered compilation were disabled." (i.e. full optimising JIT on first call) — [Microsoft Learn, Compilation config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
- Quick JIT for loops: "Enabling quick JIT for loops may improve startup performance. However, long-running loops can get stuck in less-optimized code for long periods." Knobs: `System.Runtime.TieredCompilation.QuickJitForLoops`, `<TieredCompilationQuickJitForLoops>`, `DOTNET_TC_QuickJitForLoops`. The page states "If you omit this setting, quick JIT is not used for methods that contain loops" — [Microsoft Learn, Compilation config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation) (see Gaps: this default statement conflicts with my recollection of .NET 7+ behaviour)
- Dynamic PGO: env `DOTNET_TieredPGO`, MSBuild `<TieredPGO>`; it "works hand-in-hand with tiered compilation to further optimize code based on additional instrumentation that's put in place during tier 0" — [Microsoft Learn, Compilation config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
- .NET 8: "dynamic profile-guided optimization (PGO) has been improved and is now enabled by default... On average, dynamic PGO increases performance by about 15%. In a benchmark suite of ~4600 tests, 23% saw performance improvements of 20% or more." (steady-state throughput, not startup) — [Microsoft Learn, What's new in .NET 8 runtime](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime)
- Promotion policy (design doc, written for 2.1 and possibly outdated): a method must be called 30 times; a 100 ms timer is reset by any Tier-0 JIT activity and call counting only starts once it expires without new Tier-0 jitting; Tier-1 compiles run on a background thread. Rationale: "both the call counter and background threads compiling Tier1 code versions were slowing down the foreground threads trying to complete startup" — [dotnet/runtime, tiered-compilation.md](https://github.com/dotnet/runtime/blob/main/docs/design/features/tiered-compilation.md)
- For R2R methods "the precompiled code is the tier0 version"; hot R2R methods are later replaced by jitted Tier-1 code — [dotnet/runtime, tiered-compilation.md](https://github.com/dotnet/runtime/blob/main/docs/design/features/tiered-compilation.md)
- Real-app confirmation that startup is almost entirely Tier 0: paint.net 5.0.3 startup on .NET 8 preview = about 19.8k Tier-0 JITs vs 14 Tier-1 JITs (x64) — [GitHub gist, Kunal Pathak](https://gist.github.com/kunalspathak/da3685ed70c2b1df12317c454759834d)

### Inferences
- `TieredCompilation=false` or `TieredCompilationQuickJit=false` are anti-levers for startup: every non-precompiled method would be compiled with the slow optimising JIT on first call. They are sometimes recommended for steady-state latency, not for launch time.
- With tiered compilation off, R2R code is never re-jitted, so `TieredCompilation=false` + full R2R trades JIT-free startup for permanently lower-quality code; only sensible if nearly everything on the startup path is precompiled (which a framework-dependent WPF app cannot guarantee).
- `<TieredPGO>false</TieredPGO>` is the only default that can cost startup (Tier-0 instrumentation overhead); the size of the effect on WPF first-window time is unmeasured in the sources found.

### Gaps
- No source found with before/after startup numbers for any tiering knob on a WPF app.
- Conflict to resolve: the Learn page fetched says quick JIT for loops is off when the setting is omitted; from background knowledge, .NET 7 enabled On-Stack Replacement together with quick JIT for loops by default on x64 and Arm64. I did not open a primary source for the .NET 7+ default or for OSR in this session, so OSR is not covered by cited findings.
- No source found for hidden knobs such as `DOTNET_TC_CallCountThreshold` or `DOTNET_TC_BackgroundWorkerTimeoutMs`; the design doc fetched does not name them.

## 3. Multicore JIT / startup profiles (ProfileOptimization.SetProfileRoot / StartProfile)

### Takeaway
The API is still present and documented for .NET 8, 9, 10 and 11, costs two lines of code, and helps only from the second launch on a multicore machine. The only measured numbers are from .NET Framework 4.5 in 2012 (16-35% on three WPF apps); nothing measured on modern .NET was found, so treat the gain as unproven but cheap to try.

### Cited Findings
- `System.Runtime.ProfileOptimization` is listed for .NET Core 3.0/3.1 and .NET 5 through 11 (and .NET Framework 4.5+). It "improves the startup performance... by performing background compilation of methods that are likely to be executed, based on profiles created during previous compilations" — [Microsoft Learn, ProfileOptimization class](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization?view=net-10.0)
- Usage and limits: call `SetProfileRoot(folder)` (folder must already exist) then `StartProfile(fileName)`; "Profile optimization requires a multicore computer. The methods are ignored on other computers."; "There is no performance benefit the first time a profile is created."; the profile is overwritten on each run; a corrupt/unwritable profile is silently ignored — [Microsoft Learn, ProfileOptimization class](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization?view=net-10.0)
- Current docs explicitly position it as a startup feature: "This API reduces startup time. These improvements are complementary to dynamic profile-guided optimization (Dynamic PGO)" — [Microsoft Learn, ProfileOptimization class](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization?view=net-10.0)
- Measured (.NET Framework 4.5 only, October 2012): three desktop WPF apps improved "from 16% to 35%"; Bing.com ASP.NET warm-up went from "around 155 seconds" to "just under 80 seconds"; "Improvements of 20%-50% are very typical". The Paint.NET test in that post removed native images first — [.NET Blog, 2012-10-18](https://devblogs.microsoft.com/dotnet/an-easy-solution-for-improving-app-launch-performance/)
- DevExpress (2023) states Multicore JIT works for both .NET Framework 4.5+ and .NET (Core), is less effective than NGen, and can be combined with R2R; numbers only in images — [DevExpress blog, 2023-01-26](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)
- Evidence it is still live in .NET 8: the paint.net 5.0.3 startup analysis on .NET 8 preview shows MulticoreJit in call stacks — [GitHub gist, Kunal Pathak](https://gist.github.com/kunalspathak/da3685ed70c2b1df12317c454759834d)

### Inferences
- Multicore JIT is the natural complement to R2R in a framework-dependent app: R2R covers app assemblies, while the profile replays whatever still needs JIT (cross-assembly generics, stubs, lambdas) on a background thread.
- The 2012 percentages were measured against un-NGen'd code with a non-tiered JIT; on .NET 8 Tier-0 JIT is much cheaper per method, so the relative gain is probably lower than 16-35% (inference, unmeasured).
- It does not help the very first launch after install/build, and a freshly rebuilt dev binary may invalidate or mismatch the profile, so it is a shipping lever more than an F5 lever.

### Gaps
- No measurement of ProfileOptimization on .NET 8/9/10 (WPF or otherwise) was found.
- Not verified: how the profile behaves across app updates, and whether it interacts badly with TieredPGO instrumentation.

## 4. Native AOT for WPF

### Takeaway
Official status in November 2026: WPF is not supported under Native AOT or trimming in .NET 8, 9 or 10; the tracking issue has been open since 2020 and sits on the "Future" milestone. A community fork based on WPF release/10.0 does run under Native AOT and reports about 4.8x faster startup (652 ms to 136 ms), but with drag-drop, IME, spell check, printing and accessibility disabled and a restricted binding model — an experiment, not a shippable switch.

### Cited Findings
- Native AOT limitations (page dated 2026-11-03): "No dynamic loading", "No runtime code generation, for example, System.Reflection.Emit", "No C++/CLI", "Windows: No built-in COM", "Requires trimming, which has limitations". Switch is `<PublishAot>true</PublishAot>`; Windows x64 and Arm64 supported in .NET 8, x86 added in .NET 9+ — [Microsoft Learn, Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- Trimming status: "The Windows Presentation Foundation (WPF) framework makes substantial use of reflection... Unfortunately, almost no WPF apps are runnable after trimming, so trimming support for WPF is currently disabled in the .NET SDK." C++/CLI "currently doesn't support trimming"; built-in COM marshalling is trim-incompatible (alternative: COM wrappers) — [Microsoft Learn, Known trimming incompatibilities (updated 2025-12-03)](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)
- Tracking issue "WPF is not trim-compatible" opened 2020-11-13 (287 trim warnings at the time), moved to the "Future" milestone 2021-08-27; issue #11205 "Make Wpf Native aot compatible" was closed as a duplicate of it on 2025-11-10; a member commented on 2025-09-01 that much work could be done but "the integration is the real blocker" (team resources, CI) — [dotnet/wpf issue #3811](https://github.com/dotnet/wpf/issues/3811)
- Community proof of concept: `lezzi/wpfaot` (fork of `ibebbs/wpfaot`) using the WPF fork `ibebbs/wpf` branch `feature/native-aot`, based on dotnet/wpf `release/10.0`, .NET 10 SDK preview. Median startup: framework-dependent 652 ms, self-contained 728 ms, AOT + trimmed 136 ms. Working set: 82.4 MB / 82.8 MB / 56.1 MB. Deployed size: 24 MB (202 files) / 118 MB (413 files) / 63 MB (6 files, about 55 MB exe). Machine: Intel i7-14700K, Windows 10. Self-reported — [GitHub, lezzi/wpfaot README](https://github.com/lezzi/wpfaot)
- What that fork had to change: AOT-safe XAML runtime replacing the Reflection.Emit one; BAML/XAML assembly resolution restricted to loaded assemblies; COM paths (drag-drop, TSF, UI Automation focus) gated off; DirectWrite font loaders ported from C++/CLI to C# with `[GeneratedComInterface]`; feature switches disabling spell check, printing, drag-drop, TSF/IME, accessibility; a `{CompiledBinding}` extension with source generator. Limitations: standard `{Binding}` only works for view-model types listed in `TrimmerRoots.xml`; `{CompiledBinding}` lacks dotted paths, MultiBinding, RelativeSource, ElementName, StringFormat; JumpList/ActiveX crash; no dynamic assembly loading — [GitHub, lezzi/wpfaot README](https://github.com/lezzi/wpfaot)
- Non-WPF reference point on the same class of machine as the WPF test in section 1: WinUI 3 + Native AOT empty window at 0.09-0.18 s vs WPF on .NET 8 at 0.50-0.68 s — [Zenn, suusanex, 2025-05-23](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en)
- .NET 10 WPF itself "migrat[ed] font collection loader to managed code" (one of the C++/CLI pieces) — [Microsoft Learn, What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)

### Inferences
- Three independent blockers, all sourced above: (1) reflection-heavy XAML/BAML loading and bindings (trimming), (2) built-in COM interop, (3) C++/CLI assemblies. All three must be solved inside WPF, not in app code.
- The fork's 136 ms is the best evidence of the theoretical ceiling for WPF startup: roughly 5x over JIT on the same machine, not 100x.
- Since Native AOT is unavailable, the realistic AOT-like path for a supported WPF app is R2R (+ Multicore JIT).

### Gaps
- The upstream `ibebbs/wpfaot` repo was not opened directly; numbers are from the `lezzi` fork's README, with no visible last-updated date. No independent reproduction found.
- No statement found from Microsoft committing Native AOT/trimming support for WPF to .NET 11 or later.

## 5. Trimming / self-contained / single-file for WPF

### Takeaway
None of these is a startup lever for WPF. Trimming is disabled for WPF in the SDK; self-contained was measured slower than framework-dependent in the one data point found (728 ms vs 652 ms); single-file adds either first-run extraction (native libraries) or decompression cost.

### Cited Findings
- Trimming for WPF "is currently disabled in the .NET SDK" — [Microsoft Learn, Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)
- Community workaround: the undocumented `_SuppressWpfTrimError` property re-enables trimming (the warning became an error in the .NET 6 SDK); a user reported in November 2025 a partially trimmed (`TrimMode` partial) self-contained single-file build of about 38 MB vs about 140 MB untrimmed; other commenters note partial trim is not enough for Native AOT and apps may crash — [dotnet/wpf issue #3811](https://github.com/dotnet/wpf/issues/3811)
- Self-contained vs framework-dependent, same WPF app on .NET 10: 728 ms vs 652 ms median startup; 118 MB/413 files vs 24 MB/202 files — [GitHub, lezzi/wpfaot README](https://github.com/lezzi/wpfaot)
- Single-file (`<PublishSingleFile>true</PublishSingleFile>`, works framework-dependent or self-contained, always RID-specific): "Only managed DLLs are bundled with the app into a single executable. When the app starts, the managed DLLs are extracted and loaded in memory, avoiding the extraction to a folder"; native binaries stay separate unless `IncludeNativeLibrariesForSelfExtract=true`, in which case "the files are extracted to disk before the app starts", on Windows under `%TEMP%/.net` — [Microsoft Learn, Single-file deployment (updated 2026-10-15)](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- `EnableCompressionInSingleFile=true`: "Compression comes with a performance cost. On application start, the assemblies must be decompressed into memory, which takes some time." — [Microsoft Learn, Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- `IncludeAllContentForSelfExtract` (extract everything to disk first) "is not recommended: it's a .NET Core 3.1 compatibility mode" — [Microsoft Learn, Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- Single-file API breaks: `Assembly.Location` returns an empty string, `Assembly.GetFile(s)` throws; "Managed C++ components aren't well suited for single file deployment" — [Microsoft Learn, Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- Composite R2R (the one startup-relevant extra that self-contained unlocks) is "enabled by default for self-contained single-file deployments that use ReadyToRun" in .NET 7+ — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)

### Inferences
- For a framework-dependent WPF app, keep the plain folder deployment: it avoids extraction, decompression and the larger I/O footprint of self-contained.
- The self-contained penalty (about 12% in the one data point) is plausibly because the machine-wide shared framework files are more likely to already be in the OS file cache than a private 118 MB copy (inference).

### Gaps
- No measurement found of single-file (with or without compression) startup for a WPF app.
- No measurement found of self-contained + Composite R2R for WPF; in principle the strongest supported precompilation setup, but it contradicts the framework-dependent constraint.
- From background knowledge (not sourced here): a self-contained WPF app carries native DLLs (wpfgfx, PresentationNative, D3DCompiler, PenImc), so a truly single .exe needs `IncludeNativeLibrariesForSelfExtract` and therefore disk extraction on first run.

## 6. System.Text.Json cold-start cost and the source generator

### Takeaway
The first reflection-based serializer call costs roughly 25-28 ms and about 1.1-1.5 MB of allocations for even a one-property type; the source generator cuts that to about 13-16 ms (37% less for Deserialize, 55% less for Serialize). That is a real but small absolute saving per startup (about 9 ms for a first Deserialize, about 16 ms for a first Serialize, plus more per additional type), not a game changer.

### Cited Findings
- Microsoft's measurement (minimal console app, type `JsonMessage` with one string property, pre-release .NET 6, hardware not stated), first call: Serialize 28.25 ms / 1110 KB with reflection vs 12.75 ms / 563 KB source-generated; Deserialize 24.75 ms / 1457 KB vs 15.50 ms / 1025 KB — [.NET Blog, "Try the new System.Text.Json source generator", 2021-07-22](https://devblogs.microsoft.com/dotnet/try-the-new-system-text-json-source-generator/)
- Why the first call is slow: metadata for each type is computed on first use; this "is based on reflection, and is computationally expensive both in terms of time and allocations"; member accessors are built with `Reflection.Emit` ("Generating these IL methods takes a non-trivial amount of time, but also consumes private memory"); the result is cached, so only the first call per type pays — [.NET Blog, 2021-07-22](https://devblogs.microsoft.com/dotnet/try-the-new-system-text-json-source-generator/)
- Steady-state side effect of the generator in the same post: single POCO serialize 243.1 ns to 149.3 ns (1.62x); after trimming, System.Text.Json.dll 989 KB to 460 KB — [.NET Blog, 2021-07-22](https://devblogs.microsoft.com/dotnet/try-the-new-system-text-json-source-generator/)
- .NET 8 brought the source generator close to parity with reflection (`required`/`init` properties, `JsonSourceGenerationOptionsAttribute` parity with `JsonSerializerOptions`, `TypeInfoResolverChain`) — [Microsoft Learn, What's new in .NET 8 runtime](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime)
- **[snippet only]** Microsoft's docs state "test results have shown up to 40% or more startup time reduction" for source generation — [Microsoft Learn, Reflection versus source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/reflection-vs-source-generation)
- **[snippet only]** A whole-process AWS Lambda cold start comparison: 321 ms (reflection) vs 294 ms (source generator); this is total cold start, not serializer first-call time — [dev.to, LambdaSharp](https://dev.to/lambdasharp/benchmarking-net-json-serializers-on-aws-lambda-279m)

### Inferences
- API shape of the lever: a `partial class X : JsonSerializerContext` with `[JsonSerializable(typeof(T))]` per root type, then `JsonSerializer.Deserialize(json, X.Default.T)`.
- The reflection cost scales with the number and complexity of types touched at startup, so for a settings file plus a changelog with nested types the saving could be a few tens of milliseconds; still well below what a human notices unless stacked with other costs.
- The cheaper alternative if JSON is not needed before first paint: defer the first `JsonSerializer` call until after the window is shown, which removes 100% of this cost from first-window time.

### Gaps
- No measurement on .NET 8/9/10 found; the only first-party absolute numbers are from the .NET 6 preview. The cost under an attached debugger or under x64 emulation was not measured anywhere I found.

## 7. Debug builds and running under the Visual Studio debugger (F5, XAML Hot Reload, Just My Code, diagnostic tools)

### Takeaway
No credible measurement of F5 vs Ctrl+F5 or Debug vs Release startup for WPF was found. Sources only establish qualitatively that debugger-attached launches pay for symbol loading and that XAML Hot Reload is tied to the debugger; the size of the effect has to be measured locally (Release build, Ctrl+F5, launched outside the IDE).

### Cited Findings
- **[snippet only]** A Visual Studio team post lists "build and/or deploy time, symbol loading, debug heap being enabled, and function breakpoints" as causes of slow debug startup (2015, may be outdated) — [Microsoft DevBlogs, "Make Debugging Faster with Visual Studio", 2015-03-03](https://devblogs.microsoft.com/devops/make-debugging-faster-with-visual-studio/)
- **[snippet only]** XAML Hot Reload is supported "when you're running an application in Visual Studio with the debugger attached (F5 or Start debugging)", and the required environment variable is set automatically by Start Debugging — [Microsoft Learn, XAML Hot Reload](https://learn.microsoft.com/en-us/visualstudio/debugger/xaml-hot-reload); [Microsoft Learn, XAML Hot Reload troubleshooting](https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/ide/xaml-hot-reload-troubleshooting)
- R2R (section 1) is a publish-time feature (`dotnet publish -r <RID>`), so a normal Debug/Release build started from the IDE runs the app's own code fully JIT-compiled — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)

### Inferences
- Startup timings taken under F5 should not be used as the baseline; measure a Release build started without the debugger before judging any other lever.

### Gaps
- No source with numbers for: Debug vs Release configuration, debugger attached vs not, XAML Hot Reload on vs off, Diagnostic Tools window on vs off.
- Not verified how .NET 8+ treats R2R framework images when a debugger is attached. Unsourced hypothesis worth a local test (background knowledge only): with a debugger attached and "Suppress JIT optimization on module load" active, the runtime may refuse precompiled (R2R) code for affected modules and JIT them unoptimised, which would inflate startup far beyond symbol-loading cost. Quick check: toggle that option and "Enable Just My Code" and compare.

## 8. Windows Defender / antivirus real-time scanning of freshly built assemblies

### Takeaway
Plausible but unverified for a normal WPF build output; measure locally. The largest factors in any source come from one community thread about LINQPad's built-in antivirus test: on Windows 11 23H2 several users measured 300-800 ms (outliers to seconds) to load one freshly compiled assembly with Defender's cloud-delivered protection on, versus 20-40 ms with it off and under 1 ms with a working process exclusion or Dev Drive — but one user in the same thread measured under 1 ms for a plain .NET 8 console app. For installed, unchanged binaries the measured effect is small (a JetBrains IDE started in 6.5 s with Defender vs 6.0 s without).

### Cited Findings
- LINQPad forum thread (Dec 2023 - Nov 2024, community reports, LINQPad author Joe Albahari participating), time to load a newly compiled assembly: 400-700 ms with Defender on (even with the process excluded) vs 20-40 ms with cloud-delivered protection off (user mking); 400-800 ms vs 80-100 ms with cloud protection off vs about 30 ms with memory integrity also off (GlenHandke, Win 11 23H2 build 22631.2861); over 300 ms with cloud-delivered protection on vs about 20 ms off (AlexJeffreys, also reproduced in a Windows dev VM); over 400 ms default vs about 150 ms without automatic sample submission vs 20-30 ms with Smart App Control off vs about 0.3 ms with a process exclusion for the host exe (Marfig) — [LINQPad forum, "Antivirus Performance Test"](https://forum.linqpad.net/discussion/comment/7928)
- Counter-data point in the same thread: on a managed Windows 10 machine the LINQPad test showed about 300 ms online / about 30 ms offline / under 1 ms with AV exclusion, while a "plain .NET 8 console app" measured under 1 ms (user rs38, Jan 2024). The slow numbers are therefore specific to LINQPad's test scenario (dynamically compiled assemblies) and not shown to apply to ordinary compiled apps — [LINQPad forum, "Antivirus Performance Test"](https://forum.linqpad.net/discussion/comment/7928)
- Same thread, fixes that worked for some: process exclusion (not file exclusion) for the executable; turning off cloud-delivered protection / automatic sample submission; turning off Smart App Control; Dev Drive ("<1 ms" with all features on, user gclaugus on build 22631.4249). Not universal: one user saw no effect from exclusions or Dev Drive, and exclusions appeared broken on some 23H2 builds until later Windows updates — [LINQPad forum, "Antivirus Performance Test"](https://forum.linqpad.net/discussion/comment/7928)
- Dev Drive test (unscientific, stopwatch): Rider start to welcome screen about 6.5 s on NTFS with Defender real-time, about 6.5 s on Dev Drive with performance mode, about 6.0 s on NTFS with Defender disabled; clean rebuild of a 22-project solution 32.41 s on NTFS vs 19.8 s on Dev Drive; `dotnet restore` 846 ms vs 434 ms. The author could not separate the ReFS effect from Defender performance mode and measured no application startup — [Maarten Balliauw, 2023-11-22](https://blog.maartenballiauw.be/posts/2023-11-22-test-driving-windows-11-dev-drive-for-dotnet)
- **[snippet only]** A file exclusion stops scanning of that file only; a process exclusion also skips files the process opens; exclusions apply to real-time scanning — [Microsoft Learn, Defender Antivirus exclusions overview](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-exclusions-overview)
- **[snippet only]** On a trusted Dev Drive, real-time protection runs in an asynchronous "performance mode", enabled by default for new Dev Drives — [Microsoft Learn, Defender performance mode](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-endpoint-antivirus-performance-mode)
- **[snippet only]** Microsoft's claim for Dev Drive + performance mode: "up to 30% better performance for overall build times" — [Windows Developer Blog](https://blogs.windows.com/windowsdeveloper/?p=56804)

### Inferences
- Extrapolation, not measured: a WPF app whose own assemblies were rebuilt seconds ago presents never-seen DLLs to Defender, which could trigger the same cloud lookups as in the LINQPad reports. Whether it does for ordinary build output is unverified (the rs38 console-app data point argues against it), so the per-DLL figures must not be multiplied out for a WPF app. Local test: time the first launch after a rebuild against the second launch of the same binaries, with and without an exclusion.
- The cost applies to new/changed files; the shared framework DLLs are long known to Defender, so end users of an installed, signed build see far less of this than the developer does.
- Order of fixes by evidence: process exclusion for the app exe and/or build output folder exclusion; Dev Drive for the repo and output; check Smart App Control state. All are security trade-offs and are system settings the user must change themselves.

### Gaps
- No controlled, first-party measurement of Defender's impact on .NET or WPF application startup was found; the strongest data is a community forum thread.
- No data for Defender behaviour on Windows 11 ARM64 inside a VM, or when build output lives on a host-shared/network folder (if the repo is on the macOS side of a Parallels share, scanning and file I/O characteristics would differ — unverified).

## 9. Free startup improvements from retargeting to .NET 9 / .NET 10

### Takeaway
No source found quantifies a WPF or desktop startup gain from moving .NET 8 to 9 or 10. Microsoft's WPF release notes list .NET 10 performance work (XAML parsing, font collection loader moved to managed code, dynamic resources) without any numbers, and .NET 9's WPF work was the Fluent theme, not performance. "Just retarget" is therefore a hygiene step, not a demonstrated startup lever.

### Cited Findings
- WPF in .NET 10 (released November 2025): internal data structures replaced with base .NET types to minimise allocations in UI automation and file dialogs; "optimizing cache operations, array handling, and migrating font collection loader to managed code"; "Performance was optimized across font rendering, dynamic resources, input composition, trace logging, regex usage, and XAML parsing." No figures given — [Microsoft Learn, What's new in WPF for .NET 10 (2026-02-10)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- WPF in .NET 9: Fluent theme (`ThemeMode`), accent colour support, hyphen-based ligatures, BinaryFormatter removal; no performance items listed. `ThemeMode` default is `None` (Aero2); Fluent is opt-in and loads `PresentationFramework.Fluent` resources — [Microsoft Learn, What's new in WPF for .NET 9](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)
- One absolute .NET 10 data point (different app and machine from the .NET 8 one, so not comparable): framework-dependent WPF sample at 652 ms median on an i7-14700K — [GitHub, lezzi/wpfaot README](https://github.com/lezzi/wpfaot); versus an empty .NET 8 WPF window at 0.50-0.68 s on a 2-core VM — [Zenn, suusanex](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en)
- .NET 11 appears in the docs (Learn API docs default to the `net-11.0` moniker; single-file docs reference behaviour "Starting in .NET 11"); its GA status and date were not verified in this session — [Microsoft Learn, ProfileOptimization class](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization?view=net-10.0); [Microsoft Learn, Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

### Inferences
- Opting into the .NET 9+ Fluent theme adds theme resource loading at startup, so retargeting plus `ThemeMode` could make first-window time worse, not better (inference; unmeasured).
- Local observation from the repo (not a web source): the solution already mixes `net10.0-windows` (UploadManager.App) and `net8.0-windows` (Conduit, LogoLoader, RunPanel), so the .NET 10 runtime is evidently available on the dev machine and retargeting the remaining projects is a one-line change per project.

### Gaps
- Stephen Toub's "Performance Improvements in .NET 9 / .NET 10" posts were not opened (very long; search returned only secondary SEO blogs with unverifiable figures such as "42% reduction"). No startup-specific numbers from them are included.
- Support lifecycle not verified in this session: from background knowledge, .NET 8 (LTS) and .NET 9 both reach end of support in November 2026 and .NET 10 is the current LTS; this would be the stronger argument for retargeting and should be confirmed against the official support policy page.

## 10. Windows on ARM (e.g. Windows 11 ARM in Parallels on Apple Silicon): x64 emulation vs native arm64

### Takeaway
A WPF app launched through an x64 apphost runs entirely under x64 emulation on Windows ARM, and the one practitioner report describes the difference as "a couple seconds" to the first screen emulated versus "immediately" native. If (and only if) the app's process on the ARM VM turns out to be x64, switching to a native arm64 process (`RuntimeIdentifier`/`PlatformTarget` arm64 and an arm64 apphost) is likely one of the highest-leverage fixes; if it is already arm64, there is nothing to gain here. No precise startup timings were found.

### Cited Findings
- Launcher determines architecture: the .exe apphost is native, "An AnyCPU build on an x64 machine produces an x64 launcher; on an Arm64 machine, an Arm64 launcher." An x64 launcher runs on Arm64 through emulation; an Arm64 launcher hard-fails on x64. `dotnet app.dll` runs natively on either. The author believed his AnyCPU WPF app (Markdown Monster) ran natively until he displayed the process architecture — [Rick Strahl, 2025-04-18](https://weblog.west-wind.com/posts/2025/Apr/18/The-Strong-ARM-of-NET-Wrestling-with-x64-and-Arm64-Desktop-App-Deployment)
- Reported effect on the same WPF app and ARM laptop: emulated x64 takes "a couple seconds" to reach the logo screen and feels sluggish; native Arm64 shows the logo screen immediately with responsiveness comparable to an i9 x64 laptop. No stopwatch numbers. His fix: build an arm64 launcher (`PlatformTarget` arm64) and swap it in at install time — [Rick Strahl, 2025-04-18](https://weblog.west-wind.com/posts/2025/Apr/18/The-Strong-ARM-of-NET-Wrestling-with-x64-and-Arm64-Desktop-App-Deployment)
- How emulation works: it is "just-in-time compiling blocks of x86 instructions into Arm64 instructions"; "A service caches these translated blocks of code... The caches are produced for each module". Windows 11 24H2 introduced the faster Prism emulator, but "Prism is optimized and tuned specifically for Qualcomm Snapdragon processors. Some performance features within Prism require hardware features only available in the Snapdragon X series" — [Microsoft Learn, How emulation works on Arm (2025-11-06)](https://learn.microsoft.com/en-us/windows/arm/apps-on-arm-x86-emulation)
- Even native arm64 startup is somewhat slower than x64 for the JIT-heavy part: in paint.net 5.0.3 on .NET 8 preview, individual methods took 10-34% longer to JIT on arm64 (Ampere) than x64 (Cascade Lake), attributed mainly to instruction-cache flushing (`FlushInstructionCache`), about 6% more kernel time, and slower register allocation — [GitHub gist, Kunal Pathak, 2023-06](https://gist.github.com/kunalspathak/da3685ed70c2b1df12317c454759834d)
- .NET Framework only (flagged, not applicable to .NET 8): AnyCPU .NET Framework apps "will run on an Arm64 device using x64 emulation" unless the Windows 24H2 `supportedArchitectures` manifest setting / "Prefer native ARM64" is used — [Visual Studio Blog, 2024-09-17](https://devblogs.microsoft.com/visualstudio/how-to-run-net-apps-natively-on-arm64-devices/)
- R2R images can be cross-compiled for Windows Arm64 from an x64 SDK, so an arm64 publish can still be R2R — [Microsoft Learn, ReadyToRun](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)

### Inferences
- Under emulation, a .NET app pays twice for dynamic code: the x64 CLR's JIT emits x64 machine code at runtime, which the OS emulator must then translate to Arm64. The emulator's per-module cache covers on-disk modules, so runtime-JIT'd code presumably cannot benefit from it on later launches (inference from the Learn description). JIT-heavy WPF startup is therefore a bad case for emulation.
- In a Parallels VM on Apple Silicon the CPU is not a Snapdragon, so by Microsoft's wording some Prism optimisations may not apply; the emulation penalty there may be larger than on Snapdragon X hardware (inference, unmeasured).
- Practical check: read `RuntimeInformation.ProcessArchitecture` (or Task Manager's Architecture column) for the running app. If it says X64 on the ARM VM, switching to an arm64 process is the first lever to pull, before any JIT knob.
- If the build is done inside the ARM VM with an arm64 SDK, AnyCPU should already yield an arm64 apphost (per Strahl's description); an x64 process would then indicate an explicit `win-x64` RID, `PlatformTarget` x64, or an x64 SDK/Visual Studio installation doing the build.

### Gaps
- No stopwatch comparison of emulated-x64 vs native-arm64 startup for a WPF app was found, neither on Snapdragon nor on Apple Silicon/Parallels.
- Not verified: that an emulated x64 .NET process additionally requires the x64 .NET desktop runtime to be installed on the ARM machine, and the exact MSBuild recipe for a framework-dependent arm64 publish (from background knowledge: `-r win-arm64 --self-contained false`).
- No data on Prism's behaviour with JIT-generated code specifically.
- The Learn R2R cross-compilation table lists Windows X64 and X86 as SDK host platforms but has no Windows Arm64 SDK row, so whether `PublishReadyToRun` works when publishing from an arm64 SDK inside the ARM VM is unverified (publishing R2R for win-arm64 from an x64 SDK is documented).
