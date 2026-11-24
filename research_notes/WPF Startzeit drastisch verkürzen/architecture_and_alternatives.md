# Architecture tricks and alternative UI stacks for WPF startup (state: 20 Nov 2026)

Reading notes for the report writer:
- Tags used on every number: **[measured]** (author ran it, method stated), **[vendor]** (marketing or product owner's own figure), **[anecdote]** (single remark, no method), **[snippet]** (seen only in a search-result summary, page not opened by me; treat as weaker).
- **[.NET FW]** marks .NET Framework-era material that does not carry over unchanged to .NET 8/9/10.
- Repo context (checked locally): the main app targets `net10.0-windows`, side projects `net8.0-windows`; a grep of `Directory.Build.props` and the `.csproj` files found no `PublishReadyToRun`, `TieredCompilation`, or `SplashScreen` settings (publish profiles and runtimeconfig templates were not checked).
- **[snippet, URL unconfirmed]** means the claim appeared in a search summary and the link given is the closest result listed, not a page I opened; verify before quoting.
- Part A = questions A1-A8 (stay on WPF). Part B = questions B1-B5 (alternatives and ceilings).

## A1. Splash screens: built-in SplashScreen build action versus a WPF window

### Takeaway
A splash image shown through a plain Win32 window appears well before the first WPF window because it avoids WPF's module loads and initialisation; it changes perceived start only, real time-to-first-window is unchanged. A splash built as a WPF `Window` pays the full WPF load cost before anything is visible, which defeats the purpose. No fetched source supports the stronger claim that the built-in splash appears "before the CLR loads".

### Cited Findings
- Microsoft's guidance: use a splash when there is "a significant, unavoidable delay"; the image shows "almost immediately" and fades when the first UI is ready; available via the `SplashScreen` class since .NET Framework 3.5 SP1; a custom native Win32 splash must be displayed "before the Run method is called". — [Microsoft Learn, Application Startup Time](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time) (page dated 2017, service tag dotnet-framework, **[.NET FW]** origin, still the current Learn page)
- How to enable: add an image (any WIC format: BMP, GIF, JPEG, PNG, TIFF), set Build Action = `SplashScreen`; image appears centred and "fades when the main application window appears". — [Microsoft Learn, How to add a splash screen](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/how-to-add-a-splash-screen-to-a-wpf-application)
- Mechanism of the native approach (WPF perf team, 2007): "use as little as possible managed code during startup and to use interop to display as-quickly-as-possible an unmanaged Win32 window"; in the BMP variant the WPF main window loads 21 more modules than the splash; WPF additionally has to start the PresentationFontCache service and initialise the DirectX device. — [Jossef Goldberg blog, archived on Microsoft Learn](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/splash-screen-to-improve-wpf-application-perceived-cold-startup-performance) **[.NET FW]**
- Only published number: cold start on a "slow XP machine": splash after ~2-3 s, main window after ~13 s, "a significant 11 sec improvement" in perceived time. **[anecdote, 2007, Windows XP, .NET FW 3.0]** — [same source](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/splash-screen-to-improve-wpf-application-perceived-cold-startup-performance)
- The 3.5 SP1 announcement states the splash "does not actually improve application cold startup time", only perceived startup. **[snippet]** — [Jossef Goldberg, What's new in WPF 3.5 SP1](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/whats-new-in-wpf-3-5-sp1-splash-screen-to-improve-perceived-startup-perf)
- A reader of that blog went one step further: a small native launcher exe that shows the splash "without loading the CLR or any WPF assemblies" and then starts the WPF app. **[anecdote, 2008]** — [comment on the same archived post](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/splash-screen-to-improve-wpf-application-perceived-cold-startup-performance)

### Inferences
- On 2026 hardware the 2007 ratio (13 s vs 2-3 s) is not representative; an empty WPF window on .NET 8 takes about 0.5-0.7 s process-start-to-window (see B1), so a splash only pays off if the app's own startup work adds visible delay beyond that.
- A splash cannot help secondary-window (dialog) latency at all; it only covers process start.
- The splash is a static bitmap: no animation, no theming, and it can look out of place next to a designed first window; a native-launcher splash adds a second exe to maintain.

### Gaps
- No modern (.NET 8/9/10, Windows 11) measurement of "time until the built-in splash is visible" was found.
- Not verified in a fetched source: the exact point at which the built-in `SplashScreen` build action shows its image on modern .NET, and whether the runtime is already loaded at that moment. The 2007 sample describes managed code using interop ("as little as possible managed code"), which implies the runtime is already running; what the sources support is "before WPF's window/XAML initialisation", not "before the CLR loads". Only the separate native-launcher approach (reader comment above) shows an image before any runtime load.

## A2. Pre-creating and warming secondary windows, window reuse (Hide instead of Close)

### Takeaway
WPF officially supports reusing a window instance via `Hide()` plus cancelling `Closing`; a closed window can never be shown again. Pre-creating a hidden window moves JIT, XAML parsing and template expansion out of the click path, but I found no published before/after measurement for it.

### Cited Findings
- "Once a window is closed, the same object instance can't be used to reopen the window"; attempting it throws `InvalidOperationException`. — [Microsoft Learn, How to close a window](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box) (page dated Oct 2024, service dotnet-desktop, i.e. modern .NET)
- "A hidden window can be reopened, unlike a window that has been closed. If you're going to reuse a window object instance, hide the window instead of closing it." Pattern: in `Closing`, set `e.Cancel = true` and call `Hide()`. — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box)
- Pitfall, modal reuse: "If a window is shown modally with ShowDialog, the DialogResult property will be set to null when the window is hidden. You'll need to communicate state back to the calling code by adding your own property to the window." — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box)
- Pitfall, bypass: the system-menu Close item and the title-bar Close button close instead of hide unless `Closing` is cancelled. — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box)
- Pitfall, lifetime: every instantiated window is added to `Application.Windows`, and the first window instantiated becomes `Application.MainWindow` automatically; closing an owner closes owned windows; `ShutdownMode` decides whether the app exits on main-window close or last-window close. — [Microsoft Learn, Windows in WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)
- A window can be shown without stealing focus by setting `ShowActivated = false` before the first `Show()`. — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)
- Vendor variant of pre-warming: DevExpress `ThemeManager.PreloadThemeResourceAsync` at startup (optionally behind a splash): "Although this won't speed up the main view, all child views will open faster." No numbers in text. **[vendor]** — [DevExpress, 10 Tips to Reduce WPF App Startup Time](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)
- A hidden "preloading window" that pre-JITs the controls of the real window is described in an Infragistics article, with the caveat that it only speeds up the first rendering of the preloaded controls. **[snippet; the article URL now redirects to a blog index, content not retrievable]** — [Infragistics blog](https://www.infragistics.com/community/blogs/b/kiril_matev/posts/optimizing-infragistics-wpf-controls-startup-time)

### Inferences
- Because the first window created becomes `MainWindow`, a pre-created hidden helper window created before the real main window will silently become `MainWindow` (and can keep the process alive or end it depending on `ShutdownMode`); the pre-create must happen after the main window exists, or `MainWindow`/`ShutdownMode` must be set explicitly.
- A hidden pre-created window that is never opened still costs its full memory (visual tree, bitmaps) and startup-idle CPU; it is a trade of idle time and RAM for click latency.
- Reused windows keep their state (scroll position, text, selection, running animations); a reset step is needed before each `Show()`.
- For modal use, `ShowDialog()` on a reused instance needs a custom result property since `DialogResult` is `null` after `Hide()`.

### Gaps
- No source measured "first open of a WPF dialog" versus "Show() of a pre-created hidden instance" on .NET 8/9/10. This is the single most relevant missing number for the "100x" question (see B4).
- Not confirmed in a fetched source: whether `ShowDialog()` may be called repeatedly on a hidden (not closed) instance in current .NET. A Stack Overflow answer seen only as a snippet suggests it works; test before relying on it.
- Not confirmed in a fetched source: whether constructing a window without showing it already applies templates and runs layout (commonly it does not until the window is shown or `Measure/Arrange` is forced), i.e. how much of the cost a "construct only" warm-up actually removes.

## A3. Deferring work past first paint (Loaded vs ContentRendered, Dispatcher priorities, staged population)

### Takeaway
Microsoft's own guidance is to postpone initialisation until after the main window is rendered and to keep dispatcher work items small; `ContentRendered` is the documented "window is effectively open" signal, and low dispatcher priorities let remaining work run only when input and rendering are idle.

### Cited Findings
- "Consider postponing initialization code until after the main application window is rendered"; class constructors can cascade into many others. — [Microsoft Learn, Application Startup Time](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time)
- Same page: "Instead of using XAML to set the DataContext declaratively for the main window, consider setting it programmatically in the OnActivated method"; also avoid `app.config`-style configuration on the startup path and unnecessary module loads (check with Process Explorer). — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time)
- Event order: `SourceInitialized` is raised during `Show()`; "the Loaded and ContentRendered events are raised only after the Activated event is raised. With this in mind, a window can effectively be considered opened when ContentRendered is raised." — [Microsoft Learn, Windows in WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)
- Dispatcher: "selects work items on a priority basis and runs each one to completion"; there are ten priority levels (`DispatcherPriority`); "maximize the Dispatcher throughput by keeping the work items small". — [Microsoft Learn, Threading Model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)
- Staged work on the UI thread: "If we break up the task of calculation into manageable chunks, we can periodically return to the Dispatcher and process events"; the sample re-queues itself with `Dispatcher.InvokeAsync(..., DispatcherPriority.SystemIdle)`, which runs "only when there are no important events to process"; Word's background spell check is cited as using the same idle-time mechanism. — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)
- Why staging matters in WPF specifically: "Since WPF works with the UI in the main thread, the entire window remains frozen until all elements are loaded"; DevExpress ships a `LoadingDecorator` that shows light UI first and loads heavy sections afterwards. **[vendor]** — [DevExpress, 10 Tips](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)

