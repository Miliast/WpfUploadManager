# Diagnostic tools for finding where WPF startup / window-open time goes (.NET 8/9/10, state Nov 2026)

Reading guide: items under "Cited Findings" were read on the linked page during this research (Nov 2026), except those tagged **[snippet-only]**, which rest on a search-result summary of the linked page and were not opened. Everything under "Inferences" is either synthesis or recalled practitioner knowledge that was NOT re-verified against a source in this session; such items are tagged **[unverified]**. Check both kinds of tagged items before they go into a final report as fact. The same tags are used in the verdict table.

Quick verdict table (details and sources in the sections below):

| Tool | Points at | Works for WPF on .NET 8+? | Cost |
| --- | --- | --- | --- |
| PerfView 4.0.0 + WPF ETW provider GUID | JIT, loader, CPU stacks incl. native render thread, WPF parse/layout/render events, file I/O | Yes (verified by a .NET 8 practitioner write-up) | Free download from GitHub (licence text not read) |
| dotnet-trace / dotnet-counters | Managed stacks, JIT/loader events, runtime counters from process start | Yes for runtime data (.NET 5+ per docs); native frames and kernel events are documented as not captured; WPF's own ETW events presumably not either **[unverified]** | Free |
| VS Performance Profiler (CPU Usage, Instrumentation, File I/O, Events viewer, .NET Counters) | Managed hot paths, blocked time, file I/O | Yes per docs support table | Part of Visual Studio |
| VS Application Timeline | Parse / Layout / Render per element | Docs say "yes (XAML)" and "WPF (.NET 4.0 and above)"; no explicit .NET 5+ statement found; treat as unconfirmed | Part of Visual Studio |
| WPF Performance Suite (Perforator, Visual Profiler) | Software rendering, dirty rects, IRTs, per-element layout cost | Documented only for .NET Framework 4 / Windows SDK 7.1; status on .NET 8 not confirmed | Free (legacy) |
| WPR / WPA | Whole-machine CPU, disk, file, minifilter (antivirus), GPU | Expected yes because it is OS-level ETW **[unverified]**; no source specific to WPF or .NET 8 found | Free with Windows / ADK **[unverified]** |
| dotTrace Timeline | UI freezes, GC, file I/O on a timeline | Yes per vendor page (desktop apps, .NET) | Paid, via dotUltimate **[snippet-only]**; official price and trial length not retrieved |
| Snoop 6.0.0 | Visual tree, properties, triggers, diagnostics view | Yes, tested with .NET 6-10; not for self-contained single-file | Free, MS-PL |

## PerfView: which providers/keywords for WPF startup, how to read JIT Stats and the WPF events, recipe for one window open

### Takeaway
PerfView is the one free tool that shows JIT, assembly loading, CPU stacks and WPF's own parse/layout/render events in a single trace for a .NET 8 WPF app, but the WPF provider must be enabled by GUID (`E13B77A8-14B6-11DE-8069-001B212B5009`), because the name `Microsoft-Windows-WPF` is not registered by the modern .NET installer. JIT cost is read from the JITStats report (foreground + multicore-JIT time is the startup-relevant sum).

