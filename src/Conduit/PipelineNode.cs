namespace Conduit;

public enum TestState { Queued, Running, Passed, Failed, Aborted }

public enum LogKind { Run, Pass, Fail, Warn, Request, Response, Waiting }

/// <summary>One test of a node. Rows in the list bind to it and update in place when its state changes.</summary>
public sealed class TestItem : Observable
{
    internal TestItem(int no, string name, double secs)
    {
        No = no;
        Name = name;
        Secs = secs;
    }

    public int No { get; }
    public string Name { get; }
    public TestState State { get; private set; }
    public string Note { get; private set; } = "";
    public double Secs { get; private set; }
    public bool IsDone => State is TestState.Passed or TestState.Failed or TestState.Aborted;
    public string TimeText => Fmt.Inv($"{Secs:0.0}s");

    internal void Update(TestState state, string note = "", double? secs = null)
    {
        State = state;
        Note = note;
        Secs = secs ?? Secs;
        Raise(string.Empty);
    }
}

/// <summary>One line of the log panel ("00:46.05  RUN  #25 charges a saved card").</summary>
public sealed class LogLine(int node, string nodeName, string time, LogKind kind, string text, string dim) : Observable
{
    private bool _showCursor;

    public int Node { get; } = node;
    public string NodePrefix { get; } = $"[{nodeName}] ";
    public string Time { get; } = time;
    public LogKind Kind { get; } = kind;
    public string Text { get; } = text;
    public string Dim { get; } = dim;
    public string DimText => Dim.Length > 0 ? " " + Dim : "";

    public string Tag => Kind switch
    {
        LogKind.Pass => "PASS",
        LogKind.Run => "RUN",
        LogKind.Fail => "FAIL",
        LogKind.Warn => "WARN",
        LogKind.Request => "→",
        LogKind.Response => "←",
        _ => "…",
    };

    /// <summary>Blinking cursor behind the newest line while the run is live.</summary>
    public bool ShowCursor { get => _showCursor; set => Set(ref _showCursor, value); }

    public override string ToString() => $"{Time}  {Tag,-5} {Text} {Dim}".TrimEnd();
}

/// <summary>
/// One pipeline node (e.g. "Payments" on Node 18) with its tests, plus a fake runner that advances them.
/// When the real runner exists, replace <see cref="Tick"/> with its events – the UI only binds to the public state.
/// </summary>
public sealed class PipelineNode : Observable
{
    private readonly Dictionary<int, (TestState State, string Note)> _planned;
    private readonly (LogKind Kind, string Text, string Dim)[] _flavour;
    private readonly bool _randomFailures;
    private readonly List<TestItem> _tests = [];
    private double _wall, _target, _runningFor;
    private int _flavourIndex;
    private bool _flavourDone;

    private PipelineNode(string name, string runtime, List<string> testNames, Dictionary<int, (TestState, string)> planned,
        (LogKind, string, string)[] flavour, int seed, bool randomFailures)
    {
        Name = name;
        Runtime = runtime;
        _planned = planned;
        _flavour = flavour;
        _randomFailures = randomFailures;
        var rnd = new Random(seed);
        for (int i = 0; i < testNames.Count; i++)
        {
            int no = i + 1;
            double secs = planned.TryGetValue(no, out var p) && p.Item1 == TestState.Aborted ? 30.0 : Math.Round(0.2 + (rnd.NextDouble() * rnd.NextDouble() * 2.4), 1);
            _tests.Add(new TestItem(no, testNames[i], secs));
        }
    }

    public string Name { get; }
    public string Runtime { get; }
    public IReadOnlyList<TestItem> Tests => _tests;
    public bool Stopped { get; private set; }

    /// <summary>Bumped on every visible change, so the workspace only refreshes lists when needed.</summary>
    public int Version { get; private set; }

    public int Passed => Count(TestState.Passed);
    public int Failed => Count(TestState.Failed);
    public int Aborted => Count(TestState.Aborted);
    public int Queued => Count(TestState.Queued);
    public int Done => Passed + Failed + Aborted;
    public int Total => _tests.Count;
    public int Rest => Total - Done;
    public bool Finished => Stopped || Done == Total;
    public double Fraction => Total == 0 ? 1 : (double)Done / Total;

    /// <summary>All tests ran (check icon); a stopped node keeps its progress ring.</summary>
    public bool IsComplete => Finished && !Stopped;
    public bool HasFailed => Failed > 0;
    public bool HasAborted => Aborted > 0;
    public string ProgressText => $"{Done}/{Total}";
    public TestItem? RunningTest => _tests.Find(t => t.State == TestState.Running);
    public string RunningForText => Fmt.Inv($"{_runningFor:0.0}s");

    /// <summary>Advances the fake runner by <paramref name="dt"/> wall-clock seconds and reports log lines.</summary>
    public void Tick(double dt, Random rnd, Action<LogKind, string, string> log)
    {
        if (Finished) return;
        var test = RunningTest;
        if (test is null)
        {
            Start(_tests.Find(t => t.State == TestState.Queued), log);
            return;
        }

        _wall += dt;
        _runningFor = Math.Min(test.Secs, _wall / _target * test.Secs);
        Raise(nameof(RunningForText));

        if (!_flavourDone && _flavour.Length > 0 && _wall > _target * 0.5 && rnd.NextDouble() < 0.35)
        {
            var f = _flavour[_flavourIndex++ % _flavour.Length];
            log(f.Kind, f.Text, f.Dim);
            _flavourDone = true;
        }
        if (_wall < _target) return;

        // Finish the current test …
        var (state, note) = _planned.TryGetValue(test.No, out var p) ? p
            : _randomFailures && rnd.NextDouble() < 0.03 ? (TestState.Failed, RandomFailure(rnd))
            : _randomFailures && rnd.NextDouble() < 0.015 ? (TestState.Aborted, "runner lost connection")
            : (TestState.Passed, "");
        test.Update(state, note, state == TestState.Aborted ? 30.0 : null);
        switch (state)
        {
            case TestState.Passed: log(LogKind.Pass, $"#{test.No} {test.Name}", test.TimeText); break;
            case TestState.Failed: log(LogKind.Fail, $"#{test.No} {test.Name}", note); break;
            default: log(LogKind.Warn, $"#{test.No} {test.Name} aborted: {note}", ""); break;
        }
        // … and start the next one right away.
        Start(_tests.Find(t => t.State == TestState.Queued), log);
    }