### Inferences
- A staged pattern consistent with these sources: constructor does only `InitializeComponent()` with a light shell, `ContentRendered` kicks off data/image loading, list items are appended in small batches via idle-priority dispatcher callbacks so each batch is one short work item.
- Skeleton/placeholder UI is the same idea applied visually: show the cheap frame first, fill later. No source quantified its effect for WPF.

### Gaps
- No measured numbers for "deferred to ContentRendered" versus "in constructor" were found; the benefit equals whatever work is moved, so it is app-specific.
- No fetched source documents incremental population of an `ItemsControl` in batches with numbers.
- The relative order of `DispatcherPriority` values (Loaded, Render, Background, ContextIdle, ApplicationIdle, SystemIdle) was not quoted from a fetched page; the report writer should cite the `DispatcherPriority` enum reference if exact ordering is stated.

## A4. Moving work off the UI thread (Task.Run, Freeze, what must stay on the UI thread)

### Takeaway
WPF objects are bound to the thread that created them; computation and I/O belong on background threads with results marshalled back through the Dispatcher. This reduces time-to-first-paint only for work that is not itself UI-object creation.

### Cited Findings
- "Typically, WPF applications start with two threads: one for handling rendering and another for managing the UI"; the UI thread "receives input, handles events, paints the screen, and runs application code". — [Microsoft Learn, Threading Model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)
- Big operations (large calculation, remote query) should run "in a separate thread, leaving the UI thread free"; a background thread "cannot update a text box when it is finished" and must register a work item via `Dispatcher.InvokeAsync`/`BeginInvoke`. — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)
- Multiple UI threads are supported: "It's perfectly acceptable for one Thread/Dispatcher combination to manage multiple windows, but sometimes several threads do a better job", especially "if there's any chance that one of the windows will monopolize the thread". — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)
- Visual Studio 2026 moved MEF-based editor extension loading to a background thread to speed up startup; extensions had often been loaded on the UI thread. **[snippet, URL unconfirmed, no numbers]** — [Visual Studio Blog](https://devblogs.microsoft.com/visualstudio/?p=255520)
- General rule from a vendor: slow constructor work in View/ViewModel (example: a `ProcessData` call) should be processed asynchronously; find it with the Visual Studio CPU Usage profiler. **[vendor]** — [DevExpress, 10 Tips](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)

### Inferences
- XAML parsing, control instantiation, template expansion, layout and binding evaluation must happen on the window's UI thread; `Task.Run` cannot shorten those. JSON parsing, file/network I/O and pixel decoding can leave the UI thread.
- A second UI thread (own Dispatcher) is the only documented way to build a heavy secondary window without blocking the main window, at the price of no shared UI objects between the two threads.

### Gaps
- The `Freezable`/`Freeze()` rule (a frozen `BitmapSource`/`Brush` can be handed from a worker thread to the UI thread; `BitmapCacheOption.OnLoad` forces decode at creation) was not confirmed in a fetched page here; it is standard WPF knowledge and should be cited from the "Freezable Objects Overview" doc if used.
- No measured WPF numbers for off-thread image decoding versus on-thread decoding were found.

## A5. Lazy XAML (x:Shared, DataTemplate deferral, on-demand UserControls, lazy tabs, virtualization)

### Takeaway
The documented, built-in laziness in WPF is UI virtualization (item containers created only when visible) and deferred template content; everything else is manual (load a section when first needed). Virtualization is easy to switch off by accident.

### Cited Findings
- UI virtualization: "the item container generation and associated layout computation for an item is deferred until the item is visible"; enabled by default for `ListBox`/`ListView` when bound to data; must be enabled explicitly for `TreeView`, `ComboBox`, `ContextMenu`. — [Microsoft Learn, Optimize control performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- Conditions that silently disable it: item containers added directly (e.g. explicit `ListBoxItem`s), mixed container types, `ScrollViewer.CanContentScroll = false`, `IsVirtualizing = false`. — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- Container recycling (`VirtualizationMode = Recycling`) reuses containers while scrolling; `VirtualizingStackPanel` virtualises in one direction only; "no WPF controls offer built-in support for data virtualization". — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- Template weight: un-optimised control templates create "a lot of extra unnecessary objects in the visual tree". — [same source](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- On-demand sections in practice (vendor examples): ribbon page groups moved into a template so they "load on demand"; tab content created on selection (`CacheTabsOnSelecting`) rather than all up front (`CacheAllTabs` trades startup for switch speed); lightweight themes restructure resource dictionaries to cut JIT and resource-load work. No numbers in text (charts are images). **[vendor]** — [DevExpress, 10 Tips](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)

### Inferences
- For a secondary window, the cheapest structural win is usually to make the first screenful small: keep off-screen sections (collapsed panels, non-selected tabs, long lists) out of the initial visual tree and insert them after `ContentRendered`.
- An `ItemsControl` with a plain `StackPanel`/`WrapPanel` items panel, or one hosted inside an outer `ScrollViewer`, creates every item up front; that is a common hidden cause of slow dialog opening.

### Gaps
- `x:Shared="False"`, deferred `ResourceDictionary` content, and the default lazy behaviour of stock `TabControl` content were not confirmed in fetched sources; no measurements found.
- The 2017 Learn page gives no timing table for virtualised versus non-virtualised lists.

## A6. Warm-up at app start: JIT pre-touching, ProfileOptimization (multicore JIT), ReadyToRun

### Takeaway
Two supported, low-effort runtime levers exist on modern .NET: `ProfileOptimization` (background JIT from a recorded profile; first run gains nothing) and ReadyToRun publishing (pre-compiled code for the app's own assemblies). Published gains are in the tens of percent, and for a near-empty app ReadyToRun measured as roughly nil.

### Cited Findings
- `ProfileOptimization` is available in .NET 8, 9, 10 and 11 (API monikers); it "reduces startup time" by background-compiling methods recorded in the previous run; "requires a multicore computer"; "There is no performance benefit the first time a profile is created"; call `SetProfileRoot(folder)` (folder must exist) then `StartProfile(file)`; "You can maintain multiple profiles ... so that each such activity has its own profile" (not restricted to startup, usable for "any activity that requires heavy use of the JIT compiler"). — [Microsoft Learn, ProfileOptimization class](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization)
- Measured effect of multicore JIT: three desktop WPF apps (one was Windows Performance Analyzer) improved startup 16% to 35%; Bing ASP.NET services ~155 s to just under 80 s; "Improvements of 20%-50% are very typical". **[measured by Microsoft, 2012, .NET FW 4.5, hardware unspecified]** — [.NET Blog, An easy solution for improving app launch performance](https://devblogs.microsoft.com/dotnet/an-easy-solution-for-improving-app-launch-performance/)
- ReadyToRun: "most applications that have large amounts of code receive large performance benefits"; apps with small amounts of code "will likely not experience a significant improvement ... as the .NET runtime libraries have already been precompiled with ReadyToRun"; assemblies grow 2-3x; R2R code is less optimised and tiered compilation later replaces hot methods; JIT still runs for generics across assemblies, interop, dynamic methods. — [Microsoft Learn, ReadyToRun deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run) (page updated 10 Nov 2026)
- Composite ReadyToRun (self-contained only, .NET 6+; default for self-contained single-file R2R since .NET 7) compiles more methods ahead of time at the cost of longer publish and larger output. — [same source](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Measured R2R on an empty WPF template app, .NET 8, Windows 11 24H2 VM (2 cores, 8 GB), process start to window shown, right after VM restore: plain 0.68 s / 0.50 s; ReadyToRun 0.52 s / 0.49 s / 0.57 s. Author: few runs, one environment, read as a trend. **[measured, May 2025]** — [suusanex on Zenn](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en)
- Defaults on modern .NET: tiered compilation and quick JIT are on since .NET Core 3.0 ("Enabling quick JIT decreases startup time"); quick JIT for loops "may improve startup performance"; `DOTNET_ReadyToRun=0` forces JIT of framework code (useful to see how much R2R already saves); dynamic PGO is a separate runtime-throughput feature. — [Microsoft Learn, Compilation config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation) (page dated Sep 2026)
- R2R versus JIT on a non-UI app for scale: minimal ASP.NET Core API, .NET 11 RC2, Ryzen 9 7950X, Ubuntu: time to first request 118 ms JIT, 84 ms R2R self-contained, 37 ms Native AOT. **[measured, May 2026, not desktop UI]** — [Start Debugging](https://startdebugging.net/2026/05/native-aot-vs-readytorun-vs-jit-in-dotnet-11/)
- Vendor ranking: "MultiCore JIT is not as effective as Ngen.exe"; Ngen is .NET Framework only; R2R can be combined with multicore JIT on modern .NET. **[vendor]** — [DevExpress, 10 Tips](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)
- JIT is a real share of a large app's start: Paint.NET 5.0.3 on .NET 8 preview JIT-compiled about 19,800 methods at tier 0 on x64 during startup (PerfView, Started to Ready events, Intel Cascade Lake). **[measured, gist by GitHub user kunalspathak, June 2023]** — [kunalspathak gist](https://gist.github.com/kunalspathak/da3685ed70c2b1df12317c454759834d)

### Inferences
- For a small app, R2R of the app's own assemblies buys little because WPF and the base libraries are already precompiled; the gain grows with the amount of own and third-party code on the startup path.
- Realistic combined effect of R2R plus `ProfileOptimization` on a WPF app: roughly 1.2x-1.5x on warm start (16-35% is the only WPF-specific measurement, and it predates tiered JIT and R2R, which already remove part of what multicore JIT used to save).
- Since the API allows multiple profiles for any JIT-heavy activity, a separate `StartProfile` per secondary window, started just before first open, would pre-JIT that window's code path on later runs (my application of the documented API, not a documented WPF recipe).
- Manual "touch the types on a background thread" warm-up competes with the UI thread for the same cores during startup; it suits the idle period after first paint, not the launch itself.

### Gaps
- No .NET 8/9/10 measurement of `ProfileOptimization` on a WPF app was found; the 16-35% figure is .NET Framework 4.5.
- No controlled WPF benchmark toggling `PublishReadyToRun`, `TieredCompilation`, `TieredPGO` separately was found. A course page claims "20-36% faster startup from R2R" with no method **[snippet, unverified]**.
- DevExpress's before/after charts for Ngen, multicore JIT, R2R and lightweight themes are images; the numbers could not be read.
- No source measured pre-loading fonts/images or pre-instantiating templates.

## A7. Keeping a process alive (tray/background process, single instance, startup tasks)

### Takeaway
The only technique that makes "open" near-instant is not starting at all: keep a process resident and just show a window. Microsoft itself now does this for Word (Startup Boost: preloaded, paused, resumed on open) but publishes no speed-up figure; the costs are RAM, user acceptance and added lifecycle complexity. No source measured the pattern for WPF.

### Cited Findings
- Office Startup Boost: "enables the operating system to preload Word by starting parts of the app before you open it"; the preloaded app "remains in a paused state until either the user opens it ... or the operating system removes the app from memory to reclaim resources"; requires at least 8 GB available RAM and 5 GB disk; tasks are turned off in Energy Saver mode; implemented as two Task Scheduler tasks ("Office Startup Boost", "Office Startup Boost Logon"); user can disable it in Word options. No speed-up figure is given. — [Microsoft Learn, New Startup Boost tasks](https://learn.microsoft.com/en-us/microsoft-365/troubleshoot/miscellaneous/new-startup-boost-tasks-windows-task-scheduler) (dated Mar 2025)
- Further Startup Boost details: waits 10 minutes after logon for an idle system; only runs if Word was launched recently; Office updates recreate the scheduled tasks after a user disabled them. **[snippet, URLs unconfirmed]** — [Message Center MC1041470 mirror](https://mc.merill.net/message/MC1041470), [The Register](https://www.theregister.com/2025/05/01/microsoft_will_preload_office_apps/)
- Windows File Explorer: Microsoft said it was "exploring preloading File Explorer in the background to help improve File Explorer launch performance"; press coverage was partly critical. **[snippet, URL unconfirmed]** — [PC Gamer](https://www.pcgamer.com/software/windows/windows-11-is-going-to-start-quietly-preloading-file-explorer-in-the-background-to-make-it-faster-which-is-a-good-reminder-that-you-should-probably-try-a-different-file-manager-anyway)
- New Windows Run dialog (C#/WinUI 3, compiled with .NET Native AOT): "median time-to-show of just 94ms" versus 103 ms for the legacy Win32 dialog. The post does not say whether a process is kept resident or pre-launched, and does not define the metric; since the Win32 dialog also scores ~100 ms it is not process-start-to-window. **[measured by Microsoft, 1 May 2026, hardware and method not given]** — [Windows Command Line blog](https://devblogs.microsoft.com/commandline/the-new-run-dialog-faster-cleaner-and-more-capable)
- WPF supports the building blocks: hide instead of close, `ShutdownMode` to keep the app running with no visible window. — [Microsoft Learn, How to close a window](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box), [Windows in WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)

### Inferences
- A resident WPF process holding a pre-built hidden window turns "start the app" into "activate an existing window", skipping runtime start, WPF init, JIT and XAML load together; this is the only route by which a two-orders-of-magnitude perceived improvement is plausible (see B4). No source measured it for WPF.
- Costs visible in the Microsoft examples: memory held while unused (hence the 8 GB gate), battery (hence Energy Saver off-switch), users and admins wanting an opt-out, and negative press about apps that preload themselves.
- A single-instance activation channel (mutex plus pipe, or packaged-app activation redirection) is needed so a second launch signals the resident process instead of starting cold.

### Gaps
- No measured number for a resident WPF process (show time of a hidden window) was found.
- Microsoft published no before/after numbers for Startup Boost.
- Windows `StartupTask`/Run-key mechanics and Windows 11 startup-app throttling were not researched in a fetched source.

## A8. Case studies with published numbers

### Takeaway
Few of the named apps published before/after startup numbers. The ones with numbers show tens-of-percent gains from compilation and laziness work on WPF/.NET Framework, and 40-70% gains where an app moved to WinUI 3 with Native AOT.

### Cited Findings
- PowerToys Command Palette (successor of the WPF-based PowerToys Run; WinUI 3): v0.93 "reduced its startup memory usage by ~15%", load time by ~40%, built-in extension loading time by ~70%, install size by ~55%, "all due to using the full Ahead-of-Time (AOT) compilation mode in Windows App SDK". **[vendor-measured, release date not captured, no hardware]** — [PowerToys v0.93.0 release notes](https://github.com/microsoft/PowerToys/releases/tag/v0.93.0)
- Windows Run dialog rewrite (WinUI 3 + Native AOT): 94 ms median time-to-show. **[measured by Microsoft, May 2026]** — [Windows Command Line blog](https://devblogs.microsoft.com/commandline/the-new-run-dialog-faster-cleaner-and-more-capable)
- File Explorer's WinUI portion of launch (WinUI 3 platform work, announced 11 May 2026): 41% fewer allocations, 63% fewer transient allocations, 45% fewer function calls, 25% less time in WinUI code; no overall launch-time figure given when asked. **[measured by Microsoft]** — [microsoft-ui-xaml discussion #11096](https://github.com/microsoft/microsoft-ui-xaml/discussions/11096)
- Multicore JIT on three WPF apps incl. Windows Performance Analyzer: 16-35% faster startup. **[measured, 2012, .NET FW 4.5]** — [.NET Blog](https://devblogs.microsoft.com/dotnet/an-easy-solution-for-improving-app-launch-performance/)
- Paint.NET (to my knowledge not a WPF app): author stated in 2016 that NGEN native images were "the difference between a 10+ second startup time versus <1 second" on his system. **[anecdote, snippet, URL unconfirmed, .NET FW]** — [Paint.NET forum](https://forums.getpaint.net/topic/110408-pdn-very-slow-to-start/)
- Paint.NET 5.0.3 on .NET 8 preview: startup JIT analysis (about 19,800 tier-0 methods on x64); no end-to-end time published. **[measured]** — [kunalspathak gist](https://gist.github.com/kunalspathak/da3685ed70c2b1df12317c454759834d)
- Visual Studio (WPF shell): VS 2008 SP1 WPF designer startup improved 10-20% **[snippet, URL unconfirmed, .NET FW]** — [ddperf blog archive](https://learn.microsoft.com/en-us/archive/blogs/ddperf/vs2008-sp1-and-net-fx-beta-performance-improvements); VS 2026 loads MEF editor extensions on a background thread **[snippet, URL unconfirmed, no numbers]** — [Visual Studio Blog](https://devblogs.microsoft.com/visualstudio/?p=255520)
- MPGO (profile-guided layout of NGen images) was Microsoft's 2012 answer for desktop launch; the post gives no numbers. **[.NET FW only]** — [.NET Blog, Improving Launch Performance for Your Desktop Applications](https://devblogs.microsoft.com/dotnet/improving-launch-performance-for-your-desktop-applications/)

### Inferences
- Where Microsoft wanted launcher-class responsiveness in 2025-2026 (Command Palette, Run dialog), it chose WinUI 3 plus Native AOT rather than tuning WPF; that is indirect evidence of where the ceiling for WPF sits.

### Gaps
- No published before/after startup numbers found for: JetBrains Rider/ReSharper WPF tooling (the 2018 ReSharper performance-series post returned HTTP 403), dnSpy/ILSpy, Files, GitHub Desktop's WPF predecessor, ModernWpf/Fluent apps, PowerToys Run (WPF) itself.
- No millisecond figures for Visual Studio's WPF startup work were retrievable.

## B1. Measured startup: WPF vs WinUI 3, WinForms, Avalonia, Uno, MAUI, native, Electron/Tauri

### Takeaway
No single controlled benchmark covers all stacks; available numbers disagree by 3-6x between sources. The consistent picture: an empty WPF window on modern .NET costs roughly 0.5-0.7 s from process start and managed Native AOT XAML stacks reach roughly 0.1-0.3 s, so switching to another managed XAML stack buys about 4-5x at most. The one documented case beyond that is a hand-written native Win32/C++ window, which measured "0 s" (below the test's resolution) for an empty window; that is a full rewrite, not a tool, and says nothing about a real UI.

### Cited Findings
- Same-machine comparison, empty template apps, Windows 11 24H2 VM (2 cores, 8 GB, shared host), process start to window shown, measured right after VM snapshot restore (closest to cold), 2-3 runs each **[measured, May 2025]** — [suusanex on Zenn](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en):

  | Stack | Process start to window |
  |---|---|
  | WPF, .NET 8 (JIT) | 0.68 s, 0.50 s |
  | WPF, .NET 8 + ReadyToRun | 0.52 s, 0.49 s, 0.57 s |
  | WPF, .NET Framework 4.8.1 | 0.32 s, 0.31 s |
  | WinUI 3 + Native AOT (self-contained) | 0.09 s, 0.18 s |
  | Native C++ (Win32) | 0 s, 0 s (below resolution) |

  Author's conclusion: WinUI 3 AOT and native are "nearly 0.5 seconds faster than WPF"; ranking Native C++ > WinUI 3 + NativeAOT >> .NET Framework > .NET 8.
- WPF itself under Native AOT (community fork, sample calculator app), Intel i7-14700K, Windows 10, .NET 10 preview, median: 652 ms framework-dependent JIT, 728 ms self-contained JIT, 136 ms AOT + trimmed; working set 82.4 / 82.8 / 56.1 MB. **[measured by the fork's author, run count and definition of "startup" not on the README]** — [ibebbs/wpfaot](https://github.com/ibebbs/wpfaot)
- Uno Platform (benchmark version not stated; the post ties Native AOT to release 6.6), .NET 10, Windows desktop, median of 20 launches, "Build Pulse" app: first frame 1045 ms JIT vs 300 ms Native AOT; interactive 2151 ms vs 546 ms; memory 282 MB vs 212 MB. Second app (Uno.Chefs) on Windows: 1605 ms vs 824 ms. Hardware not specified. **[vendor-measured, 6 Aug 2026]** — [Uno Platform blog](https://platform.uno/blog/native-aot-in-uno-platform-faster-startup-five-platforms/)
- "Hello World" template, .NET 11 preview 4 SDK, Release, cold cache, no AOT, "Windows cold start (WinUI 3 path)": MAUI 11 280 ms, Avalonia 11.3 240 ms, Uno 6 310 ms. The stated test hardware (Pixel 8, iPhone 15, MacBook Pro M2) contains no Windows machine, so the origin of the Windows row is unexplained; WPF and WinUI 3 native not included. **[blog author's figures, 27 May 2026; low confidence for the Windows row]** — [Start Debugging](https://startdebugging.net/2026/05/maui-vs-avalonia-vs-uno-in-2026/)
- Ranking without numbers (trivial app, time until it reports loaded): "WinForms is fastest. WPF is really slow. WinUI next fastest, then Avalonia, MAUI, and Uno but not a lot in it." No versions, hardware, run counts or values in text; the author himself calls the comparison of limited use. **[measured, weak method, July 2025]** — [Matt Lacey](https://www.mrlacey.com/2025/07/how-much-does-start-up-time-matter-when.html)
- "Moderately complex line-of-business application", Ryzen 7 / 32 GB / integrated graphics, WPF on .NET 9 vs WinUI 3 on Windows App SDK 1.8: cold ~1.8 s vs ~1.4 s; warm ~0.9 s vs ~0.7 s; idle working set ~185 MB vs ~155 MB. The same table lists "With Native AOT (preview)" ~0.7 s for WPF, which contradicts the post's own statement that WPF AOT is "not production-ready" and the official status in B3. **[author's own test, no method, 10 Apr 2026; treat the AOT row as unreliable]** — [CTCO blog](https://www.ctco.blog/posts/winui-vs-wpf-2026-practical-comparison/)
- WinUI 3 platform itself: community members state it is still slower than UWP system XAML; one links a benchmark claiming it is "measurably slower than both WPF and UWP" on XAML workloads (not startup), another disputes the WPF part. No figures in the thread. **[anecdote, May 2026]** — [microsoft-ui-xaml discussion #11096](https://github.com/microsoft/microsoft-ui-xaml/discussions/11096)
- Avalonia + Native AOT on Windows: ~0.6 s cold for Avalonia 11.2 (vs ~1.6 s JIT) in one blog **[snippet, URL unconfirmed, no method]** — [CTCO blog, MAUI vs Avalonia](https://www.ctco.blog/posts/maui-vs-avalonia-2026-cross-platform-dotnet-ui/); a Hacker News commenter reported a full Avalonia app starting in 0.3 s with Native AOT **[anecdote, snippet, URL unconfirmed]** — [Hacker News](https://news.ycombinator.com/item?id=39246988)
- Electron/Tauri reference points conflict across 2026 articles seen only in search summaries: 380 ms (Tauri) vs 1,420 ms (Electron) in one, ~100 ms vs ~140 ms in another; conditions unclear, and I cannot attribute each pair to a specific page. **[snippet, URL unconfirmed]** — one of the listed results: [tech-insider.org](https://tech-insider.org/tauri-vs-electron-2026/)

### Inferences
- Ratios from the same-machine test: WPF .NET 8 (mean ~0.59 s) vs WinUI 3 AOT (mean ~0.135 s) is about 4.4x; WPF R2R (mean ~0.53 s) is about 1.1x better than plain WPF; .NET Framework 4.8.1 WPF (~0.315 s) starts about 1.9x faster than .NET 8 WPF for an empty window.
- Ratios from JIT to Native AOT on the same framework: 4.8x (WPF fork, 652 to 136 ms), 3.5x first frame and 3.9x interactive (Uno), 1.9x (Uno.Chefs on Windows). So "about 2x to 5x" is the documented band for AOT.
- Without AOT, the better-documented comparisons show alternatives only modestly ahead of or behind WPF (WinUI 3 ~1.4 s vs WPF ~1.8 s cold in one LOB test; Uno JIT first frame 1045 ms on Windows); the 240-310 ms figures for Avalonia/Uno/MAUI are low-confidence. Switching frameworks without also adopting Native AOT does not buy an order of magnitude.
- The native C++ row ("0 s" against ~0.6 s for WPF) is an unbounded ratio for an empty window, plausibly 50x or more; it shows where the time goes (runtime plus UI framework), not what a rewritten real application would achieve.

### Gaps
- No benchmark with stated hardware covers WPF on .NET 10 or 11 specifically; the WPF numbers above are .NET 8, .NET 9 and .NET 10 preview.
- No measured WinForms millisecond figure on modern .NET was found (only the ranking "fastest").
- No trustworthy Windows hello-world numbers for Electron/Tauri with a native control were retrieved.
- Cold (post-reboot, disk-bound) versus warm is rarely separated in these sources; only the Zenn test approximates cold start.

## B2. Avalonia with Native AOT; cost of porting a WPF window; Avalonia XPF

### Takeaway
Avalonia supports Native AOT and its headline "4x faster startup" figure is an Android, version-to-version vendor number, not a Windows JIT-vs-AOT measurement. Porting a styled WPF window means rewriting triggers, storyboards, effects and tooltips. XPF is a paid cross-platform WPF runtime (from EUR 9,500 per app) that supports Native AOT but makes no startup claim for Windows.

### Cited Findings
- Avalonia's vendor page: startup 1,960 ms (11.x) vs 460 ms (12.0), labelled "NativeAOT, 4x faster"; the block is titled "Avalonia 12.0 against 11.x, on Android"; device and method not stated. **[vendor, Android, not Windows]** — [Avalonia, for developers](https://avaloniaui.net/solutions/developer)
- Porting differences (official docs): styles and triggers are replaced by "a CSS-like system" (selectors, classes, pseudo-classes); data templates live in a `DataTemplates` collection; `StyledProperty`/`DirectProperty`/`AttachedProperty` instead of `DependencyProperty`; pointer events (`PointerPressed`) instead of mouse events; tunnelling via routing-strategy flags instead of `Preview*` events; "most controls, layout panels, data binding, and MVVM patterns work the same way or have a clear equivalent". — [Avalonia docs, WPF developers](https://docs.avaloniaui.net/docs/get-started/wpf/)
- Cheat-sheet specifics: "No triggers in Avalonia" (use `:pointerover`, `:pressed`, `:disabled`, `:checked`); `Storyboard`/`DoubleAnimation` become keyframe `Animation`s declared in `Style.Animations`; `VisualStateManager` becomes pseudo-classes; `DropShadowEffect` maps to `BoxShadow` on `Border` ("Different API, CSS-like") or the `Effect` property with `BlurEffect`/`DropShadowEffect`; tooltips use the attached property `ToolTip.Tip`; `Visibility` becomes `bool IsVisible` (no `Hidden`, use `Opacity="0"`); `LayoutTransform` needs `LayoutTransformControl`; `AllowsTransparency="True"` maps to `TransparencyLevelHint="Transparent"` without WPF-style transparent click-through; `WindowStyle="None"` maps to `WindowDecorations="None"`; compiled bindings via `{CompiledBinding}` or `x:CompileBindings="True"`. — [Avalonia docs, WPF cheat sheet](https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet)
- Avalonia XPF: "a cross-platform implementation of WPF that allows you to run your existing WPF applications" on macOS/Linux (mobile/WebAssembly in the top tier); "binary-compatible WPF runtime"; migration is one SDK reference change, it "does not convert or generate anything"; 429 third-party controls tested, 90% working (vendor figure); XPF 2.x requires .NET 10+, XPF 1.x .NET 6+. — [Avalonia XPF](https://avaloniaui.net/xpf)
- XPF pricing (perpetual, per application, 12 months of updates): Internal EUR 9,500; Business EUR 29,500 (Embedded Linux add-on +EUR 21,250); Enterprise from EUR 124,500; evaluation via contact form. **[vendor, page as of Nov 2026]** — [Avalonia XPF](https://avaloniaui.net/xpf); an older figure of EUR 19,500 for Business appeared in a search snippet of a pricing page that now returns 404, so prices have changed.
- XPF performance claim: only that for typical business UI "rendering performance is broadly comparable to WPF"; no startup claim. — [Avalonia XPF](https://avaloniaui.net/xpf)
- XPF and Native AOT: "Unlike WPF, XPF does not use COM marshalling, which allows it to be compatible with AOT compilation"; set `PublishAot=true`; "trimming and linking must be conservative"; the SDK ships an `rd.xml` rooting XPF, the built-in WPF assemblies and user assemblies; XAML-only types in third-party libraries may be removed unless rooted with `TrimmerRootAssembly`. No startup figure. — [Avalonia docs, XPF Native AOT](https://docs.avaloniaui.net/xpf/deployment/native-aot)

### Inferences
- For a hand-designed window that relies on WPF triggers, storyboards, drop-shadow/blur effects and transparent windows, an Avalonia port is a rewrite of the styling and animation layer, not a mechanical conversion; layout and bindings mostly carry over.
- XPF is positioned for cross-platform reach, not for Windows speed; using it purely as a "WPF with Native AOT on Windows" vehicle is technically conceivable from the docs but unmeasured, costs at least EUR 9,500, and conservative trimming limits the size benefit.

### Gaps
- No controlled Windows measurement of the same Avalonia app with JIT versus Native AOT was found.
- No measurement of XPF startup (JIT or AOT) versus WPF on Windows was found.
- Whether Avalonia's XAML compiler (XamlIl compiles XAML to IL at build time, unlike WPF's BAML which is parsed at runtime) materially shortens window creation was not confirmed or quantified in a fetched source.

## B3. Can WPF itself run with Native AOT or ahead-of-time compiled XAML in 2026?

### Takeaway
No. Officially, trimming is disabled for WPF in the .NET SDK and the tracking issue has sat in the "Future" milestone since 2021; a request for Native AOT was closed as a duplicate in November 2025. One community proof-of-concept fork runs a sample app under Native AOT at 136 ms, with many subsystems disabled.

### Cited Findings
- "almost no WPF apps are runnable after trimming, so trimming support for WPF is currently disabled in the .NET SDK"; reason: "substantial use of reflection" and reliance on "runtime code inspection". Built-in COM marshalling and C++/CLI are also listed as trimming-incompatible. — [Microsoft Learn, Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities) (updated Dec 2025)
- Tracking issue "WPF is not trim-compatible" (#3811): open since 13 Nov 2020, milestone "Future" (set Aug 2021), labels Enhancement Requested / Work Item; on 10 Nov 2025 issue #11205 "Make Wpf Native aot compatible" was marked as a duplicate of it; no newer maintainer roadmap statement on the page. — [dotnet/wpf issue #3811](https://github.com/dotnet/wpf/issues/3811)
- Community proof of concept: fork of dotnet/wpf release/10.0 with Reflection.Emit removed from the XAML runtime, BAML resolution restricted to already-loaded assemblies, DirectWrite font loaders ported from C++/CLI to C#, trimming annotations, and a source-generated `{CompiledBinding}`. Results on a sample calculator: 136 ms vs 652-728 ms, 56 MB vs 82 MB working set, 6 files / 63 MB (exe ~55 MB). — [ibebbs/wpfaot](https://github.com/ibebbs/wpfaot)
- Its limits: drag-drop, TSF/IME, spell check, printing gated or no-ops; accessibility/UI Automation "in progress"; Taskbar/JumpList and ActiveX "will crash if used in AOT"; no dynamic assembly loading; compiled bindings lack dotted paths, collections, `MultiBinding`, `RelativeSource`, `ElementName`, `StringFormat`, indexers; manual `TrimmerRoots.xml` entries needed. 14 commits, 4 stars; nothing about upstreaming. — [ibebbs/wpfaot](https://github.com/ibebbs/wpfaot), [fork copy lezzi/wpfaot](https://github.com/lezzi/wpfaot)
- A 2026 blog lists "Native AOT (preview)" numbers for WPF while saying WPF AOT is "not production-ready"; no source given. — [CTCO blog](https://www.ctco.blog/posts/winui-vs-wpf-2026-practical-comparison/)

### Inferences
- The fork proves the technical ceiling for WPF under AOT (about 4.8x on a small app) but is not usable for a shipping app: no IME, no accessibility, no drag-drop, and it requires building and maintaining a private WPF.
- WPF XAML remains BAML loaded and interpreted at runtime in official builds; there is no supported "compile XAML to code" switch.

### Gaps
- "What's new in WPF in .NET 11" (GA 10 Nov 2026 per search snippets) was not fetched; release-day coverage mentioned only reliability fixes for WPF. Whether .NET 11 changed anything about WPF trimming is unverified.
- The fork's full methodology file (`wpfaot-perf.md`) was not read.

## B4. Typical absolute numbers for a tuned WPF app, breakdown, and how large a speed-up is realistic

### Takeaway
An empty WPF window on .NET 8/10 appears about 0.5-0.7 s after process start on ordinary to fast hardware; a moderately complex LOB app was reported at ~0.9 s warm and ~1.8 s cold. Documented improvements are 1.1-1.5x from compilation settings and about 2-5x from Native AOT on managed stacks. No tool, setting or managed framework switch is documented anywhere near 100x. Two cases could reach that order of magnitude: a hand-written native Win32 window (measured at "0 s" for an empty window, i.e. a rewrite), and "show an already-built window in an already-running process", which no source measured.

### Cited Findings
- Empty WPF app floor: 0.50-0.68 s (.NET 8, 2-core VM, near-cold) — [Zenn](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en); 652 ms median for a small calculator (.NET 10 preview, i7-14700K) — [ibebbs/wpfaot](https://github.com/ibebbs/wpfaot).
- Moderately complex WPF LOB app, .NET 9, Ryzen 7: ~1.8 s cold, ~0.9 s warm. **[author's own test, no method]** — [CTCO blog](https://www.ctco.blog/posts/winui-vs-wpf-2026-practical-comparison/)
- Cold vs warm definition and diagnosis: cold = first start after reboot, dominated by disk page faults; "If all subsequent launches ... (warm startup) are much faster, your cold startup issue is most likely caused by I/O"; otherwise the cause is "lengthy initialization or computation, waits for some event to complete, or requires a lot of JIT compilation". — [Microsoft Learn, Application Startup Time](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time)
- What the runtime+framework share looks like: the same empty window is 0 s in native C++, ~0.1-0.2 s in WinUI 3 Native AOT, ~0.3 s in WPF on .NET Framework 4.8.1 and ~0.5-0.7 s in WPF on .NET 8. — [Zenn](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en)
- Magnitudes on record: R2R on empty WPF app ~nil to ~10% — [Zenn](https://zenn.dev/suusanex/articles/f642d91df412c9?locale=en); multicore JIT 16-35% on WPF apps (.NET FW) — [.NET Blog](https://devblogs.microsoft.com/dotnet/an-easy-solution-for-improving-app-launch-performance/); WinUI 3 AOT in Command Palette ~40% load time — [PowerToys v0.93.0](https://github.com/microsoft/PowerToys/releases/tag/v0.93.0); Native AOT 3.5-3.9x on Uno desktop — [Uno blog](https://platform.uno/blog/native-aot-in-uno-platform-faster-startup-five-platforms/); 4.8x on the WPF AOT fork — [ibebbs/wpfaot](https://github.com/ibebbs/wpfaot); perceived 13 s to 2-3 s with a splash on 2007 hardware — [jgoldb](https://learn.microsoft.com/en-us/archive/blogs/jgoldb/splash-screen-to-improve-wpf-application-perceived-cold-startup-performance).
- Reference point from Microsoft for a launcher-type window: 94 ms median "time-to-show" (WinUI 3 + Native AOT Run dialog), versus 103 ms for the old Win32 dialog on the same metric. The metric is not defined in the post and is evidently not process-start-to-window (a Win32 dialog scored 103 ms), so it is not comparable with the other numbers here and is not a floor. — [Windows Command Line blog](https://devblogs.microsoft.com/commandline/the-new-run-dialog-faster-cleaner-and-more-capable)

### Inferences
- Rough split for an empty WPF window on .NET 8 from the same-machine numbers: about 0.1-0.2 s is what a managed AOT XAML stack needs at all, about another 0.3-0.5 s is runtime start plus WPF initialisation plus JIT of the startup path. Anything an app shows beyond roughly 0.7 s warm is the app's own code, resources, data and layout, which is the part architecture tricks (A2-A5) can remove.
- Judging "extremely slow": if a WPF app's warm start is several seconds, or a dialog takes more than a few hundred milliseconds to open, most of that is app-level work, not the framework floor; in that situation app-level restructuring can plausibly deliver 3x-10x on those specific paths without leaving WPF. That estimate is reasoning from the floor numbers above, not a measured case.
- Realistic ceilings, by lever: compile/runtime switches about 1.1-1.5x; moving to a Native AOT-capable stack about 2-5x on process start; perceived start with splash/skeleton can feel much faster without changing real time. "100x" is not documented for any tool, setting or managed framework switch. Arithmetic: 100x on a 0.6 s empty-window floor would mean 6 ms process-start-to-window; the only measurement in that region is the empty native C++ window ("0 s", below resolution), and the managed AOT stacks sit at 90-300 ms.
- Within WPF, the only scenario where a ratio near 100x is arithmetically plausible is resident process plus pre-built hidden window (for example ~1.5 s cold start replaced by a window activation of a few tens of milliseconds). No source measured this; it should be presented as a hypothesis to be measured in the project, not as a documented result.

### Gaps
- No source gives a measured three-way breakdown (runtime start / WPF init / app code) for WPF on .NET 8/9/10; the split above is inferred from cross-stack differences.
- No published typical figure for "time to open a WPF dialog" (first open versus subsequent opens) was found.
- No well-documented case study of a WPF app going from slow to fast with before/after numbers on modern .NET was found.

## B5. "Black box" products that promise drastic .NET/WPF speed-ups

### Takeaway
I found no commercial or open-source product for .NET 8/9/10 WPF that acts as a drop-in startup accelerator. The historical "make .NET native" tools are either .NET Framework-only (NGen, MPGO), UWP-only (.NET Native, now legacy), or unverifiable here (Salamander, Xenocode Postbuild). The modern equivalents are built into the SDK (ReadyToRun, Native AOT), and Native AOT does not support WPF.

### Cited Findings
- NGen: recommended in the WPF startup doc ("you should always use Ngen.exe" for warm start) but it is a .NET Framework tool; ".NET/.NET Core projects don't support Ngen", ReadyToRun is the replacement. — [Microsoft Learn, Application Startup Time](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time), [DevExpress, 10 Tips](https://community.devexpress.com/blogs/wpf/archive/2023/01/26/9-tips-to-reduce-wpf-app-startup-time.aspx)
- MPGO (managed profile-guided optimisation of NGen image layout): .NET Framework 4.5 only, no numbers published in the post. — [.NET Blog, 2012](https://devblogs.microsoft.com/dotnet/improving-launch-performance-for-your-desktop-applications/)
- .NET Native: Microsoft describes it as the legacy precompilation technology for UWP apps, receiving only security and reliability fixes; UWP templates now target modern .NET with Native AOT. **[snippet, URL unconfirmed]** — [Microsoft Learn, Modernize UWP apps with .NET](https://learn.microsoft.com/windows/uwp/dotnet-native/modernize-uwp-apps-with-dotnet)
- Other items on the old Microsoft startup checklist that are .NET Framework-specific and do not transfer: GAC installation to skip strong-name verification, `generatePublisherEvidence` to skip Authenticode checks, domain-neutral AppDomains, Sgen for XmlSerializer, ClickOnce update timing, SuperFetch on Vista. — [Microsoft Learn, Application Startup Time](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time)
- Supported modern equivalents: ReadyToRun (and composite R2R) — [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run); `ProfileOptimization` — [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.profileoptimization); Native AOT, which officially excludes WPF — [Microsoft Learn, trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities).
- Products that do deliver AOT for a WPF-shaped codebase, with caveats: Avalonia XPF (commercial, from EUR 9,500 per app, AOT supported, no startup figure) — [Avalonia XPF](https://avaloniaui.net/xpf), [XPF Native AOT docs](https://docs.avaloniaui.net/xpf/deployment/native-aot); the wpfaot community fork (proof of concept, 136 ms vs 652 ms, many features disabled) — [ibebbs/wpfaot](https://github.com/ibebbs/wpfaot).
- An extended web search for commercial ".NET/WPF startup accelerator / optimizer / native compiler" products for .NET 8 returned only Microsoft's built-in options and vendor tip lists, no such product. — [DevExpress docs, Reduce Application Launch Time](https://docs.devexpress.com/GeneralInformation/400286/build-deploy/reduce-application-launch-time) (representative result)

### Inferences
- .NET Native was a UWP toolchain; I found no source describing it as ever applicable to WPF (background knowledge says it was not).
- Anything marketed in 2026 as a one-click "100x faster WPF" tool has no evidence behind it in the sources I could find; the strongest real effect from any tool-level change is Native AOT at about 2-5x, and that is unavailable for stock WPF.
- Obfuscators and "protectors" generally add startup work (decryption, anti-tamper checks) rather than remove it; this is a general expectation, not something a fetched source measured.

### Gaps
- Salamander .NET linker/native compiler (Remotesoft) and Xenocode Postbuild (later Spoon/Turbo Studio): no current source retrieved; from background knowledge both targeted .NET Framework and neither is known to support .NET 8+ WPF. Unverified.
- No independent test of any obfuscator/"optimizer" suite's effect on WPF startup under .NET 8+ was found.
- Platform status lines not verified in fetched pages: .NET 11 GA on 10 Nov 2026 and same-day servicing releases .NET 10.0.15 / 9.0.23 / 8.0.34 were seen only in search snippets; .NET 8 and .NET 9 end-of-support dates were not checked.