### Cited Findings
- The WPF ETW provider is declared as name `Microsoft-Windows-WPF`, guid `{E13B77A8-14B6-11DE-8069-001B212B5009}`, symbol `MICROSOFT_WINDOWS_WPF_PROVIDER` — [dotnet/wpf wpf-etw.man](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man)
- Keyword masks in that manifest: `KeywordGeneral 0x1`, `KeywordPerf 0x2`, `KeywordText 0x4`, `KeywordInput 0x8`, `KeywordAnnotation 0x10`, `KeywordXamlBaml 0x20`, `KeywordXPS 0x40`, `KeywordAnimation 0x80`, `KeywordLayout 0x100`, `KeywordHosting 0x400`, `KeywordHeapMeter 0x800`, `KeywordGraphics 0x1000`, `KeywordDispatcher 0x2000` (`KeywordPackaging 0x200` is commented out) — [dotnet/wpf wpf-etw.man](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man)
- Event/task names in the manifest that matter for startup and window open: app lifecycle `WClientAppCtor`, `WClientAppRun`; parsing `WClientParseBamlBegin/End`, `WClientParseXamlBegin/End`, `WClientParseXmlBegin/End`, `WClientParseXamlBamlInfo`; resources `WClientResourceFindBegin/End`, `WClientResourceCacheMiss`, `WClientResourceBamlAssembly`; layout `WClientLayoutBegin/End`, `WClientMeasureBegin/End`, `WClientArrangeBegin/End`, `WClientMeasureElementBegin/End`, `WClientArrangeElementBegin/End`; visuals `WClientCreateVisual`, `WClientPropParentCheck`; fonts `WClientFontCache`; rendering tasks `WClientUceRender`, `WClientUcePresent`, `WClientRenderHandler`, `WClientMediaRender`, `WClientOnRender`; intermediate render targets `WClientCreateIRT`, `WClientPotentialIRTResource`; dispatcher `WClientUIContextDispatch`, `WClientUIContextIdle`. No image-decode event (`WClientDecode*`) was found in the part of the manifest that was read (first 100,000 of 150,205 characters) — [dotnet/wpf wpf-etw.man](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man)
- "The WPF ETW provider's display name is 'Microsoft-Windows-WPF' but using that does not work"; the GUID must be used. The .NET Framework installer puts `wpf-etw.man` in `C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF`; the modern .NET installer neither ships the manifest nor registers the provider, so `logman query providers` does not list it. The manifest can be taken from the WPF source at `src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man` (Ian Griffiths, endjin, published 12 Jan 2024, updated 29 Mar 2026, app targeted .NET 8.0) — [endjin: Analyzing WPF performance by enabling ETW](https://endjin.com/blog/wpf-performance-enable-etw)
- Exact strings for PerfView's "Additional Providers" box: all WPF events `E13B77A8-14B6-11DE-8069-001B212B5009:*:*`; performance-keyword events only `E13B77A8-14B6-11DE-8069-001B212B5009:2:*`. First field is the keyword bitfield (values can be OR-ed, e.g. 6 = perf + text), second field is the level — [endjin: enabling ETW](https://endjin.com/blog/wpf-performance-enable-etw)
- Collection steps used there: PerfView > Collect > Run; enter full path of the WPF exe and an output file name; expand Advanced Options; uncheck the other checkboxes if only WPF events are wanted; put the provider string into Additional Providers; Run Command; exit the app; then type `WPF` into the Filter box of the Events view. Events show up as `Microsoft-Windows-WPF/WClientLayout/Start` and `/Stop` — [endjin: enabling ETW](https://endjin.com/blog/wpf-performance-enable-etw)
- Practitioner walk-through of one slow view (tab switch) in a .NET 8 WPF app: custom `SetTab.Start` / `SetTab.Stop` events bracket the interaction; in PerfView's Events view select the Time cells of the start and stop event and press Alt+R to set the time range; filter event types with "WPF", Ctrl+A, Enter; raise `MaxRet` above the default 10,000 rows. `WClientPropParentCheck` and `WClientResourceFind` bursts mark template loading, `WClientMeasure` marks the layout phase, `WClientUceRender` gives render time — [endjin: Analysing WPF Performance Using ETW and PerfView](https://endjin.com/blog/2024/01/analysing-wpf-performance-using-etw-and-perfview)
- Typical finding from that trace: layout started about 70 ms after template loading began (template loading alone used 70% of a 100 ms budget); root causes were a large visual tree with mostly off-screen elements and overpainting. Reducing element count cut template-load and layout time; hiding fully obscured elements left template/layout time unchanged but lowered `WClientUceRender` time — [endjin: Analysing WPF Performance](https://endjin.com/blog/2024/01/analysing-wpf-performance-using-etw-and-perfview)
- Method detail from the same post: the author clicked the slow tabs once to get JIT out of the way, waited about 10 s, clicked again and analysed only the second round (so the trace shows steady-state UI cost, not first-open cost); the app was run in debug mode — [endjin: Analysing WPF Performance](https://endjin.com/blog/2024/01/analysing-wpf-performance-using-etw-and-perfview)
- JITStats: "This data is mostly useful for optimizing startup"; each process gets a summary table of JIT time broken down by trigger (Foreground, Multicore JIT Background, Tiered Compilation Background); the per-method "Trigger" column shows "TC" for Tier-1 recompiles; "If there is a large amount of time spent in the sum of Foreground and Multicore JIT Background compilations then this can cause the application to start slowly"; "Most tiered compilation JIT activity should not occur right at startup, but rather very shortly afterwards" — [PerfView HtmlReportUsersGuide.htm](https://raw.githubusercontent.com/microsoft/perfview/main/src/PerfView/SupportFiles/HtmlReportUsersGuide.htm)
- Remedies named in the PerfView guide for high JIT time: precompile with Crossgen/ReadyToRun (which "does not handle compiling generic code over value types", so JIT count drops but not to zero); background/multicore JIT via `SetProfileRoot` + `StartProfile` (pushes "about 1/2 the JIT time" off the foreground thread); keep tiered compilation on; restructure so less code runs on the startup path. Code served from ReadyToRun images has no JIT accounting in the report. A "CPU (with Optimization Tiers) Stacks" view in the Advanced Group annotates frames with their tier — [PerfView HtmlReportUsersGuide.htm](https://raw.githubusercontent.com/microsoft/perfview/main/src/PerfView/SupportFiles/HtmlReportUsersGuide.htm)
- Current release: "PerfView and TraceEvent 4.0.0", marked Latest, dated 14 Oct (the page showed no year; 3.2.8 is dated 28 Sep, 3.2.6 19 Aug). 4.0.0 requires Windows 10 or later and drops 32-bit PerfView; older systems need a 3.x build — [microsoft/perfview releases](https://github.com/microsoft/perfview/releases)
- Truncated `dotnet-trace` output can be opened in PerfView with the `/ContinueOnError` command-line flag — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)

### Inferences
- Suggested combined mask for a startup trace, derived from the manifest keyword values: General|Perf|Text|XamlBaml|Layout|Graphics|Dispatcher = `0x3127`, i.e. `E13B77A8-14B6-11DE-8069-001B212B5009:0x3127:5`. Which keyword each individual event is assigned to was not verified, so `:*:*` is the safe choice for a first trace (startup is short; volume is manageable).
- Recipe for one window open (synthesis of the cited steps): (1) add an `EventSource` with `OpenWindow.Start/Stop` markers around `new Window()` ... `ContentRendered` (see self-instrumentation section); (2) PerfView > Collect > Collect (or Run), keep Kernel Base + CPU Samples + .NET checked, tick Thread Time, Additional Providers = `E13B77A8-14B6-11DE-8069-001B212B5009:*:*,*MyCompany-MyApp-Startup`; (3) open the window once, stop; (4) Events view: find Start/Stop markers, Alt+R on the two Time cells; (5) with that range open Advanced Group > JITStats (JIT ms and method count inside the range), CPU Stacks filtered to the UI thread, and the WPF events (sum of ParseBaml, Layout/Measure/Arrange, UceRender spans); (6) repeat for the second open: the difference between first and second open is JIT + type loading + one-time resource/font/image work, the second-open cost is tree size/layout/render. **[unverified]** for the exact checkbox names and the `*Name` EventSource syntax, which come from recall of PerfView's UI, not from a page read in this session.
- **[unverified]** Command-line equivalent from recall: `PerfView.exe /AcceptEULA /NoGui /ThreadTime /BufferSizeMB:256 /Providers:E13B77A8-14B6-11DE-8069-001B212B5009:*:*,*MyCompany-MyApp-Startup /DataFile:startup.etl run "C:\path\MyApp.exe"`. Flag names should be checked against `PerfView /?`.
- **[unverified]** Other PerfView views that separate root causes: "Image Load Stacks" (which code path pulled in which DLL), "File I/O Stacks" / "Disk I/O Stacks" (slow I/O, antivirus filter in the stack), "Thread Time (with StartStop Activities)" (blocked vs CPU time on the UI thread). Names are from recall.
- The WPF render thread and DirectWrite/WIC work run in native code, so only ETW-based tools (PerfView, WPR, VS CPU Usage, dotTrace) can attribute time there; this follows from dotnet-trace's documented "Event callstacks include native frames: No".

### Gaps
- No source was found that gives a canonical "these exact PerfView checkboxes for WPF startup" recipe from Microsoft; the Vance Morrison tutorial videos were not retrieved.
- The menu location of JITStats ("Advanced Group") appeared only in a search-result summary of the PerfView user guide, not in the fetched text.
- Whether WPF emits any image-decode or font-load specific events beyond `WClientFontCache` could not be established (manifest read was truncated at 100,000 characters). Image decoding and font loading will therefore most reliably show up as CPU stacks (WIC / DirectWrite modules) rather than as named WPF events; that statement is itself unverified.
- The PerfView 4.0.0 release year is not shown on the page read; assumed 2026 from page order.

## dotnet-trace / dotnet-counters: do they work for WPF GUI apps, which profiles, how to view results

### Takeaway
Both tools can launch a .NET 5+ executable as a child process and record from runtime start, so they answer "is it JIT / assembly loading / managed code on the UI thread" without admin rights. Per the docs they capture no native frames and no kernel events, so they cannot show render-thread cost, software rendering, disk or antivirus activity; that they also miss WPF's own parse/layout/render ETW events is expected but **[unverified]**.

### Cited Findings
- `dotnet-trace collect` "launches a child process and traces it (.NET 5 or later)"; append `--` plus the command. Documented example: `dotnet-trace collect -- hello.exe arg1 arg2`. Install: `dotnet tool install --global dotnet-trace` (direct downloads for win-x64 / win-arm64 exist). Page applies to dotnet-trace 9.0.661903 and later; ms.date 2026-08-24, updated 2026-10-21 — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- With `--`, the tool traces the first .NET process that connects back; use a self-contained app or `dotnet exec <app.dll>`, not `dotnet run`. Child stdin/stdout are redirected unless `--show-child-io` is passed; stopping the trace with Enter/Ctrl+C also exits the child — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- Profiles in current versions: `dotnet-common` (GC, AssemblyLoader, Loader, JIT, Exceptions, Threading, JittedMethodILToNativeMap, Compilation; equals `--providers "Microsoft-Windows-DotNETRuntime:0x100003801D:4"`), `dotnet-sampled-thread-time` (samples all managed thread stacks to estimate wall-clock time), `gc-verbose`, `gc-collect`, `database`. With no `--profile/--providers/--clrevents`, `dotnet-common` + `dotnet-sampled-thread-time` are enabled by default — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- The older `cpu-sampling` profile was removed "because the name was misleading. It sampled all threads regardless of their CPU usage"; replacement is `--profile dotnet-sampled-thread-time,dotnet-common`; exact old behaviour: `--profile dotnet-sampled-thread-time --providers "Microsoft-Windows-DotNETRuntime:0x14C14FCCBD:4"` — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- Sample rate: the fetched page states the runtime default as "~1000 Hz, every 1 ms"; adjustable via env var `DOTNET_EventPipeThreadSamplingRate` (milliseconds) — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace); a search-result summary of the same page family quoted "~100 Hz", so the figure differs between page versions.
- Limits of `collect` per the docs' feature table: requires admin: No; capture native library and kernel events: No; event callstacks include native frames: No — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- `--clrevents` aliases relevant to startup: `loader` 0x8, `assemblyloader` 0x4, `jit` 0x10, `compilation` 0x1000000000, `type` 0x80000, `exception` 0x8000; level via `--clreventlevel verbose` — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- Custom providers: `--providers Name[:Flags[:Level[:KeyValueArgs]]]`, comma separated, added on top of the profile. A trace can be stopped automatically on an event: `--stopping-event-provider-name <p> --stopping-event-event-name <e>` — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- Viewing: `.nettrace` opens in Visual Studio or PerfView on Windows; `--format Speedscope` (or `dotnet-trace convert trace.nettrace --format Speedscope`) produces a file for https://www.speedscope.app; `Chromium` is the third format. Conversion is lossy but the original `.nettrace` is kept. Text summary without a GUI: `dotnet-trace report <tracefile> topN [-n <n>] [--inclusive]` — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- `dotnet-counters` also supports child launch: `dotnet-counters monitor --counters System.Runtime[dotnet.assembly.count] -- my-app.exe` and `dotnet-counters collect --format csv|json --refresh-interval <s> --counters System.Runtime -- <command>`; "often useful to collect metrics for the application's startup path". Install `dotnet tool install --global dotnet-counters` or one-shot `dnx dotnet-counters ...` from .NET SDK 10.0.100. Page applies to 9.0.652701+, updated 2026-11-04 — [Learn: dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
- Counter names differ by runtime: on .NET 9+ the `System.Runtime` Meter gives `dotnet.jit.compilation.time (s)`, `dotnet.jit.compiled_methods`, `dotnet.jit.compiled_il.size`, `dotnet.assembly.count`, `dotnet.gc.pause.time`, `dotnet.process.cpu.time`; "If the app uses .NET version 8 or lower" the tool falls back to EventCounters named `IL Bytes Jitted (B)`, `Number of Methods Jitted`, `Number of Assemblies Loaded`, `Exception Count`, `Working Set (MB)` etc. — [Learn: dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
- Shape of a finding (docs sample output, not a WPF app): `dotnet.jit.compilation.time (s) 1.317`, `dotnet.jit.compiled_methods 6,008`, `dotnet.assembly.count 115` — [Learn: dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
- An x86 target needs the x86 build of the tool; bitness mismatch gives `Win32Exception (299)` — [Learn: dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)

### Inferences
- Startup command for a WPF app (assembled from the documented syntax; not test-run here): `dotnet-trace collect --profile dotnet-common,dotnet-sampled-thread-time --providers MyCompany-MyApp-Startup -o startup.nettrace -- "C:\src\MyApp\bin\Release\net8.0-windows\MyApp.exe"`, then `dotnet-trace report startup.nettrace topN -n 30 --inclusive` and `dotnet-trace convert startup.nettrace --format Speedscope`. On tool versions older than the profile rename use `--profile cpu-sampling`.
- **[unverified]** WPF's `Microsoft-Windows-WPF` provider is a classic native ETW provider, not an `EventSource`, so it does not flow through EventPipe and `--providers Microsoft-Windows-WPF` yields nothing. This is consistent with the docs table (no native/kernel events) but was not confirmed from a WPF source.
- Quick JIT yes/no test: read `Number of Methods Jitted` / `dotnet.jit.compiled_methods` and JIT time after the window is up; a count in the thousands on a cold start that falls sharply after enabling ReadyToRun confirms JIT as a main contributor.
- `dotnet-sampled-thread-time` samples wall-clock stacks of all managed threads, so a UI thread blocked in a synchronous file/network call shows up with large inclusive time even though CPU is idle; that is the cheapest way to spot blocking I/O in startup code.

### Gaps
- No source explicitly confirms the `--` child launch for a WPF apphost exe (GUI subsystem); the docs only state ".NET 5 or later". It is expected to work but should be test-run.
- No WPF-specific worked example with dotnet-trace was found.

## Visual Studio Performance Profiler (2022 and current release): CPU Usage, .NET Object Allocation, Application Timeline, Live Visual Tree, XAML Binding Failures, Events view

### Takeaway
The profiler tools CPU Usage, Instrumentation, File I/O, .NET Object Allocation, .NET Counters, GPU Usage and Events viewer are documented as supported for ".NET" projects and run without the debugger via Alt+F2; that is the documented most-accurate path. Application Timeline is listed as ".NET: yes (XAML)" but its own page still says "WPF (.NET 4.0 and above)" with a 2018 date, so its availability for WPF on .NET 8+ is not confirmed by the docs.

### Cited Findings
- Support table (columns: .NET | C/C++ | UWP | ASP.NET/ASP.NET Core): CPU Usage yes/yes/yes/yes; .NET Object Allocation yes/no/yes/yes; Instrumentation yes/yes/yes/yes; .NET Counters "yes (.NET Core/5+ only)"; File I/O yes/yes/yes/yes; Events viewer yes/yes/yes/yes; GPU Usage yes/yes/yes/no; Application Timeline "yes (XAML)"/no/yes/no. Page ms.date 2025-09-09, updated 2026-10-08, monikers `vs-2022` and `visualstudio` — [Learn: Which performance tool should I use?](https://learn.microsoft.com/en-us/visualstudio/profiling/choose-performance-tool?view=vs-2022)
- Tool descriptions: Instrumentation "Shows exact call counts and call times ... you want to see blocked time, such as time spent waiting for locks" (extra overhead); File I/O "Shows your File I/O operations and how long they take" (use: "Investigate UI freeze"); GPU Usage "Check whether the performance of your app is CPU-bound or GPU-bound"; Application Timeline "Investigate UI performance in XAML apps, such as time spent rendering frames"; Events viewer "Shows HTTP requests, log messages, and exceptions"; PerfTips show elapsed time between debugger steps — [Learn: Which performance tool should I use?](https://learn.microsoft.com/en-us/visualstudio/profiling/choose-performance-tool?view=vs-2022)
- Application Timeline platforms as listed: "Universal Windows apps (on Windows 10 and later)", "Windows 8.1", "Windows Presentation Foundation (.NET 4.0 and above)", "Windows 7". Start: Debug > Performance Profiler > Application Timeline > Start; a UAC prompt for `VsEtwCollector.exe` may appear. Page ms.date 2018-11-01 — [Learn: Application Timeline](https://learn.microsoft.com/en-us/visualstudio/profiling/application-timeline?view=visualstudio)
- Application Timeline output: "UI thread utilization (%)" bar graph, "Visual throughput (FPS)" graph, and Timeline details with categories Parsing (expanding shows the dependency chain of all XAML files parsed), Layout (per-element cost of Measure/Arrange/ApplyTemplate and the visual trees that took part in the pass), Render, I/O, App Code, Xaml Other; tip to combine it with CPU Usage — [Learn: Application Timeline](https://learn.microsoft.com/en-us/visualstudio/profiling/application-timeline?view=visualstudio)
- Release vs debug: "Release build performance numbers in the Performance Profiler are the most precise and accurate"; steps: set configuration to Release, Debug > Performance Profiler (Alt+F2), select tools, Start, Stop Collection. .NET Object Allocation is only available in the Performance Profiler (non-debugger). Command-line collection is possible with `VSDiagnostics.exe` (ships with VS or the Remote Tools) — [Learn: Run profiling tools on release or debug builds](https://learn.microsoft.com/en-us/visualstudio/profiling/running-profiling-tools-with-or-without-the-debugger?view=visualstudio)
- In the current `visualstudio` moniker the launch page has tabs "Flexible" (tools that can be combined), "Standalone" (tools that must run alone) and, if Copilot is enabled, "Recommended"; "Show all tools" lists tools disabled for the session — [Learn: Run profiling tools on release or debug builds](https://learn.microsoft.com/en-us/visualstudio/profiling/running-profiling-tools-with-or-without-the-debugger?view=visualstudio)
- While debugging (F5) the Diagnostic Tools window shows events, process memory, CPU usage and .NET Counters; its Events tab lists diagnostic events with category prefixes and a filter — [Learn: Run profiling tools on release or debug builds](https://learn.microsoft.com/en-us/visualstudio/profiling/running-profiling-tools-with-or-without-the-debugger?view=visualstudio)
- XAML Binding Failures window: VS 2019 16.8+ and VS 2022; Debug > Windows > XAML Binding Failures, only during debugging; also a "Binding failures" counter button in the in-app toolbar and in Live Visual Tree. Columns: Data Context, Binding Path, Target, Target Type, Description, File/Line/Project; "Combine Duplicates" shows a Count column (item templates repeat the same failure). Supported: "WPF .NET 5.0 RC2+" yes, with navigate-to-source (requires XAML Hot Reload enabled); WPF .NET Framework yes without navigation. For .NET 5+ the WPF trace-level setting does not affect the list. Page ms.date 2025-11-26 — [Learn: XAML data binding diagnostics](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/xaml-data-binding-diagnostics?view=visualstudio)
- `.nettrace` files from dotnet-trace can be opened in Visual Studio — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)

### Inferences
- The docs moniker `visualstudio` sits next to `vs-2022` and is the default; it is read here as the current major release (Visual Studio 2026). **[unverified]** that mapping was not stated on the pages read.
- Practical check for Application Timeline on a .NET 8 WPF project: open Alt+F2, click "Show all tools" and see whether Application Timeline is offered or greyed out for the project. The documentation does not settle it.
- A high Count in "Combine Duplicates" for a binding failure inside an ItemTemplate is a concrete startup finding: each failed binding costs a reflection lookup and a trace write per item while a debugger is attached.
- For startup, CPU Usage + File I/O + Events viewer in one "Flexible" session, Release build, gives managed hot path plus file reads; Instrumentation (Standalone) gives exact wall-clock per method including blocked time, at higher overhead.

### Gaps
- No page read states whether Application Timeline works for WPF on .NET Core 3.1 / .NET 5+; the support table says "yes (XAML)" for .NET without qualification and the tool page lists only "WPF (.NET 4.0 and above)".
- No Visual Studio 2026 release notes were read; any 2026-specific profiler change beyond the "Recommended (Copilot)" tab is unknown.
- Live Visual Tree's own page was not read; whether it displays descendant counts per node (useful for tree-size checks) is **[unverified]** recall.
- The Events viewer's ability to show JIT/module-load/custom EventSource events for a WPF app was not verified; the table only describes it as showing HTTP requests, log messages and exceptions.

## WPF Performance Suite (Perforator, Visual Profiler): still downloadable, works with .NET 8, meaning of the Perforator readings, alternatives

### Takeaway
The only documentation found is archived .NET Framework 4-era material: the suite shipped inside the Windows Performance Toolkit of Windows SDK 7.1. No source read confirms that it is still shipped in current SDK/ADK builds or that it attaches to .NET 8 WPF processes, so it should be treated as a legacy .NET Framework tool whose readings can be approximated on modern .NET with WPF ETW events and `RenderCapability`.

### Cited Findings
- Install path as documented: Windows SDK (v7.1) > Tools > "Install Windows Performance Tool Kit", which installs Performance Analyzer, GPUView and "WPF Performance Suite"; needs an administrator account; "You should start the WPF Performance Suite before you run the application that you want to profile". Tools: Perforator ("Analyzes rendering behavior") and Visual Profiler ("Profiles the use of WPF services such as layout and event handling by elements in the visual tree"). Archived page, ms.date 2011-02-23 — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))
- Perforator graphs: Frame Rate ("For applications without animation, this value should be near 0"); Dirty Rect Addition Rate ("how many rectangular regions that WPF has to update for each frame"); SW IRTs Per Frame (software intermediate render targets; "On a computer that supports hardware acceleration, this number should be 0. Otherwise ... some of your scene is rendered by using the slower software pipeline"); HW IRTs Per Frame (caused by `DrawingBrush`, `VisualBrush`, `Opacity` on a Visual, tile modes on `TileBrush`; "If this number is high (for example, greater than 5) ... a large amount of work"); Video Memory Usage (exceeding texture memory "will usually cause WPF render logic to fall back to software"; multi-monitor multiplies the need) — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))
- Perforator options: "Draw software rendering with purple tint" (software render targets, software 3D, per-primitive software fallback); "Draw software rendered bitmap effects with red tint"; "Show dirty-region update overlay"; "Disable dirty region support"; "Clear back-buffer before rendering"; "Disable Opacity Effects"; "Disable per-primitive software fallback"; "Disable high-quality image rescaling" ("If checking this option significantly lowers your frame rate, consider decoding your images to a size near the size that they will be displayed"); "Disable 3D rendering" — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))
- "Typically, the time that it takes to render an area in software is proportional to the number of pixels rendered. Therefore, be wary of large areas rendered by using the software pipeline" — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))
- Visual Profiler: element tree labels like `Border 'border1' (26) 0.02% (I)/ 0.00 % (E) - .24 ms (I) / 0.00 ms (E)` = type, name, subtree size, inclusive/exclusive share and layout milliseconds; "hotpath" expansion; application CPU breakdown by Layout, Rendering Thread, Render (MediaContext), Tick (TimeManager), UpdateRealizations, Unlabeled Time — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))
- The modern WPF ETW manifest still defines intermediate-render-target and render events (`WClientCreateIRT`, `WClientPotentialIRTResource`, `WClientUceRender`, `WClientUcePresent`), which is the raw data behind several Perforator graphs — [dotnet/wpf wpf-etw.man](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man)
- A Microsoft blog post titled "Where to Download WPF Performance Suite? (Perforator, Visual Profiler)" (Kirill Osenkov, 2016) exists; its content was not read — [Learn archive: kirillosenkov](https://learn.microsoft.com/en-us/archive/blogs/kirillosenkov/where-to-download-wpf-performance-suite-perforator-visual-profiler)

### Inferences
- Alternatives on .NET 8+: (a) per-element layout cost and tree size: PerfView with the WPF provider (`WClientMeasureElement*` / `WClientArrangeElement*`), VS Application Timeline if it is offered, or Snoop for tree inspection; (b) software rendering: `RenderCapability.Tier` at runtime plus the A/B test of forcing `RenderMode.SoftwareOnly` (see built-in diagnostics section); (c) IRT/opacity/VisualBrush cost: `WClientCreateIRT` event counts in a PerfView trace, VS GPU Usage, WPR GPU profile.
- **[unverified]** Recall: the suite was last shipped with the Windows 8.x-era Windows Performance Toolkit and is absent from the Windows 10/11 ADK; Visual Profiler depends on in-process .NET Framework components and does not attach to .NET Core/5+ processes, while Perforator talks to the native render layer and may partly work. None of this was confirmed by a source in this session.

### Gaps
- No dotnet/wpf issue or Microsoft statement was found that says whether Perforator or Visual Profiler work with .NET Core 3.1 / .NET 5-10 WPF (one targeted search returned nothing relevant).
- Current download location (which SDK/ADK version, if any, still contains `WpfPerf.exe`) was not verified.

## Windows Performance Recorder / Analyzer (WPR/WPA) for WPF startup

### Takeaway
WPR has documented built-in profiles for CPU, disk I/O, file I/O, minifilter I/O and GPU activity, which makes it a candidate for causes outside the process such as antivirus filter drivers, disk contention or GPU/driver stalls; no WPF-specific WPR guidance was found, the only documentation read is a 2017 list of built-in profiles, and the commands below are from recall **[unverified]**.

### Cited Findings
- Built-in resource profiles: CPU usage, Disk I/O activity, File I/O activity, Registry I/O activity, Networking I/O activity, Heap usage, Pool usage, VirtualAlloc usage, Power usage, GPU activity, Handle usage, XAML activity ("Events from XAML-related providers"), HTML activity, Desktop composition activity, Resident Set analysis. Scenario profiles include Minifilter I/O activity and XAML application analysis. Profiles are selected in the WPR UI under More Options; custom profiles can be authored. Page ms.date 2017-05-05, updated 2022-04-21 — [Learn: Built-in Recording Profiles](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/built-in-recording-profiles)
- The WPF provider is enabled by GUID `E13B77A8-14B6-11DE-8069-001B212B5009`; the source demonstrates this only with PerfView — [endjin: enabling ETW](https://endjin.com/blog/wpf-performance-enable-etw)

### Inferences
- Because the WPF provider is a regular ETW provider addressed by GUID, it should be possible to add it to a WPR session through a custom `.wprp` profile; this was not shown in any source read.
- **[unverified]** Recalled command line (profile names from `wpr -profiles`, not from a page read here): `wpr -start GeneralProfile -start CPU -start FileIO -start DiskIO -start Minifilter -start DotNET -filemode`, start the app, then `wpr -stop C:\traces\startup.etl`; open in WPA and use "CPU Usage (Precise)" for wait/ready analysis of the UI thread, "File I/O" and "Minifilter" graphs for antivirus overhead, "Images" for DLL load order.
- **[unverified]** The "XAML activity" profiles target the UWP/WinUI XAML stack, not WPF; WPF events need a custom `.wprp` profile containing the WPF provider GUID.
- **[unverified]** `wpr.exe` ships in-box with Windows 10/11; WPA comes with the Windows ADK (Windows Performance Toolkit) or from the Microsoft Store.
- WPA resolves JIT-compiled .NET frames less conveniently than PerfView; for a .NET app PerfView is usually the first choice and WPA the second opinion for OS-level waits. This is a judgment, not a cited fact.

### Gaps
- No source read gives WPR command-line profile names, current WPT version, or a WPF startup walk-through in WPA.
- No source found on how well current WPA resolves managed stacks for .NET 8+ without extra steps.

## JetBrains dotTrace (timeline mode), WPF-specific views, pricing/trial

### Takeaway
dotTrace's Timeline mode is the vendor's recommended mode for UI freezes, GC, and file I/O and it is sold only inside dotUltimate / All Products Pack; the official price and trial length could not be retrieved, and no WPF-specific view was confirmed on the pages read.

### Cited Findings
- Modes: Timeline binds call data to a timeline and is positioned for "UI freezes, excessive garbage collection, uneven workload distribution, and inefficient file I/O"; Sampling is the quick overall mode; Tracing and Line-by-line are for examining a specific algorithm. Filters select time intervals by condition; a "SQL Queries" filter exists. Supports .NET Framework, .NET Core, .NET, desktop apps; integrates with Visual Studio and Rider; can attach to a running process; command-line profiling tools exist — [JetBrains: dotTrace features](https://www.jetbrains.com/profiler/features/)
- **[snippet-only]** Licensing: dotTrace is licensed only as part of dotUltimate or the All Products Pack — [JetBrains dotTrace product page](https://jetbrains.com/profiler)
- **[snippet-only]** Reseller price points (not official, pages not opened): ComponentSource lists "dotUltimate 2026.1 - New Subscriptions", 1 user commercial annual, first year, at $596.82, and notes a JetBrains price increase effective 1 Oct 2025 — [ComponentSource: dotUltimate prices](https://www.componentsource.com/product/dotultimate/prices?pf=new); Connection lists a commercial annual subscription at $340.53 — [Connection](https://www.connection.com/product/jetbrains-corp.-dotultimate-commercial-annual-subscription/c-s.dul-y-40c/41207224); a G2 listing showed $469/user/year (organizations) and $169/user/year (individuals), date unknown — [G2: dotTrace](https://www.g2.com/products/dottrace/reviews). These figures conflict with each other; none should be quoted as the 2026 price.

### Inferences
- **[unverified]** Recall: the Timeline viewer has interval filters for "UI Freeze", "JIT", "File I/O", "Blocking GC", "Lock contention" and a "Subsystems" breakdown that groups time into WPF, GC, JIT, File I/O, etc.; Timeline uses ETW and therefore needs elevation. None of these names appeared on the page read.
- If a team already owns dotUltimate (ReSharper bundle), dotTrace Timeline is the most guided "what froze the UI thread" tool; if not, PerfView covers the same data for free with more manual work.

### Gaps
- JetBrains' own pricing pages (`jetbrains.com/profiler/buy/`, `jetbrains.com/dotnet/buy/`) returned no usable content or 404; official 2026 prices, trial length and free non-commercial terms are unconfirmed.
- JetBrains help pages for profiling modes returned 404 at the URLs tried; no WPF-specific dotTrace documentation was read.
- Explicit .NET 8/9/10 support statement and current dotTrace version number not found.

## Snoop / WPF Inspector / XAML diagnostics for visual-tree size and binding errors

### Takeaway
Snoop is maintained and explicitly tested on .NET 6 through 10, but cannot attach to self-contained single-file apps; Visual Studio's XAML Binding Failures window is the documented way to list binding errors with counts for WPF on .NET 5+.

### Cited Findings
- Snoop supports .NET Framework 4.6.2+ and .NET 6+ ("Tested with 6, 7, 8, 9 and 10"); top version entry 6.0.0; distributed via Chocolatey and GitHub releases; license MS-PL. Features described: browse visual, logical and automation trees, change property values, view triggers, break on property changes, a "Diagnostics" view (since 4.0.0), ARM/ARM64 support (since 4.0.0). "Self-contained single-file applications are not supported"; trimmed single-file apps may not work — [snoopwpf/snoopwpf README](https://github.com/snoopwpf/snoopwpf)
- XAML Binding Failures window lists every failed binding with Data Context, Binding Path, Target, Description and a duplicate Count; available only while debugging; supports WPF .NET 5.0 RC2+ — [Learn: XAML data binding diagnostics](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/xaml-data-binding-diagnostics?view=visualstudio)
- Why tree size matters, from a real trace: a large visual tree with mostly off-screen elements dominated template-load and layout time; reducing element count reduced both — [endjin: Analysing WPF Performance](https://endjin.com/blog/2024/01/analysing-wpf-performance-using-etw-and-perfview)
- Visual Profiler (legacy) displays subtree size per element in its tree labels, e.g. `(26)` — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))

### Inferences
- A tool-independent element count (works in any build, no attach needed) is a recursive `VisualTreeHelper.GetChildrenCount` walk from the window, logged at `ContentRendered`. **[unverified]** as a recipe in this session, but uses only public API.
- **[unverified]** Recall: Snoop's tree shows a descendant count next to each node and has a filter for elements with binding errors; install via `choco install snoop`. Not shown on the README text that was read.

### Gaps
- "WPF Inspector" was not researched; no source on whether it works on .NET 8+ (recall: abandoned, .NET Framework only, unverified).
- Snoop 6.0.0 release date not shown on the page read.
- No source found for a programmatic binding-error hook on modern .NET beyond `PresentationTraceSources` (see next section).

## Built-in WPF diagnostics: PresentationTraceSources, RenderCapability.Tier, ProcessRenderMode, HwndTarget.RenderMode, software-rendering tint, checking hardware acceleration at runtime

### Takeaway
Hardware vs software rendering can be checked in-process with `RenderCapability.Tier` (the `>> 16` shift and "0 = software only" reading are **[snippet-only]**) and forced for A/B tests per process (`RenderOptions.ProcessRenderMode`), per window (`HwndTarget.RenderMode`) or machine-wide (registry `HKCU\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration`); the purple tint for software-rendered areas is documented only as a Perforator option, not as a standalone registry switch.

### Cited Findings
- Cases in which WPF renders in software (Microsoft WPF team blog, 2010): video driver older than 11/2004; Tier 0 graphics card; window larger than the card's max texture size (e.g. spanning monitors); running over Remote Desktop ("WPF renders the application content using a software rasterizer on the server and then remotes the content as bitmaps in all cases" as of .NET 3.5 SP1/4); virtual machines whose graphics emulation does not work correctly; app calls `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;` (per process, .NET 4) or `hwndTarget.RenderMode = RenderMode.SoftwareOnly;` (per window, 3.5 SP1); the registry value `[HKEY_CURRENT_USER\Software\Microsoft\Avalon.Graphics] "DisableHWAcceleration"` ("impacts all WPF application consider using this key for testing purposes only"); legacy bitmap effects; printing; `RenderTargetBitmap`; tiled `TileBrush` content; too many 3D lights; out-of-video-memory failures — [Learn archive: Software Rendering Usage in WPF](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/software-rendering-usage-in-wpf)
- Detection per the same post: Perforator's "Draw software rendering with purple tint" / "Draw software rendered bitmap effects with red tint"; in code "Check the RenderCapability.Tier. You can also listen on the Tier change event" — [Learn archive: Software Rendering Usage in WPF](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/software-rendering-usage-in-wpf)
- **[snippet-only]** From a search-result summary spanning Microsoft's rendering-tier page and third-party posts (none opened): the tier is read as `RenderCapability.Tier >> 16`; Tier 0 means all rendering in software with no hardware acceleration; Tier 1 is partial hardware acceleration; `RenderCapability.TierChanged` signals changes. The summary did not describe Tier 2, and it flagged that some third-party pages wrongly treat Tier 1 as software — [Learn: Graphics rendering tiers](https://learn.microsoft.com/nb-no/dotnet/desktop/wpf/advanced/graphics-rendering-tiers)
- The WPF trace keyword set includes `KeywordGraphics 0x1000` and IRT events, so software/IRT activity is observable through ETW on modern .NET — [dotnet/wpf wpf-etw.man](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man)
- For WPF on .NET 5+ binding failures reach Visual Studio independent of the WPF Trace Settings level; for .NET Framework the level is set under Tools > Options > Debugging > Output Window > WPF Trace Settings > Data Binding — [Learn: XAML data binding diagnostics](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/xaml-data-binding-diagnostics?view=visualstudio)

### Inferences
- Runtime check to log at startup (public API; snippet not taken from a source): `int tier = RenderCapability.Tier >> 16; var mode = RenderOptions.ProcessRenderMode; var hwnd = (HwndSource)PresentationSource.FromVisual(window); var perWindow = hwnd.CompositionTarget.RenderMode;` and log `tier`, `mode`, `perWindow`, `SystemParameters.IsRemoteSession`.
- A/B test that isolates rendering as the cause: run once normally and once with `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly` set at the top of `App` startup. If time-to-`ContentRendered` barely changes, rendering mode is not the bottleneck; if the software run is much slower on the dev machine but equal to what users report, users are probably on software rendering.
- The 2010 blog's registry example prints `dword:00000000` while describing how to force software; **[unverified]** recall of the registry documentation says `1` disables hardware acceleration. Verify before use.
- **[unverified]** Recall: Perforator's tint and overlay options work through a debug-control channel that WPF only opens when a registry value (`EnableDebugControl` under `...\Avalon.Graphics`) is set before the app starts, which is why the suite must be started first. No public standalone registry "tint" switch is known; not confirmed in this session.
- **[unverified]** `PresentationTraceSources` (e.g. `PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning` plus a `TraceListener`, or `PresentationTraceSources.TraceLevel="High"` on a single binding) and `System.Windows.Diagnostics.VisualDiagnostics` / `BindingDiagnostics.BindingFailed` (.NET 5+; active only when diagnostics are enabled, i.e. debugger attached or the `ENABLE_XAML_DIAGNOSTICS_SOURCE_INFO` environment variable) exist as in-process hooks; details are from recall and were not checked against docs here.
- The Remote Desktop statement dates from 2010; later Windows/RDP versions can provide GPU acceleration in remote sessions, so `RenderCapability.Tier` should be measured in the actual session rather than assumed. **[unverified]**.

### Gaps
- No current (.NET 8+) Microsoft page was read that restates the software-fallback list; the cited list is from 2010 and items like XP layered windows are obsolete.
- No documented switch to tint software-rendered regions without Perforator was found.
- The Learn pages for `PresentationTraceSources` and `System.Windows.Diagnostics` were not read.

## Self-instrumentation that is precise for WPF: process start to ContentRendered / Dispatcher idle, EventSource markers visible in PerfView

### Takeaway
A few timestamps (process creation, `Main`/`App` constructor, `OnStartup`, window `Loaded`, `ContentRendered`, first dispatcher idle) emitted through an `EventSource` give a stable baseline number and, in the same trace, the range markers that PerfView's Alt+R range selection needs; this technique is confirmed by the endjin write-up, the concrete code below is not from a source.

### Cited Findings
- Custom Start/Stop events (`SetTab.Start` / `SetTab.Stop`) were used to bracket an interaction and then to set the PerfView time range (select the two Time cells, Alt+R) before inspecting WPF events inside it — [endjin: Analysing WPF Performance](https://endjin.com/blog/2024/01/analysing-wpf-performance-using-etw-and-perfview)
- dotnet-trace accepts custom EventSource providers by name through `--providers Name[:Flags[:Level]]` and can stop on a named event (`--stopping-event-provider-name`, `--stopping-event-event-name`), which allows "trace until the window is rendered" — [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- **[snippet-only]** Microsoft's WPF startup guidance, as summarised in search results: postpone initialization code until after the main application window is rendered — [Learn: Application startup time (WPF)](https://learn.microsoft.com/th-th/dotnet/desktop/wpf/advanced/application-startup-time)
- The WPF provider itself has `WClientAppCtor` and `WClientAppRun` events that mark Application construction and `Run` — [dotnet/wpf wpf-etw.man](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man)

### Inferences
- **[unverified]** code sketch (public APIs, not compiled in this session):

```csharp
[EventSource(Name = "MyCompany-MyApp-Startup")]
sealed class StartupEvents : EventSource
{
    public static readonly StartupEvents Log = new();
    [Event(1)] public void Mark(string name, double msSinceProcessStart) => WriteEvent(1, name, msSinceProcessStart);
}

static class StartupClock
{
    static readonly DateTime ProcessStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    public static void Mark(string name)
    {
        double ms = (DateTime.UtcNow - ProcessStartUtc).TotalMilliseconds;
        StartupEvents.Log.Mark(name, ms);
        Trace.WriteLine($"[startup] {name}: {ms:F0} ms");
    }
}

// App.xaml.cs
public App() => StartupClock.Mark("AppCtor");       // first managed marker: everything before it is host + runtime start

protected override void OnStartup(StartupEventArgs e)
{
    StartupClock.Mark("OnStartup");
    base.OnStartup(e);
    var w = new MainWindow();                       // BAML parse of the window happens in InitializeComponent
    StartupClock.Mark("MainWindowConstructed");
    w.Loaded += (_, _) => StartupClock.Mark("Loaded");
    w.ContentRendered += (_, _) =>
    {
        StartupClock.Mark("ContentRendered");       // first frame handed to the render thread
        w.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            () => StartupClock.Mark("DispatcherIdle")); // queue drained: app is interactive
    };
    w.Show();
}
```

- How to read the markers: process start to `AppCtor` = host resolution + runtime start + assembly load + antivirus scan of the binaries; `AppCtor` to `OnStartup` = `App.xaml` resources and application-level setup; `OnStartup` to `MainWindowConstructed` = BAML parse, resource dictionaries, JIT of control code; `MainWindowConstructed` to `Loaded` = HWND creation, first measure/arrange; `Loaded` to `ContentRendered` = render pass, font/glyph and image realization; `ContentRendered` to `DispatcherIdle` = deferred work (bindings at DataBind priority, queued loads). The mapping of phases to causes is reasoning, not a cited fact.
- `Process.StartTime` is the kernel's process-creation time, so the first marker includes everything before managed `Main`; `Environment.TickCount64` only gives milliseconds since boot and cannot provide the process-start origin by itself; `Stopwatch` started in `Main` misses pre-`Main` time. **[unverified]** in this session.
- `DispatcherPriority.ContextIdle` (3) and `ApplicationIdle` (2) both run after Loaded (6), Render (7), DataBind (8) and Input (5) work, so a callback at either priority approximates "UI has settled". **[unverified]** numeric values from recall.
- In PerfView the EventSource is enabled by adding `*MyCompany-MyApp-Startup` to Additional Providers (leading `*` = name-derived GUID). **[unverified]** syntax from recall.
- The same pattern measures a single secondary window: `Mark("Dlg.Start")` before `new Dialog()`, `Mark("Dlg.Rendered")` in its `ContentRendered`, `Mark("Dlg.Idle")` at `ContextIdle`.

### Gaps
- No Microsoft or practitioner source with this exact ContentRendered/idle pattern was read; treat the code as a proposal to test.
- No source read quantifies how far `ContentRendered` lags the moment pixels reach the screen (render thread + DWM present).

## How much do the debugger, XAML Hot Reload and Debug configuration distort measurements, and how to measure correctly

### Takeaway
Microsoft's documented position is qualitative: Release builds without the debugger give the most accurate numbers, Debug builds lack optimizations, and the debugger itself adds time by intercepting exception and module-load events; no source read puts a number on the distortion for WPF or on XAML Hot Reload specifically.

### Cited Findings
- "Release build performance numbers in the Performance Profiler are the most precise and accurate"; "Performance numbers in debug builds are less accurate, because debug builds lack these optimizations"; "the debugger itself changes performance times, as it does necessary debugger operations like intercepting exception and module load events"; "External performance problems, like file I/O or network responsiveness issues, won't look much different in a release build or a debug build" — [Learn: Run profiling tools on release or debug builds](https://learn.microsoft.com/en-us/visualstudio/profiling/running-profiling-tools-with-or-without-the-debugger?view=visualstudio)
- XAML Hot Reload is a debugging feature toggled under Tools > Options > Debugging > XAML Hot Reload; the in-app toolbar and binding-failure tooling exist only while debugging — [Learn: XAML data binding diagnostics](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/xaml-data-binding-diagnostics?view=visualstudio)
- First-run vs later-run method used by a practitioner: exercise the path once so JIT is done, wait, then measure the second run to see pure UI cost — [endjin: Analysing WPF Performance](https://endjin.com/blog/2024/01/analysing-wpf-performance-using-etw-and-perfview)
- JIT activity is concentrated at startup and precompilation (ReadyToRun) removes most but not all of it — [PerfView HtmlReportUsersGuide.htm](https://raw.githubusercontent.com/microsoft/perfview/main/src/PerfView/SupportFiles/HtmlReportUsersGuide.htm)

### Inferences
- Measurement protocol (synthesis): (1) `dotnet publish -c Release` with the same settings users get (ReadyToRun, self-contained or not, single-file or not); (2) start from Explorer or a shell, never F5; Ctrl+F5 still uses the build output, not the published layout; (3) record cold start = first launch after reboot/sign-in, and warm start = median of 5 launches after discarding the first; report both; (4) take the number from the `ContentRendered` and `DispatcherIdle` markers, not from a stopwatch in hand; (5) repeat once on a machine comparable to the slowest user machine.
- Since a debugger intercepts module loads and exceptions, apps that load many assemblies or throw first-chance exceptions during startup are slowed most under F5; first-chance exception counts are visible in dotnet-counters (`Exception Count`) and in the Diagnostic Tools Events tab.
- **[unverified]** Recall: with a debugger attached, Visual Studio by default disables JIT optimizations for loaded modules ("Suppress JIT optimization on module load") and may bypass ReadyToRun code; XAML Hot Reload / Live Visual Tree inject a tap assembly into the app and attach source-info to every element created from XAML, which makes BAML loading measurably slower. No figures were found; an A/B run (F5 vs Ctrl+F5 vs published exe) with the startup markers is the way to get the real factor for a given app.
- **[unverified]** Antivirus effect on cold start: Microsoft Defender real-time scanning inspects each DLL on first load; in a PerfView/WPA trace this appears as CPU in `MsMpEng.exe` during the launch window and as file-open latency with the Defender minifilter on the stack. Comparing a launch from a normal folder with a launch from an excluded test folder or a Dev Drive isolates it. Changing exclusions is a security-setting change that belongs to the machine owner.

### Gaps
- No quantitative source for debugger, Debug-configuration or XAML Hot Reload overhead on WPF startup was found.
- No source read on Microsoft Defender's impact on .NET app startup or on how to recognise it in a trace.
- No source read on single-file extraction or trimming effects on measurement.

## How to tell from a trace that the app runs under x64 emulation on Windows on ARM, or in a VM with a basic display adapter

### Takeaway
No source was retrieved for this question; what follows in Inferences is unverified recall and reasoning, and the only cited facts are that VMs and remote sessions are known software-rendering triggers and that the diagnostic tools ship ARM64 builds.

### Cited Findings
- "Rendering in Virtual Machine. WPF will try and render in HW but on some VMs the graphic emulator that comes with the VM is not working correctly. User will have to switch to software"; Remote Desktop and Tier 0 adapters also lead to software rendering — [Learn archive: Software Rendering Usage in WPF](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/software-rendering-usage-in-wpf)
- Software rendering cost scales with pixel count ("proportional to the number of pixels rendered"), so large or high-DPI windows suffer most — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))
- dotnet-trace and dotnet-counters are published as separate Windows x64 and Arm64 builds, and a tool/target bitness mismatch is an explicit error case — [Learn: dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
- Snoop lists ARM/ARM64 support since 4.0.0 — [snoopwpf/snoopwpf README](https://github.com/snoopwpf/snoopwpf)

### Inferences
- **[unverified]** In-process check to log at startup: `RuntimeInformation.ProcessArchitecture` vs `RuntimeInformation.OSArchitecture`; `X64` process on `Arm64` OS means x64 emulation. Task Manager's Details tab "Architecture" column shows the same per process.
- **[unverified]** In an ETW trace (PerfView "Image Load" events / WPA "Images" table) an emulated x64 process loads the emulator binaries (`xtajit64.dll` and related) and CPU samples show time inside them; the trace header/system configuration shows an ARM64 processor while the process's modules are x64. Under emulation JIT-compiled code is translated a second time, so JIT-heavy startup is disproportionately slow; publishing `win-arm64` removes it.
- **[unverified]** Basic display adapter / no GPU: `RenderCapability.Tier >> 16` returns 0; Device Manager or `dxdiag` shows "Microsoft Basic Display Adapter" (or a Hyper-V / remote display driver); in a CPU trace the WPF render thread's time sits in the software rasterizer inside `wpfgfx_cor3.dll` instead of in the D3D/driver modules, and no vendor GPU driver DLL is loaded into the process.
- A cheap field diagnostic is to log, at startup, the tuple (process arch, OS arch, render tier, `SystemParameters.IsRemoteSession`, primary screen pixel size and DPI) next to the startup timings; this distinguishes "slow because emulated", "slow because software-rendered" and "slow everywhere" without a trace.

### Gaps
- No source read on detecting x64 emulation (module names, WPA columns) or on WPF behaviour under emulation on Windows on ARM.
- No source read on WPF's rendering tier under Microsoft Basic Display Adapter / WARP or in common VM products as of 2026.
- No current source on WPF hardware acceleration over RDP on Windows 11.

## Ordered triage procedure (5-8 steps) that separates JIT/assembly loading, XAML/BAML parse and layout, image decoding, font loading, software rendering, debugger overhead, antivirus, slow I/O

### Takeaway
The cheapest order is: remove measurement distortion first, get one baseline number with markers, compare cold vs warm and first vs second open, then take one PerfView trace with the WPF provider and split the marked range into JIT, parse/layout, render and I/O; the steps below are a synthesis and the mapping from symptom to cause is partly unverified.

### Cited Findings
- Release build, no debugger, Performance Profiler path is the documented accurate way; the debugger adds module-load and exception interception — [Learn: Run profiling tools on release or debug builds](https://learn.microsoft.com/en-us/visualstudio/profiling/running-profiling-tools-with-or-without-the-debugger?view=visualstudio)
- JITStats: sum of Foreground + Multicore JIT Background time is what slows startup; ReadyToRun reduces it — [PerfView HtmlReportUsersGuide.htm](https://raw.githubusercontent.com/microsoft/perfview/main/src/PerfView/SupportFiles/HtmlReportUsersGuide.htm)
- WPF provider string `E13B77A8-14B6-11DE-8069-001B212B5009:*:*`, events `WClientParseBaml*`, `WClientLayout*`, `WClientMeasure*`, `WClientUceRender`, `WClientResourceFind*`, `WClientFontCache`, `WClientCreateIRT` — [endjin: enabling ETW](https://endjin.com/blog/wpf-performance-enable-etw); [dotnet/wpf wpf-etw.man](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Shared/Tracing/wpf-etw.man)
- Marker events plus Alt+R range selection; first interaction vs second interaction to separate JIT from steady-state UI cost — [endjin: Analysing WPF Performance](https://endjin.com/blog/2024/01/analysing-wpf-performance-using-etw-and-perfview)
- `RenderCapability.Tier` check and forced `RenderMode.SoftwareOnly`; list of software-rendering triggers — [Learn archive: Software Rendering Usage in WPF](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/software-rendering-usage-in-wpf)
- dotnet-counters / dotnet-trace child launch for JIT and assembly counts without admin — [Learn: dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters); [Learn: dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- "Disable high-quality image rescaling ... consider decoding your images to a size near the size that they will be displayed" (image cost indicator in Perforator) — [Learn (archived): WPF Performance Suite](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/aa969767(v=vs.100))

### Inferences
Proposed order (each step names the cause it confirms or rules out):

1. **Remove debugger / Debug distortion.** Publish Release, start the exe from Explorer, read the `ContentRendered` and `DispatcherIdle` markers. If the problem disappears, the cause was F5 (debugger, Hot Reload, unoptimized code). Rules out: debugger overhead.
2. **Cold vs warm, first vs second.** Record first launch after reboot, then 5 more; for a slow window, open it twice in one session. Large cold-vs-warm gap points to disk I/O, antivirus scanning and DLL loading; large first-vs-second-open gap inside one process points to JIT, type loading and one-time resource/font/image work; no gap at all points to tree size, layout, rendering or code that runs every time.
3. **Environment fingerprint.** Log process vs OS architecture, `RenderCapability.Tier >> 16`, `IsRemoteSession`, screen size/DPI. Tier 0 or emulation explains "slow only on some machines" before any profiling. Confirm rendering with the forced `SoftwareOnly` A/B run.
4. **JIT / assembly loading.** `dotnet-counters collect --format csv --refresh-interval 1 --counters System.Runtime -- MyApp.exe` and read methods jitted, JIT time, assembly count; or PerfView JITStats restricted to the marker range. JIT time that is a large share of the range confirms JIT; a ReadyToRun publish that removes most of it is the proof.
5. **One PerfView trace with the WPF provider** over the marked range (Thread Time + `E13B77A8-...:*:*` + own EventSource). Sum durations: `WClientParseBaml` (XAML/BAML and resource dictionaries), `WClientResourceFind` / `WClientPropParentCheck` bursts (template and style lookup), `WClientLayout`/`Measure`/`Arrange` (tree size, layout), `WClientUceRender` and render-thread CPU (rendering, overdraw, IRTs).
6. **CPU stacks on the UI thread and render thread in the same range.** Group by module: time in WIC / image codec modules = image decoding (fix: decode to display size, defer); time in DirectWrite / font cache modules or many font file reads = font loading; time in the WPF native software rasterizer = software rendering; time in app code = own startup logic. **[unverified]** module names are from recall.
7. **Blocked time and I/O.** In Thread Time or VS File I/O / Instrumentation look for the UI thread waiting: synchronous file, network, registry or database calls, lock waits. Slow file opens with a filter driver on the stack, or CPU in the antivirus process during launch, confirm antivirus; repeat from an excluded folder to prove it.
8. **Tree size and bindings last.** If layout/template time dominates, count elements (Snoop, Live Visual Tree, or a `VisualTreeHelper` walk) and check XAML Binding Failures counts; virtualization and deferred loading are the usual remedies.

### Gaps
- The procedure is a synthesis; no single source read prescribes this order, and steps 3, 6 and 7 rely on unverified recall for module names and trace signatures.
- No measured thresholds ("JIT above N ms is abnormal") were found; all judgments are relative to the app's own marker range.
- Font loading and image decoding have no confirmed dedicated WPF ETW events in the part of the manifest that was read, so their identification rests on CPU stacks.