    public void Stop()
    {
        if (Finished) return;
        RunningTest?.Update(TestState.Queued);
        Stopped = true;
        Changed();
    }

    private void Start(TestItem? test, Action<LogKind, string, string> log)
    {
        if (test is not null)
        {
            test.Update(TestState.Running);
            _wall = 0;
            _runningFor = 0;
            _flavourDone = false;
            // Wall-clock time of the demo: a compressed version of the test's own duration.
            _target = 0.35 + (Math.Min(test.Secs, 3) * 0.3);
            log(LogKind.Run, $"#{test.No} {test.Name}", "");
        }
        Changed();
    }

    private void Changed()
    {
        Version++;
        Raise(string.Empty);
    }

    private int Count(TestState state)
    {
        int n = 0;
        foreach (var t in _tests) if (t.State == state) n++;
        return n;
    }

    private static string RandomFailure(Random rnd) => rnd.Next(3) switch
    {
        0 => "expected 200, received 500",
        1 => "timeout after 5000ms",
        _ => "snapshot mismatch",
    };

    // ── Factories ──────────────────────────────────────────────────────────

    /// <summary>The demo data from the Paper design (Payments 50, Formatting 60, Queue worker 50 = 160 tests).</summary>
    public static List<PipelineNode> Sample(bool single)
    {
        var payments = new PipelineNode("Payments", "Node 18",
            Names(["creates a", "updates a", "validates the", "cancels a", "lists every"],
                ["customer", "invoice", "payout", "subscription", "coupon", "dispute", "tax rate", "refund", "price", "mandate"], 50,
                (20, "retries a failed webhook"), (21, "applies currency rounding"), (22, "sends receipt email"),
                (23, "rejects expired cards"), (24, "stores card token securely"), (25, "charges a saved card")),
            new() { [20] = (TestState.Failed, "expected 200, received 500"), [22] = (TestState.Aborted, "runner lost connection") },
            [
                (LogKind.Request, "POST /v1/charges", "{ amount: 1299, currency: \"eur\" }"),
                (LogKind.Response, "201 Created", "ch_3PqLx2 · 571ms"),
                (LogKind.Warn, "webhook delivery slow, retrying in 250ms", ""),
                (LogKind.Waiting, "waiting for", "charge.succeeded"),
                (LogKind.Request, "GET /v1/customers/cus_92", ""),
                (LogKind.Response, "200 OK", "cus_92 · 88ms"),
            ], 18, false);
        if (single) return [payments];

        var formatting = new PipelineNode("Formatting", "Node 20",
            Names(["formats", "parses", "rounds", "localizes", "pads"],
                ["dates", "currency per locale", "numbers", "percentages", "plurals", "addresses", "phone numbers", "time zones", "lists", "units", "durations", "file sizes"], 60),
            [],
            [(LogKind.Request, "load locale de-DE", ""), (LogKind.Response, "locale ready", "41ms"), (LogKind.Request, "load locale ja-JP", "")], 20, false);

        var queue = new PipelineNode("Queue worker", "Node 22",
            Names(["enqueues", "processes", "acks", "requeues", "drops"],
                ["a job", "a batch", "a delayed job", "a priority job", "a cron job", "a webhook event", "an email", "an export", "a retry", "a large payload"], 50,
                (28, "dead-letters after 3 attempts")),
            new() { [28] = (TestState.Aborted, "worker restarted mid-test") },
            [(LogKind.Request, "PUSH jobs:default", "id=8812"), (LogKind.Response, "ACK", "8812 · 12ms"), (LogKind.Warn, "queue depth above 500", "")], 22, false);

        return [payments, formatting, queue];
    }

    /// <summary>A node for a real test folder. Tests are simulated, roughly 4 per spec file.</summary>
    public static PipelineNode ForFolder(string name, int specFiles, int seed) => new(
        name, "", Names(["runs", "checks", "verifies", "covers", "asserts"],
            ["happy path", "empty input", "invalid input", "edge cases", "error handling", "retries", "timeouts", "permissions", "pagination", "caching"],
            Math.Clamp(specFiles * 4, 12, 120)),
        [], [(LogKind.Request, "spawn worker", ""), (LogKind.Response, "worker ready", "")], seed, true);

    /// <summary>verb × noun test names, with fixed names at given (1-based) positions.</summary>
    private static List<string> Names(string[] verbs, string[] nouns, int count, params (int No, string Name)[] fixedNames)
    {
        var names = new List<string>(count);
        for (int i = 0; names.Count < count; i++)
            names.Add($"{verbs[(i / nouns.Length) % verbs.Length]} {nouns[i % nouns.Length]}" + (i >= verbs.Length * nouns.Length ? $" #{i}" : ""));
        foreach (var (no, name) in fixedNames) names[no - 1] = name;
        return names;
    }
}
